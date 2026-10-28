using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace AudioRoute
{
    // 将“再次启动”转为发给现有主窗口的激活消息。隐藏到托盘的窗口也能找到；
    // 不启动第二套监听或托盘图标。测试程序集使用独立标记，避免激活用户真实实例。
    internal static class InstanceActivation
    {
        private static readonly string Marker = "AudioRoute.MainWindow." + Assembly.GetEntryAssembly().GetName().Name;
        public static readonly uint ShowMessage = RegisterWindowMessage("AudioRoute.ShowMainWindow.v1");
        private delegate bool EnumWindowsProc(IntPtr window, IntPtr state);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetProp(IntPtr window, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetProp(IntPtr window, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr RemoveProp(IntPtr window, string name);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        public static void Register(IntPtr window)
        {
            if (ShowMessage == 0 || !SetProp(window, Marker, new IntPtr(1)))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法注册窗口激活通知。");
        }
        public static void Unregister(IntPtr window) { RemoveProp(window, Marker); }
        public static bool RequestActivation()
        {
            // 首个实例可能刚获得互斥锁但尚未创建窗口；短暂重试处理快速连续双击。
            var wait = Stopwatch.StartNew();
            do
            {
                IntPtr target = IntPtr.Zero;
                EnumWindows(delegate(IntPtr window, IntPtr state)
                {
                    if (GetProp(window, Marker) == IntPtr.Zero) return true;
                    target = window; return false;
                }, IntPtr.Zero);
                if (target != IntPtr.Zero)
                {
                    uint processId; GetWindowThreadProcessId(target, out processId);
                    // 新启动实例因用户双击而具备前台权限，将其仅授权给已有实例。
                    AllowSetForegroundWindow(processId);
                    return PostMessage(target, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                }
                Thread.Sleep(100);
            } while (wait.ElapsedMilliseconds < 2500);
            return false;
        }
    }
}
