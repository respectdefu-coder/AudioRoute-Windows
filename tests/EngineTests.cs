using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AudioRoute;

internal static class EngineTests
{
    private static int count;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
    public static int Main(string[] args)
    {
        try
        {
            var sample = new ActivitySnapshot();
            sample.Processes.Add(new ProcessEntry { Id = 10, Name = "vlc", Path = @"C:\Apps\vlc.exe" });
            sample.Processes.Add(new ProcessEntry { Id = 20, Name = "msedge", Path = @"C:\Apps\msedge.exe" });
            sample.Processes.Add(new ProcessEntry { Id = 30, Name = "notepad", Path = "" });
            sample.Windows.Add(new WindowEntry { ProcessId = 20, Title = "Watch | video.example.com — Microsoft Edge", Foreground = true });
            sample.Windows.Add(new WindowEntry { ProcessId = 30, Title = "YouTube notes", Foreground = false });
            var process = new Rule { Kind = TriggerKind.ProcessName, Pattern = "VLC.EXE", DeviceId = "headphones", Priority = 100 };
            var browser = new Rule { Kind = TriggerKind.BrowserTitle, Pattern = "Watch", DeviceId = "speaker", Priority = 200 };
            var ids = new HashSet<string> { "speaker", "headphones" };
            Check(RuleMatcher.Matches(process, sample), "case insensitive process .exe suffix");
            sample.Processes.Add(new ProcessEntry { Id = 40, Name = "my.player", Path = "" });
            Check(RuleMatcher.Matches(new Rule { Kind = TriggerKind.ProcessName, Pattern = "my.player" }, sample), "process name containing a dot is preserved");
            process.Pattern = "vl"; Check(!RuleMatcher.Matches(process, sample), "process name must be exact"); process.Pattern = "vlc";
            process.ForegroundOnly = true; Check(!RuleMatcher.Matches(process, sample), "background process excluded for foreground rule"); process.ForegroundOnly = false;
            var path = new Rule { Kind = TriggerKind.ExecutablePath, Pattern = @"c:\apps\VLC.exe" };
            Check(RuleMatcher.Matches(path, sample), "full path case insensitive");
            path.Pattern = @"C:\Other\vlc.exe"; Check(!RuleMatcher.Matches(path, sample), "same executable in other directory excluded");
            Check(RuleMatcher.Matches(browser, sample), "browser title detected");
            browser.Pattern = "YouTube"; Check(!RuleMatcher.Matches(browser, sample), "non-browser title excluded"); browser.Pattern = "Watch";
            var title = new Rule { Kind = TriggerKind.WindowTitle, Pattern = "YouTube" };
            Check(RuleMatcher.Matches(title, sample), "generic window title match");
            title.ForegroundOnly = true; Check(!RuleMatcher.Matches(title, sample), "window foreground filter");
            var domain = new Rule { Kind = TriggerKind.BrowserDomain, Pattern = "https://example.com/path" };
            Check(RuleMatcher.Matches(domain, sample), "domain token allows subdomain in browser title");
            sample.Windows[0].Title = "notexample.com example.com.evil.test";
            Check(!RuleMatcher.Matches(domain, sample), "domain false-positive boundaries excluded");
            sample.Windows[0].Title = "普通视频标题"; Check(!RuleMatcher.Matches(domain, sample), "domain not guessed from title without domain");
            Check(RuleMatcher.NormalizeDomain("bad domain") == null, "invalid domain rejected");
            sample.Windows[0].Title = "Watch";
            var rules = new List<Rule> { process, browser };
            Check(RuleMatcher.Select(rules, sample, ids) == browser, "higher priority wins");
            browser.Priority = 100; Check(RuleMatcher.Select(rules, sample, ids) == process, "stable equal-priority ordering");
            browser.Priority = 200; browser.Enabled = false; Check(RuleMatcher.Select(rules, sample, ids) == process, "disabled rule skipped"); browser.Enabled = true;
            ids.Remove("speaker"); Check(RuleMatcher.Select(rules, sample, ids) == process, "offline high-priority device skipped"); ids.Add("speaker");
            sample.Processes.RemoveAll(p => p.Id == 10); browser.Enabled = false;
            Check(RuleMatcher.Select(rules, sample, ids) == null, "process exit releases rule");
            var settings = new Settings { FallbackDeviceId = "speaker" };
            Check(SwitchPlanner.Desired(settings, null, ids) == "speaker", "idle fallback selected");
            settings.ManualDeviceId = "headphones";
            Check(SwitchPlanner.Desired(settings, null, ids) == "headphones", "manual selection retained during idle");
            Check(SwitchPlanner.Desired(settings, browser, ids) == "speaker", "active automatic rule takes precedence over manual");
            Check(SwitchPlanner.Desired(settings, null, ids) == "headphones", "manual choice restored after rule ends");
            ids.Remove("headphones"); Check(SwitchPlanner.Desired(settings, null, ids) == "speaker", "offline manual device uses fallback");
            ids.Remove("speaker"); Check(SwitchPlanner.Desired(settings, null, ids) == null, "all devices offline means no switching");
            settings.Paused = true; Check(SwitchPlanner.Desired(settings, browser, ids) == null, "pause prevents all auto switching");
            var planner = new SwitchPlanner();
            Check(planner.Plan("A", false, true, 0) == null && planner.Plan("A", false, true, 999) == null, "activation debounce");
            Check(planner.Plan("A", false, true, 1000) == "A", "stable activation becomes actionable"); planner.Succeeded(1000);
            Check(planner.Plan("A", true, true, 5000) == null, "already default avoids redundant writes");
            Check(planner.Plan("B", false, false, 1600) == null && planner.Plan("B", false, false, 3100) == null, "cooldown and release debounce");
            Check(planner.Plan("B", false, false, 3500) == "B", "release becomes actionable after cooldown"); planner.Failed(3500);
            Check(planner.Plan("B", false, false, 8499) == null && planner.Plan("B", false, false, 8500) == "B", "failed switch backs off five seconds");
            planner = new SwitchPlanner(); planner.Plan("A", false, true, 0); planner.Plan("B", false, true, 900);
            Check(planner.Plan("A", false, true, 1000) == null && planner.Plan("A", false, true, 1900) == null, "flapping target restarts debounce");
            planner.Manual(2000); Check(planner.Plan("A", false, true, 2500) == null && planner.Plan("A", false, true, 4499) == null, "manual switch has cooldown");
            Check(!CoreAudio.IsDefault(new [] { "A", "B", "A" }, "A", false), "both multimedia and console roles checked");
            Check(CoreAudio.IsDefault(new [] { "A", "A", "B" }, "A", false) && !CoreAudio.IsDefault(new [] { "A", "A", "B" }, "A", true), "communications role opt-in");
            string testRoot = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "AudioRouteTests");
            string directory = Path.Combine(testRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var store = new ConfigStore(directory); var config = new Settings { FallbackDeviceId = "speaker", ManualDeviceId = "headphones" }; config.Rules.Add(process);
            store.Save(config); var loaded = store.Load();
            Check(loaded.Rules.Count == 1 && loaded.Rules[0].Pattern == "vlc" && loaded.ManualDeviceId == "headphones", "JSON roundtrip including manual selection");
            config.ManualDeviceId = "speaker"; store.Save(config);
            Check(File.Exists(store.FilePath + ".bak") && store.Load().ManualDeviceId == "speaker", "atomic save creates backup");
            File.WriteAllText(store.FilePath, "{ corrupt"); loaded = store.Load();
            Check(loaded.ManualDeviceId == "headphones" && store.Warning != null, "corrupt config recovers previous backup");
            Check(Directory.GetFiles(directory, "*.corrupt-*").Length == 1, "corrupt original preserved");
            Console.WriteLine("All " + count + " tests passed. No system default audio device was changed."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
