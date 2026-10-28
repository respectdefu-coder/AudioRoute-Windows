using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AudioRoute
{
    internal sealed class MainForm : Form
    {
        private readonly Settings settings;
        private readonly ConfigStore store;
        private readonly IAudioBackend audio;
        private readonly bool preview;
        private readonly bool startInTray;
        private readonly SwitchPlanner planner = new SwitchPlanner();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Timer timer = new Timer { Interval = 800 };
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ListBox devices = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, HorizontalScrollbar = true };
        private readonly DataGridView grid = new DataGridView();
        private readonly ComboBox fallback = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        private readonly Label current = new Label();
        private readonly Label state = new Label();
        private readonly Label idle = new Label();
        private readonly ListBox events = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, HorizontalScrollbar = true };
        private readonly CheckBox paused = new CheckBox { Text = "暂停自动切换", AutoSize = true };
        private readonly CheckBox communications = new CheckBox { Text = "同时切换通信设备", AutoSize = true };
        private readonly CheckBox startup = new CheckBox { Text = "登录 Windows 后启动", AutoSize = true };
        private readonly TrackBar volume = new TrackBar { Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, Dock = DockStyle.Fill, SmallChange = 1, LargeChange = 5, AccessibleName = "当前输出设备音量" };
        private readonly Label volumeText = Theme.Label("当前输出音量", 9, true);
        private readonly CheckBox muted = new CheckBox { Text = "静音", AutoSize = true };
        private bool updatingVolume;
        private bool loading = true, busy, exiting;
        private string lastError;
        private string winnerId;
        private List<AudioDevice> online = new List<AudioDevice>();
        private string[] defaults = new string[3];
        private ActivitySnapshot snapshot = new ActivitySnapshot();

        public MainForm(Settings config, ConfigStore configStore, IAudioBackend backend, bool trayMode, bool previewMode)
        {
            settings = config; store = configStore; audio = backend; preview = previewMode; startInTray = trayMode;
            Theme.Form(this); Text = "AudioRoute · 音频输出自动切换";
            ClientSize = new Size(1120, 700); MinimumSize = new Size(1060, 650);
            BuildLayout(); BuildTray();
            volume.Scroll += delegate { AdjustVolume(); };
            muted.CheckedChanged += delegate { if (!updatingVolume) AdjustMute(); };
            paused.Checked = settings.Paused; communications.Checked = settings.IncludeCommunications;
            try { startup.Checked = StartupRegistration.Enabled; } catch (Exception e) { AddEvent("无法读取开机启动状态：" + e.Message); }
            paused.CheckedChanged += delegate { if (loading) return; settings.Paused = paused.Checked; planner.Reset(); Persist(); UpdateStatus(); };
            communications.CheckedChanged += delegate { if (loading) return; settings.IncludeCommunications = communications.Checked; planner.Reset(); Persist(); };
            startup.CheckedChanged += delegate
            {
                if (loading || preview) return;
                try { StartupRegistration.Set(startup.Checked, Application.ExecutablePath); AddEvent(startup.Checked ? "已启用登录后启动" : "已关闭登录后启动"); }
                catch (Exception e) { MessageBox.Show(this, e.Message, "开机启动设置失败"); loading = true; startup.Checked = !startup.Checked; loading = false; }
            };
            RefreshDevices(); RefreshRules(); loading = false;
            if (store.Warning != null) AddEvent(store.Warning);
            if (!preview && store.Warning != null) Shown += delegate { MessageBox.Show(this, store.Warning, "配置恢复提示"); };
            timer.Tick += async delegate { await Evaluate(); };
            Shown += async delegate
            {
                Rectangle area = Screen.FromControl(this).WorkingArea;
                Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
                if (preview) return;
                if (startInTray) Hide();
                timer.Start(); await Evaluate();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!exiting && !preview && e.CloseReason == CloseReason.UserClosing)
                { e.Cancel = true; Hide(); tray.ShowBalloonTip(1600, "AudioRoute 正在后台运行", "右键托盘图标可切换设备或退出。", ToolTipIcon.Info); }
            };
            FormClosed += delegate { exiting = true; timer.Stop(); timer.Dispose(); tray.Visible = false; tray.Dispose(); };
            ResumeLayout(true);
        }
        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 20, 26, 18), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            Controls.Add(root);
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(Theme.Label("AudioRoute  /  输出路由", 21, true), 0, 0);
            header.Controls.Add(Theme.Button("最小化到托盘", delegate { Hide(); }, false), 1, 0); root.Controls.Add(header, 0, 0);
            var route = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Blue, Padding = new Padding(16, 8, 16, 8), RowCount = 2, Margin = new Padding(0, 6, 0, 14) };
            current.Dock = DockStyle.Fill; current.Font = new Font(Font.FontFamily, 14, FontStyle.Bold); current.ForeColor = Color.White; current.AutoEllipsis = true;
            state.Dock = DockStyle.Fill; state.ForeColor = Color.FromArgb(223, 236, 253); state.AutoEllipsis = true;
            route.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); route.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            route.Controls.Add(current, 0, 0); route.Controls.Add(state, 0, 1); root.Controls.Add(route, 0, 1);
            var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 285)); workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, Margin = new Padding(0, 0, 18, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            left.Controls.Add(Theme.Label("可用输出设备", 12, true), 0, 0); devices.Font = new Font(Font.FontFamily, 10); devices.ItemHeight = 30;
            devices.DoubleClick += delegate { ManualSwitch(); }; left.Controls.Add(devices, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(Theme.Button("切换到此设备", ManualSwitch, true)); buttons.Controls.Add(Theme.Button("刷新", RefreshDevices, false));
            left.Controls.Add(buttons, 0, 2);
            var help = Theme.Label("手动选择将保留为空闲输出。\n有规则命中时，自动规则仍优先。", 9, false); help.ForeColor = Theme.Muted; left.Controls.Add(help, 0, 3);
            var volumePanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Margin = new Padding(0) };
            volumePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); volumePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); volumePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            volumePanel.Controls.Add(volumeText, 0, 0); volumePanel.Controls.Add(volume, 0, 1); volumePanel.Controls.Add(muted, 0, 2); left.Controls.Add(volumePanel, 0, 4);
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Margin = new Padding(0) };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            right.Controls.Add(Theme.Label("自动切换规则", 12, true), 0, 0);
            grid.Dock = DockStyle.Fill; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false; grid.RowHeadersVisible = false; grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(224, 233, 244); grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Ink;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(218, 234, 252); grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
            grid.RowTemplate.Height = 34; grid.ColumnHeadersHeight = 34;
            string[] columns = { "状态", "规则 / 匹配内容", "目标设备", "优先级" };
            for (int i = 0; i < columns.Length; i++) { grid.Columns.Add("c" + i, columns[i]); grid.Columns[i].SortMode = DataGridViewColumnSortMode.NotSortable; }
            grid.Columns[0].FillWeight = 55; grid.Columns[1].FillWeight = 170; grid.Columns[2].FillWeight = 120; grid.Columns[3].FillWeight = 50;
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) EditRule(false); };
            right.Controls.Add(grid, 0, 1);
            var ruleActions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            ruleActions.Controls.Add(Theme.Button("添加规则", delegate { EditRule(true); }, true));
            ruleActions.Controls.Add(Theme.Button("编辑", delegate { EditRule(false); }, false));
            ruleActions.Controls.Add(Theme.Button("启用 / 停用", ToggleRule, false));
            ruleActions.Controls.Add(Theme.Button("删除", DeleteRule, false));
            ruleActions.Controls.Add(Theme.Button("查看窗口标题", InspectWindows, false)); right.Controls.Add(ruleActions, 0, 2);
            workspace.Controls.Add(left, 0, 0); workspace.Controls.Add(right, 1, 0); root.Controls.Add(workspace, 0, 2);
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
            var fallRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            fallRow.Controls.Add(Theme.Label("无规则命中时回退到", 9, true)); fallback.Margin = new Padding(10, 4, 10, 0); fallRow.Controls.Add(fallback);
            fallRow.Controls.Add(Theme.Button("保存回退设备", SaveFallback, false));
            fallRow.Controls.Add(Theme.Button("清除手动选择", delegate { settings.ManualDeviceId = ""; planner.Reset(); Persist(); UpdateStatus(); }, false));
            options.Controls.Add(fallRow);
            idle.AutoSize = true; idle.ForeColor = Theme.Muted; idle.Margin = new Padding(3, 0, 0, 8); options.Controls.Add(idle);
            var switches = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            paused.Margin = new Padding(3, 0, 22, 0); communications.Margin = new Padding(3, 0, 22, 0); startup.Margin = new Padding(3, 0, 22, 0);
            switches.Controls.Add(paused); switches.Controls.Add(communications); switches.Controls.Add(startup); options.Controls.Add(switches); root.Controls.Add(options, 0, 3);
            var log = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = new Padding(0) };
            log.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); log.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            log.Controls.Add(Theme.Label("运行记录", 9, true), 0, 0); events.ForeColor = Theme.Muted; events.BackColor = Theme.Canvas; log.Controls.Add(events, 0, 1); root.Controls.Add(log, 0, 4);
        }
        private void BuildTray()
        {
            tray.Icon = Theme.AppIcon; tray.Text = "AudioRoute"; tray.Visible = !preview;
            tray.DoubleClick += delegate { Restore(); };
            var menu = new ContextMenuStrip();
            menu.Opening += delegate
            {
                foreach (ToolStripItem item in menu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose(); menu.Items.Clear();
                menu.Items.Add("打开 AudioRoute", null, delegate { Restore(); });
                menu.Items.Add(settings.Paused ? "恢复自动切换" : "暂停自动切换", null, delegate { paused.Checked = !paused.Checked; });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem(current.Text) { Enabled = false });
                foreach (AudioDevice d in online)
                {
                    AudioDevice target = d;
                    var item = new ToolStripMenuItem(d.Name) { Checked = defaults[1] == d.Id };
                    item.Click += delegate { ManualSwitch(target); }; menu.Items.Add(item);
                }
                menu.Items.Add(new ToolStripSeparator());
                var muteItem = new ToolStripMenuItem("当前输出静音") { Checked = muted.Checked, Enabled = muted.Enabled };
                muteItem.Click += delegate { muted.Checked = !muted.Checked; }; menu.Items.Add(muteItem);
                menu.Items.Add("音量 +5%", null, delegate { ChangeVolumeBy(5); });
                menu.Items.Add("音量 -5%", null, delegate { ChangeVolumeBy(-5); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("退出", null, delegate { exiting = true; Close(); });
            };
            tray.ContextMenuStrip = menu;
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); InstanceActivation.Register(Handle);
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            InstanceActivation.Unregister(Handle); base.OnHandleDestroyed(e);
        }
        protected override void WndProc(ref Message message)
        {
            if ((uint)message.Msg == InstanceActivation.ShowMessage)
            { Restore(); return; }
            base.WndProc(ref message);
        }
        private void Restore() { Show(); WindowState = FormWindowState.Normal; BringToFront(); Activate(); }
        private void RefreshDevices()
        {
            try { online = audio.Enumerate(); defaults = audio.Defaults(); BindDevices(); UpdateVolume(); UpdateStatus(); }
            catch (Exception e) { ReportError(e); }
            finally { UpdateStatus(); }
        }
        private void BindDevices()
        {
            string selected = (devices.SelectedItem as AudioDevice ?? new AudioDevice()).Id;
            devices.Items.Clear(); devices.Items.AddRange(online.Cast<object>().ToArray());
            devices.SelectedItem = online.FirstOrDefault(d => d.Id == selected) ?? online.FirstOrDefault(d => d.Id == defaults[1]) ?? online.FirstOrDefault();
            var choices = new List<AudioDevice> { new AudioDevice { Id = "", Name = "保持当前设备（不设置回退）" } }; choices.AddRange(online);
            if (!String.IsNullOrEmpty(settings.FallbackDeviceId) && !online.Any(d => d.Id == settings.FallbackDeviceId))
                choices.Add(new AudioDevice { Id = settings.FallbackDeviceId, Name = "已保存的回退设备（离线）" });
            fallback.Items.Clear(); fallback.Items.AddRange(choices.Cast<object>().ToArray());
            fallback.SelectedItem = choices.FirstOrDefault(d => d.Id == settings.FallbackDeviceId) ?? choices[0];
        }
        private string DeviceName(string id)
        { var device = online.FirstOrDefault(d => d.Id == id); return device != null ? device.Name : String.IsNullOrEmpty(id) ? "未设置" : "已保存设备（离线）"; }
        private void UpdateStatus()
        {
            current.Text = "当前输出   " + DeviceName(defaults[1]);
            Rule winner = settings.Rules.FirstOrDefault(r => r.Id == winnerId);
            state.Text = settings.Paused ? "自动切换已暂停 · 可随时手动选择输出" :
                winner != null ? "规则命中：" + winner.Name + "  →  " + DeviceName(winner.DeviceId) :
                settings.Rules.Count == 0 ? "添加第一条规则，让应用启动时自动切换输出。" : "正在监听 · 暂无可用规则命中";
            idle.Text = String.IsNullOrEmpty(settings.ManualDeviceId) ? "空闲输出：预设回退设备（未设置时保持当前）" :
                "空闲输出：手动选择的 " + DeviceName(settings.ManualDeviceId) + "；离线时尝试预设回退设备";
            string text = "AudioRoute · " + (settings.Paused ? "已暂停" : winner == null ? "待机" : winner.Name);
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }
        private void UpdateVolume()
        {
            updatingVolume = true;
            try
            {
                bool available = !String.IsNullOrEmpty(defaults[1]); volume.Enabled = available; muted.Enabled = available;
                if (!available) { volumeText.Text = "当前输出音量：无可用设备"; return; }
                VolumeState value = audio.GetVolume(defaults[1]);
                volume.Value = Math.Max(0, Math.Min(100, (int)Math.Round(value.Level * 100)));
                muted.Checked = value.Muted; volumeText.Text = "当前输出音量   " + volume.Value + "%";
            }
            catch (Exception e) { volume.Enabled = false; muted.Enabled = false; volumeText.Text = "此设备音量暂不可用"; ReportError(e); }
            finally { updatingVolume = false; }
        }
        private void AdjustVolume()
        {
            if (updatingVolume || String.IsNullOrEmpty(defaults[1]) || preview) return;
            try { audio.SetVolume(defaults[1], volume.Value / 100F); volumeText.Text = "当前输出音量   " + volume.Value + "%"; }
            catch (Exception e) { ReportError(e); UpdateVolume(); }
        }
        private void AdjustMute()
        {
            if (String.IsNullOrEmpty(defaults[1]) || preview) return;
            try { audio.SetMute(defaults[1], muted.Checked); }
            catch (Exception e) { ReportError(e); UpdateVolume(); }
        }
        private void ChangeVolumeBy(int delta)
        {
            UpdateVolume(); if (!volume.Enabled) return;
            volume.Value = Math.Max(0, Math.Min(100, volume.Value + delta)); AdjustVolume();
        }
        private Rule SelectedRule() { return grid.SelectedRows.Count == 0 ? null : grid.SelectedRows[0].Tag as Rule; }
        private void RefreshRules()
        {
            Rule selected = SelectedRule(); grid.Rows.Clear();
            foreach (Rule rule in settings.Rules)
            {
                int index = grid.Rows.Add("", rule.Name + "  /  " + rule.Pattern, rule.DeviceName, rule.Priority);
                grid.Rows[index].Tag = rule;
                grid.Rows[index].Cells[1].ToolTipText = rule.Kind + "\n" + rule.Pattern + (rule.ForegroundOnly ? "\n仅前台" : "");
                if (selected != null && selected.Id == rule.Id) grid.Rows[index].Selected = true;
            }
            UpdateRuleStates();
        }
        private void UpdateRuleStates()
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                Rule rule = (Rule)row.Tag;
                string status = !rule.Enabled ? "已停用" : !online.Any(d => d.Id == rule.DeviceId) ? "设备离线" :
                    rule.Id == winnerId && !settings.Paused ? (CoreAudio.IsDefault(defaults, rule.DeviceId, settings.IncludeCommunications) ? "输出中" : "待切换") : RuleMatcher.Matches(rule, snapshot) ? "已命中" : "等待";
                row.Cells[0].Value = status;
                row.DefaultCellStyle.ForeColor = rule.Enabled ? Theme.Ink : Theme.Muted;
            }
        }
        private void EditRule(bool add)
        {
            Rule selected = add ? null : SelectedRule(); if (!add && selected == null) return;
            using (var dialog = new RuleDialog(selected, online))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (add) settings.Rules.Add(dialog.Result); else settings.Rules[settings.Rules.IndexOf(selected)] = dialog.Result;
                planner.Reset(); Persist(); RefreshRules();
            }
        }
        private void ToggleRule() { Rule rule = SelectedRule(); if (rule == null) return; rule.Enabled = !rule.Enabled; planner.Reset(); Persist(); RefreshRules(); }
        private void DeleteRule()
        {
            Rule rule = SelectedRule(); if (rule == null) return;
            if (MessageBox.Show(this, "删除规则“" + rule.Name + "”？", "删除规则", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            settings.Rules.Remove(rule); planner.Reset(); Persist(); RefreshRules();
        }
        private void SaveFallback()
        {
            AudioDevice choice = fallback.SelectedItem as AudioDevice; if (choice == null) return;
            settings.FallbackDeviceId = choice.Id; planner.Reset(); Persist(); UpdateStatus();
            AddEvent("已保存回退设备。已有手动选择时，可点击“清除手动选择”恢复使用回退设备。");
        }
        private void ManualSwitch() { ManualSwitch(devices.SelectedItem as AudioDevice); }
        private void ManualSwitch(AudioDevice device)
        {
            if (device == null || preview) return;
            try
            {
                audio.Switch(device.Id, settings.IncludeCommunications);
                settings.ManualDeviceId = device.Id; planner.Manual(clock.ElapsedMilliseconds); Persist();
                defaults = audio.Defaults(); UpdateVolume(); UpdateStatus(); AddEvent("手动切换：" + device.Name);
            }
            catch (Exception e) { ReportError(e); MessageBox.Show(this, e.Message, "设备切换失败"); }
        }
        private async Task Evaluate()
        {
            if (busy || exiting || preview) return; busy = true;
            try
            {
                bool paths = settings.Rules.Any(r => r.Enabled && r.Kind == TriggerKind.ExecutablePath);
                // OS 采样放在线程池；规则、COM 切换与界面更新统一在 UI STA 线程，避免重入竞争。
                snapshot = await Task.Run(() => ActivityMonitor.Capture(paths));
                if (exiting || IsDisposed) return;
                List<AudioDevice> refreshed = audio.Enumerate(); defaults = audio.Defaults();
                if (!refreshed.Select(d => d.Id + d.Name).SequenceEqual(online.Select(d => d.Id + d.Name)))
                { online = refreshed; BindDevices(); }
                var ids = new HashSet<string>(online.Select(d => d.Id));
                Rule winner = RuleMatcher.Select(settings.Rules, snapshot, ids); winnerId = winner == null ? null : winner.Id;
                string desired = SwitchPlanner.Desired(settings, winner, ids);
                string change = planner.Plan(desired, CoreAudio.IsDefault(defaults, desired, settings.IncludeCommunications), winner != null, clock.ElapsedMilliseconds);
                if (change != null)
                {
                    try
                    {
                        audio.Switch(change, settings.IncludeCommunications); planner.Succeeded(clock.ElapsedMilliseconds);
                        defaults = audio.Defaults(); AddEvent((winner == null ? "空闲回退" : "规则“" + winner.Name + "”") + " → " + DeviceName(change));
                        lastError = null;
                    }
                    catch (Exception e) { planner.Failed(clock.ElapsedMilliseconds); ReportError(e); }
                }
                UpdateVolume(); UpdateStatus(); UpdateRuleStates();
            }
            catch (Exception e) { planner.Reset(); ReportError(e); }
            finally { busy = false; }
        }
        private void InspectWindows()
        {
            using (var dialog = new Form())
            {
                Theme.Form(dialog); dialog.Text = "当前可见窗口标题（不会保存到日志）"; dialog.Size = new Size(850, 500);
                var text = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false };
                text.Text = String.Join(Environment.NewLine + Environment.NewLine, snapshot.Windows.Select(w =>
                { var p = snapshot.Processes.FirstOrDefault(x => x.Id == w.ProcessId); return (p == null ? w.ProcessId.ToString() : p.Name) + (w.Foreground ? " [前台]" : "") + Environment.NewLine + w.Title; }));
                dialog.Controls.Add(text); dialog.ResumeLayout(true); dialog.ShowDialog(this);
            }
        }
        private void Persist()
        {
            if (preview) return;
            try { store.Save(settings); }
            catch (Exception e) { ReportError(e); MessageBox.Show(this, "当前设置仅在本次运行有效，无法写入配置：\n" + e.Message, "配置保存失败"); }
        }
        private void ReportError(Exception e)
        { if (lastError == e.Message) return; lastError = e.Message; AddEvent("错误：" + e.Message); }
        private void AddEvent(string message)
        {
            events.Items.Insert(0, DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            while (events.Items.Count > 100) events.Items.RemoveAt(events.Items.Count - 1);
            if (!preview) store.Log(message);
        }
    }
}
