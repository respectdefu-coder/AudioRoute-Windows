using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace AudioRoute
{
    public interface IAudioBackend
    {
        List<AudioDevice> Enumerate();
        string[] Defaults();
        void Switch(string id, bool communications);
        VolumeState GetVolume(string id);
        void SetVolume(string id, float level);
        void SetMute(string id, bool muted);
    }

    public sealed class CoreAudio : IAudioBackend
    {
        private static IMMDeviceEnumerator Enumerator()
        { return (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))); }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        private static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
        public List<AudioDevice> Enumerate()
        {
            IMMDeviceEnumerator enumerator = Enumerator(); IMMDeviceCollection collection = null;
            try
            {
                // eRender=0，DEVICE_STATE_ACTIVE=1：只列出当前可用的输出端点。
                Check(enumerator.EnumAudioEndpoints(0, 1, out collection));
                uint count; Check(collection.GetCount(out count));
                var result = new List<AudioDevice>();
                for (uint index = 0; index < count; index++)
                {
                    IMMDevice device = null; IPropertyStore store = null;
                    try
                    {
                        Check(collection.Item(index, out device));
                        string id; Check(device.GetId(out id));
                        Check(device.OpenPropertyStore(0, out store));
                        var key = new PropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
                        PropVariant value; Check(store.GetValue(ref key, out value));
                        string name;
                        try { name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : id; }
                        finally { PropVariantClear(ref value); }
                        result.Add(new AudioDevice { Id = id, Name = name ?? id });
                    }
                    catch (COMException) { /* 拔插竞争：下个采样周期重试该设备。 */ }
                    finally { Release(store); Release(device); }
                }
                return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            }
            finally { Release(collection); Release(enumerator); }
        }
        public string[] Defaults()
        {
            IMMDeviceEnumerator enumerator = Enumerator();
            try
            {
                var result = new string[3];
                for (int role = 0; role < 3; role++)
                {
                    IMMDevice device = null;
                    try
                    {
                        int hr = enumerator.GetDefaultAudioEndpoint(0, role, out device);
                        if (hr == unchecked((int)0x80070490)) continue; // 无输出端点
                        Check(hr); string id; Check(device.GetId(out id)); result[role] = id;
                    }
                    finally { Release(device); }
                }
                return result;
            }
            finally { Release(enumerator); }
        }
        public static bool IsDefault(string[] defaults, string id, bool communications)
        { return !String.IsNullOrEmpty(id) && defaults.Take(communications ? 3 : 2).All(x => x == id); }

        private static void WithVolume(string id, Action<IAudioEndpointVolume> action)
        {
            IMMDeviceEnumerator enumerator = Enumerator(); IMMDevice device = null;
            IAudioEndpointVolume volume = null; IntPtr pointer = IntPtr.Zero;
            try
            {
                Check(enumerator.GetDevice(id, out device));
                var iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
                Check(device.Activate(ref iid, 23, IntPtr.Zero, out pointer));
                volume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(pointer);
                action(volume);
            }
            finally
            {
                Release(volume);
                if (pointer != IntPtr.Zero) Marshal.Release(pointer);
                Release(device); Release(enumerator);
            }
        }
        public VolumeState GetVolume(string id)
        {
            var state = new VolumeState();
            WithVolume(id, delegate(IAudioEndpointVolume volume)
            { Check(volume.GetMasterVolumeLevelScalar(out state.Level)); Check(volume.GetMute(out state.Muted)); });
            return state;
        }
        public void SetVolume(string id, float level)
        {
            if (Single.IsNaN(level) || level < 0 || level > 1) throw new ArgumentOutOfRangeException("level");
            // 调整端点主音量，范围 0..1；不修改应用混音器音量，不自动解除静音。
            WithVolume(id, delegate(IAudioEndpointVolume volume)
            { Guid context = Guid.Empty; Check(volume.SetMasterVolumeLevelScalar(level, ref context)); });
        }
        public void SetMute(string id, bool muted)
        {
            WithVolume(id, delegate(IAudioEndpointVolume volume)
            { Guid context = Guid.Empty; Check(volume.SetMute(muted, ref context)); });
        }

        public static void ProbePolicy()
        {
            object policy = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")));
            try { if (!(policy is IPolicyConfig)) throw new NotSupportedException("此系统不支持 IPolicyConfig。"); }
            finally { Release(policy); }
        }
        public void Switch(string id, bool communications)
        {
            if (!Enumerate().Any(d => d.Id == id)) throw new InvalidOperationException("目标设备已离线，请重新连接或选择其他设备。");
            string[] before = Defaults();
            IPolicyConfig policy = (IPolicyConfig)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")));
            var changed = new List<int>();
            try
            {
                // Core Audio 公共接口只能读取默认端点。设置使用未公开的 PolicyConfig；
                // 按 Console/Multimedia/可选 Communications 分角色切换，不重启音频服务。
                for (int role = 0; role < (communications ? 3 : 2); role++)
                {
                    if (before[role] == id) continue;
                    Check(policy.SetDefaultEndpoint(id, role)); changed.Add(role);
                }
                if (!IsDefault(Defaults(), id, communications)) throw new InvalidOperationException("系统未确认默认设备变更。");
            }
            catch (Exception e)
            {
                bool rollbackFailed = false;
                foreach (int role in changed)
                {
                    if (before[role] == null) { rollbackFailed = true; continue; }
                    try { Check(policy.SetDefaultEndpoint(before[role], role)); } catch { rollbackFailed = true; }
                }
                throw new InvalidOperationException("切换失败" + (rollbackFailed ? "，部分角色未能恢复，请检查 Windows 声音设置" : "，已恢复原设备") + "：" + e.Message, e);
            }
            finally { Release(policy); }
        }
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] internal struct PropVariant
    { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, out IntPtr result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        // 前十个方法保留真实 COM vtable 顺序；本工具只调用 SetDefaultEndpoint。
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int mode, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpoint, IntPtr mix);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int mode, IntPtr normal, IntPtr minimum);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint steps);
        [PreserveSig] int VolumeStepUp(ref Guid context);
        [PreserveSig] int VolumeStepDown(ref Guid context);
        [PreserveSig] int QueryHardwareSupport(out uint mask);
        [PreserveSig] int GetVolumeRange(out float minimum, out float maximum, out float increment);
    }
}
