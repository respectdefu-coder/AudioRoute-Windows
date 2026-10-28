using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AudioRoute
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length >= 2 && args[0] == "--diagnose")
                {
                    var audio = new CoreAudio(); var devices = audio.Enumerate(); string[] defaults = audio.Defaults();
                    var lines = new StringBuilder(); lines.AppendLine("AudioRoute read-only diagnostics");
                    lines.AppendLine("OS: " + Environment.OSVersion); lines.AppendLine("64-bit process: " + Environment.Is64BitProcess);
                    CoreAudio.ProbePolicy(); lines.AppendLine("PolicyConfig interface: available (no switch performed)");
                    foreach (AudioDevice device in devices) lines.AppendLine(device.Name + " | " + device.Id);
                    for (int i = 0; i < defaults.Length; i++) lines.AppendLine("Default role " + i + ": " + defaults[i]);
                    if (defaults[1] != null) { VolumeState volume = audio.GetVolume(defaults[1]); lines.AppendLine("Volume: " + volume.Level + "; mute: " + volume.Muted); }
                    ActivitySnapshot sample = ActivityMonitor.Capture(true);
                    lines.AppendLine("Processes: " + sample.Processes.Count + "; windows: " + sample.Windows.Count);
                    lines.AppendLine("Readable process paths: " + sample.Processes.Count(p => !String.IsNullOrEmpty(p.Path)));
                    File.WriteAllText(args[1], lines.ToString(), Encoding.UTF8); return 0;
                }
                if (args.Length >= 2 && args[0] == "--preview")
                {
                    using (var form = new MainForm(new Settings(), new ConfigStore(Path.GetDirectoryName(Path.GetFullPath(args[1]))), new CoreAudio(), false, true))
                    {
                        form.Show(); Application.DoEvents();
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png); }
                        form.Close();
                    }
                    return 0;
                }
                bool created;
                using (var mutex = new Mutex(true, @"Local\AudioRoute.Desktop.v1", out created))
                {
                    if (!created)
                    {
                        // 再次双击 EXE 或快捷方式时，直接唤出现有实例，不弹提示框。
                        InstanceActivation.RequestActivation(); return 0;
                    }
                    try
                    {
                        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioRoute");
                        var store = new ConfigStore(directory);
                        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                        { store.Log("UI error: " + e.Exception); MessageBox.Show(e.Exception.Message, "AudioRoute 运行错误"); };
                        Application.Run(new MainForm(store.Load(), store, new CoreAudio(), args.Contains("--tray"), false));
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception e)
            {
                if (args.Length >= 2 && (args[0] == "--diagnose" || args[0] == "--preview"))
                    File.WriteAllText(args[1] + ".error.txt", e.ToString());
                else MessageBox.Show(e.ToString(), "AudioRoute 无法启动");
                return 1;
            }
        }
    }
}
