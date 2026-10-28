using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AudioRoute
{
    public static class RuleMatcher
    {
        private static readonly HashSet<string> Browsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore", "arc" };

        public static string NormalizeDomain(string value)
        {
            Uri uri;
            string text = (value ?? "").Trim();
            if (!text.Contains("://")) text = "https://" + text;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") ||
                Uri.CheckHostName(uri.Host) != UriHostNameType.Dns || !uri.Host.Contains(".")) return null;
            return uri.IdnHost.ToLowerInvariant();
        }

        public static bool Matches(Rule rule, ActivitySnapshot snapshot)
        {
            if (!rule.Enabled || String.IsNullOrWhiteSpace(rule.Pattern)) return false;
            string needle = rule.Pattern.Trim();
            if (rule.Kind == TriggerKind.ProcessName || rule.Kind == TriggerKind.ExecutablePath)
            {
                return snapshot.Processes.Any(p =>
                    (!rule.ForegroundOnly || snapshot.Windows.Any(w => w.ProcessId == p.Id && w.Foreground)) &&
                    (rule.Kind == TriggerKind.ProcessName
                        ? String.Equals(p.Name, needle.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? needle.Substring(0, needle.Length - 4) : needle, StringComparison.OrdinalIgnoreCase)
                        : String.Equals(p.Path, needle.Trim('"'), StringComparison.OrdinalIgnoreCase)));
            }
            string domain = rule.Kind == TriggerKind.BrowserDomain ? NormalizeDomain(needle) : null;
            if (rule.Kind == TriggerKind.BrowserDomain && domain == null) return false;
            if (rule.Kind != TriggerKind.WindowTitle && rule.Kind != TriggerKind.BrowserTitle && rule.Kind != TriggerKind.BrowserDomain) return false;
            return snapshot.Windows.Any(w =>
            {
                if (rule.ForegroundOnly && !w.Foreground) return false;
                if (rule.Kind != TriggerKind.WindowTitle &&
                    !snapshot.Processes.Any(p => p.Id == w.ProcessId && Browsers.Contains(p.Name))) return false;
                // 域名只是标题中可见的文本证据，不读取地址栏、历史记录或后台标签页。
                if (domain != null)
                    return Regex.IsMatch(w.Title ?? "", @"(?<![\p{L}\p{N}_-])" + Regex.Escape(domain) + @"(?![\p{L}\p{N}_.-])", RegexOptions.IgnoreCase);
                return (w.Title ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
            });
        }

        public static Rule Select(IEnumerable<Rule> rules, ActivitySnapshot snapshot, ISet<string> online)
        {
            // OrderBy 稳定排序：同优先级按界面顺序；离线设备跳过，允许低优先级规则接管。
            return rules.Where(r => online.Contains(r.DeviceId) && Matches(r, snapshot))
                .OrderByDescending(r => r.Priority).FirstOrDefault();
        }
    }

    // 纯状态机，不接触 Windows；单调时间由调用方传入，便于确定性测试。
    public sealed class SwitchPlanner
    {
        private string pending;
        private long pendingSince;
        private long lastSwitch = -100000;
        private long retryAfter;
        public void Reset() { pending = null; retryAfter = 0; }
        public void Manual(long now) { Reset(); lastSwitch = now; }
        public void Succeeded(long now) { lastSwitch = now; retryAfter = 0; }
        public void Failed(long now) { retryAfter = now + 5000; }

        public string Plan(string desired, bool alreadyDefault, bool isAutomaticRule, long now)
        {
            if (String.IsNullOrEmpty(desired)) { Reset(); return null; }
            if (pending != desired) { pending = desired; pendingSince = now; }
            if (alreadyDefault) return null;
            int settleMs = isAutomaticRule ? 1000 : 1500;
            if (now - pendingSince < settleMs || now - lastSwitch < 2500 || now < retryAfter) return null;
            return desired;
        }

        public static string Desired(Settings settings, Rule winner, ISet<string> online)
        {
            if (settings.Paused) return null;
            if (winner != null) return winner.DeviceId;
            if (!String.IsNullOrEmpty(settings.ManualDeviceId) && online.Contains(settings.ManualDeviceId)) return settings.ManualDeviceId;
            return online.Contains(settings.FallbackDeviceId ?? "") ? settings.FallbackDeviceId : null;
        }
    }
}
