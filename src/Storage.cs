using System;
using System.IO;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace AudioRoute
{
    public sealed class ConfigStore
    {
        public readonly string DirectoryPath;
        public string FilePath { get { return Path.Combine(DirectoryPath, "settings.json"); } }
        public string Warning;
        public ConfigStore(string directory) { DirectoryPath = directory; }
        public Settings Load()
        {
            if (!File.Exists(FilePath)) return new Settings();
            try { return Read(FilePath); }
            catch (Exception e)
            {
                Warning = "配置读取失败，原文件已保留：" + e.Message;
                // 保存故障文件后才允许后续覆盖；备份副本也保留，便于用户恢复。
                File.Copy(FilePath, FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                try { Settings backup = Read(FilePath + ".bak"); Warning += "；已加载上一份备份。"; return backup; }
                catch { Warning += "；已使用空配置。"; return new Settings(); }
            }
        }
        private static Settings Read(string path)
        {
            using (var stream = File.OpenRead(path))
            { var settings = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream); settings.Validate(); return settings; }
        }
        public void Save(Settings settings)
        {
            settings.Validate();
            Directory.CreateDirectory(DirectoryPath);
            string temp = FilePath + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, settings); stream.Flush(true); }
            // 同卷原子替换，保留上一版，避免进程被终止时留下半个 JSON。
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
        }
        public void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string path = Path.Combine(DirectoryPath, "events.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1048576)
                { File.Copy(path, path + ".old", true); File.WriteAllText(path, ""); }
                File.AppendAllText(path, DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
            }
            catch { /* 日志故障不应终止设备控制。 */ }
        }
    }
    public static class StartupRegistration
    {
        private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "AudioRoute";
        public static bool Enabled
        {
            get { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Key)) return key != null && key.GetValue(Name) != null; }
        }
        public static void Set(bool enabled, string executable)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Key))
            { if (enabled) key.SetValue(Name, "\"" + executable + "\" --tray"); else key.DeleteValue(Name, false); }
        }
    }
}
