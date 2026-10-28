using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace AudioRoute
{
    public static class ActivityMonitor
    {
        private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

        public static ActivitySnapshot Capture(bool needPaths)
        {
            var result = new ActivitySnapshot();
            // 每 800ms 采样；不需要管理员权限。短于采样周期的进程可能不被观察到。
            // 逐进程处理退出/权限竞争，单个受保护进程不会使整个快照失效。
            foreach (Process p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        var entry = new ProcessEntry { Id = p.Id, Name = p.ProcessName, Path = "" };
                        if (needPaths)
                        {
                            IntPtr handle = OpenProcess(0x1000, false, p.Id); // QUERY_LIMITED_INFORMATION
                            if (handle != IntPtr.Zero)
                            {
                                try { var buffer = new StringBuilder(32768); int size = buffer.Capacity;
                                    if (QueryFullProcessImageName(handle, 0, buffer, ref size)) entry.Path = buffer.ToString(); }
                                finally { CloseHandle(handle); }
                            }
                        }
                        result.Processes.Add(entry);
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
            IntPtr foreground = GetForegroundWindow();
            EnumWindows(delegate(IntPtr window, IntPtr data)
            {
                if (!IsWindowVisible(window)) return true;
                var text = new StringBuilder(4096);
                GetWindowText(window, text, text.Capacity);
                int pid; GetWindowThreadProcessId(window, out pid);
                // 无标题的前台窗口也保留，供进程规则的“仅前台”选项判断。
                if (text.Length > 0 || window == foreground)
                    result.Windows.Add(new WindowEntry { ProcessId = pid, Title = text.ToString(), Foreground = window == foreground });
                return true;
            }, IntPtr.Zero);
            return result;
        }
    }
}
