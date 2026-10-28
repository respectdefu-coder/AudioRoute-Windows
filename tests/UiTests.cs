using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using AudioRoute;

internal sealed class FakeAudio : IAudioBackend
{
    public string Current = "speaker";
    public int SwitchCount;
    public float Level = .64F;
    public bool Muted;
    public List<AudioDevice> Enumerate() { return new List<AudioDevice> { new AudioDevice { Id = "speaker", Name = "内置扬声器（测试设备）" }, new AudioDevice { Id = "headphones", Name = "USB 耳机（测试设备）" } }; }
    public string[] Defaults() { return new[] { Current, Current, "speaker" }; }
    public void Switch(string id, bool communications) { Current = id; SwitchCount++; }
    public VolumeState GetVolume(string id) { return new VolumeState { Level = Level, Muted = Muted }; }
    public void SetVolume(string id, float value) { Level = value; }
    public void SetMute(string id, bool value) { Muted = value; }
}
internal static class UiTests
{
    private static int count;
    private static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }
    private static void Call(object instance, string name) { instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(instance, null); }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); count++; }
    private static void Pump(int milliseconds) { var clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(20); } }
    private static void Screenshot(Form form, string path) { using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(path); } }
    [STAThread]
    public static int Main(string[] args)
    {
        MainForm main = null;
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root = args[0]; Directory.CreateDirectory(root);
            var audio = new FakeAudio(); var config = new Settings { FallbackDeviceId = "speaker" };
            config.Rules.Add(new Rule { Name = "测试进程运行时使用耳机", Kind = TriggerKind.ProcessName, Pattern = Process.GetCurrentProcess().ProcessName, DeviceId = "headphones", DeviceName = "USB 耳机（测试设备）" });
            var store = new ConfigStore(Path.Combine(root, "ui-config-" + Guid.NewGuid().ToString("N")));
            main = new MainForm(config, store, audio, false, false); main.Show(); Pump(3500);
            using (var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AudioRoute.App.ico"))
                Check(iconStream != null, "custom multi-size icon is embedded in application assembly");
            Check(Object.ReferenceEquals(main.Icon, Theme.AppIcon) && Object.ReferenceEquals(Field<NotifyIcon>(main, "tray").Icon, Theme.AppIcon), "main window and tray use the custom application icon");
            Check(audio.Current == "headphones" && audio.SwitchCount == 1, "real process sampling drives one automatic switch through fake audio backend");
            Check(ActivityMonitor.Capture(false).Windows.Any(w => w.Title.Contains("AudioRoute")), "native EnumWindows observes a live application window");
            var fallback = Field<ComboBox>(main, "fallback");
            Check(fallback.SelectedItem != null && ((AudioDevice)fallback.SelectedItem).Id == "speaker", "fallback combo retains configured device after showing form");
            Check(Field<TrackBar>(main, "volume").Value == 64, "volume shown from current endpoint");
            Field<TrackBar>(main, "volume").Value = 37; Call(main, "AdjustVolume");
            Check(Math.Abs(audio.Level - .37F) < .001, "slider writes normalized endpoint volume");
            Field<CheckBox>(main, "muted").Checked = true; Check(audio.Muted, "mute control writes endpoint mute");
            audio.Level = .82F; audio.Muted = false; Call(main, "UpdateVolume");
            Check(Field<TrackBar>(main, "volume").Value == 82 && !Field<CheckBox>(main, "muted").Checked, "external volume and mute changes reflected");
            main.Hide();
            Check(InstanceActivation.RequestActivation(), "second-launch activation message reaches hidden main window");
            Pump(150); Check(main.Visible && main.WindowState == FormWindowState.Normal, "hidden tray window is restored without duplicate-instance dialog");
            main.WindowState = FormWindowState.Minimized; Pump(100);
            Check(InstanceActivation.RequestActivation(), "second-launch activation message reaches minimized window");
            Pump(150); Check(main.WindowState == FormWindowState.Normal, "minimized window is restored on repeated launch");
            Screenshot(main, Path.Combine(root, "main-ui.png"));
            config.Rules[0].Enabled = false;
            typeof(MainForm).GetMethod("ManualSwitch", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(AudioDevice) }, null).Invoke(main, new object[] { audio.Enumerate()[0] });
            int calls = audio.SwitchCount; Pump(3300);
            Check(audio.Current == "speaker" && audio.SwitchCount == calls && store.Load().ManualDeviceId == "speaker", "manual switch persists and remains stable without matching rules");
            int pickerCalls = 0; IWin32Window pickerOwner = null;
            string pickedFile = @"C:\Apps\Audio Player\My.Player.exe";
            using (var browserEditor = new RuleDialog(null, audio.Enumerate(), delegate(IWin32Window owner) { pickerCalls++; pickerOwner = owner; return pickedFile; }))
            {
                browserEditor.Show(); Application.DoEvents();
                var browseButton = Field<Button>(browserEditor, "browse");
                Check(browseButton.Visible && browseButton.Enabled, "browse is available for the default process-name trigger");
                browseButton.PerformClick();
                Check(pickerCalls == 1 && pickerOwner == browserEditor && Field<TextBox>(browserEditor, "pattern").Text == "My.Player.exe", "clicking browse opens the picker and extracts the executable name");
                Field<ComboBox>(browserEditor, "kind").SelectedIndex = (int)TriggerKind.ExecutablePath;
                browseButton.PerformClick();
                Check(pickerCalls == 2 && Field<TextBox>(browserEditor, "pattern").Text == pickedFile, "path trigger keeps the full selected executable path");
                pickedFile = null; browseButton.PerformClick();
                Check(pickerCalls == 3 && Field<TextBox>(browserEditor, "pattern").Text == @"C:\Apps\Audio Player\My.Player.exe", "canceling executable selection keeps the existing pattern");
                foreach (TriggerKind textKind in new[] { TriggerKind.WindowTitle, TriggerKind.BrowserTitle, TriggerKind.BrowserDomain })
                {
                    Field<ComboBox>(browserEditor, "kind").SelectedIndex = (int)textKind;
                    Check(!browseButton.Visible, "file browse hidden for " + textKind);
                }
                browserEditor.Close();
            }
            using (var editor = new RuleDialog(null, audio.Enumerate()))
            {
                editor.Show(); Application.DoEvents();
                Check(Field<ComboBox>(editor, "device").SelectedIndex == 0, "new rule displays the first available device");
                Field<TextBox>(editor, "name").Text = "视频网页";
                Field<ComboBox>(editor, "kind").SelectedIndex = (int)TriggerKind.BrowserTitle;
                Field<TextBox>(editor, "pattern").Text = "YouTube";
                Field<ComboBox>(editor, "device").SelectedIndex = 1;
                Screenshot(editor, Path.Combine(root, "rule-editor.png"));
                Call(editor, "SaveRule");
                Check(editor.Result != null && editor.Result.Kind == TriggerKind.BrowserTitle && editor.Result.Pattern == "YouTube" && editor.Result.DeviceId == "headphones", "rule editor saves typed trigger and selected output");
            }
            using (var empty = new MainForm(new Settings(), store, audio, false, true))
            {
                empty.Show(); Application.DoEvents();
                Check(Field<ComboBox>(empty, "fallback").SelectedIndex == 0, "first launch explicitly displays keep-current fallback option");
                empty.Close();
            }
            Console.WriteLine("All " + count + " UI integration tests passed. Audio backend was simulated; system audio was not changed.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { if (main != null) { typeof(MainForm).GetField("exiting", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(main, true); main.Close(); main.Dispose(); } }
    }
}
