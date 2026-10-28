using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AudioRoute
{
    public enum TriggerKind { ProcessName, ExecutablePath, WindowTitle, BrowserTitle, BrowserDomain }

    [DataContract]
    public sealed class Rule
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Name = "新规则";
        [DataMember] public TriggerKind Kind;
        [DataMember] public string Pattern = "";
        [DataMember] public string DeviceId = "";
        [DataMember] public string DeviceName = "";
        [DataMember] public bool Enabled = true;
        [DataMember] public bool ForegroundOnly;
        [DataMember] public int Priority = 100;
        public Rule Copy() { return (Rule)MemberwiseClone(); }
    }

    [DataContract]
    public sealed class Settings
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<Rule> Rules = new List<Rule>();
        [DataMember] public string FallbackDeviceId = "";
        [DataMember] public string ManualDeviceId = "";
        [DataMember] public bool IncludeCommunications;
        [DataMember] public bool Paused;
        public void Validate()
        {
            if (Version != 1) throw new System.IO.InvalidDataException("不支持的配置版本：" + Version);
            if (Rules == null) Rules = new List<Rule>();
            Rules.RemoveAll(r => r == null);
            foreach (Rule r in Rules)
            {
                if (String.IsNullOrEmpty(r.Id)) r.Id = Guid.NewGuid().ToString("N");
                r.Pattern = r.Pattern ?? "";
                r.DeviceId = r.DeviceId ?? "";
                r.Name = r.Name ?? "规则";
                r.Priority = Math.Max(0, Math.Min(9999, r.Priority));
            }
        }
    }

    public sealed class AudioDevice
    {
        public string Id;
        public string Name;
        public override string ToString() { return Name; }
    }
    public sealed class VolumeState
    {
        public float Level;
        public bool Muted;
    }
    public sealed class ProcessEntry
    {
        public int Id;
        public string Name;
        public string Path;
    }
    public sealed class WindowEntry
    {
        public int ProcessId;
        public string Title;
        public bool Foreground;
    }
    public sealed class ActivitySnapshot
    {
        public List<ProcessEntry> Processes = new List<ProcessEntry>();
        public List<WindowEntry> Windows = new List<WindowEntry>();
    }
}
