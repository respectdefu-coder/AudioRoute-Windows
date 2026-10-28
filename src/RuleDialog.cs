using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AudioRoute
{
    internal sealed class RuleDialog : Form
    {
        public Rule Result;
        private readonly TextBox name = new TextBox { Dock = DockStyle.Fill };
        private readonly ComboBox kind = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox pattern = new TextBox { Dock = DockStyle.Fill };
        private readonly ComboBox device = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown priority = new NumericUpDown { Minimum = 0, Maximum = 9999, Value = 100, Width = 140 };
        private readonly CheckBox enabled = new CheckBox { Text = "启用此规则", AutoSize = true, Checked = true };
        private readonly CheckBox foreground = new CheckBox { Text = "仅当前台窗口属于触发对象时生效", AutoSize = true };
        private readonly Label hint = new Label { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(580, 0) };
        private Button browse;
        private readonly Rule source;
        private readonly Func<IWin32Window, string> executablePicker;
        public RuleDialog(Rule original, List<AudioDevice> devices)
            : this(original, devices, PickExecutable) { }
        internal RuleDialog(Rule original, List<AudioDevice> devices, Func<IWin32Window, string> picker)
        {
            executablePicker = picker;
            source = original ?? new Rule(); Theme.Form(this);
            Text = original == null ? "添加规则" : "编辑规则";
            ClientSize = new Size(680, 570); MinimumSize = new Size(640, 590);
            MaximizeBox = false; MinimizeBox = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 2, RowCount = 9 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 9; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);
            Add(root, 0, "规则名称", name); Add(root, 1, "触发类型", kind);
            var input = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            input.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); input.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            input.Controls.Add(pattern, 0, 0);
            browse = Theme.Button("浏览…", Browse, false); input.Controls.Add(browse, 1, 0);
            Add(root, 2, "匹配内容", input);
            root.Controls.Add(hint, 0, 3); root.SetColumnSpan(hint, 2); hint.Margin = new Padding(0, 6, 0, 22);
            Add(root, 4, "输出设备", device); Add(root, 5, "优先级", priority);
            var choices = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 14, 0, 12) };
            choices.Controls.Add(enabled); choices.Controls.Add(foreground); root.Controls.Add(choices, 1, 6);
            var note = Theme.Label("数字越大越优先；同优先级按列表先后顺序。", 9, false); root.Controls.Add(note, 0, 7); root.SetColumnSpan(note, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
            Button save = Theme.Button("保存规则", SaveRule, true);
            Button cancel = Theme.Button("取消", delegate { DialogResult = DialogResult.Cancel; }, false);
            actions.Controls.Add(save); actions.Controls.Add(cancel); root.Controls.Add(actions, 1, 8);
            AcceptButton = save; CancelButton = cancel;
            kind.Items.AddRange(new object[] { "进程名", "可执行文件完整路径", "任意窗口标题关键字", "浏览器标题关键字", "浏览器标题中的域名" });
            kind.SelectedIndexChanged += delegate { UpdateHint(); };
            name.Text = source.Name; pattern.Text = source.Pattern; priority.Value = source.Priority;
            enabled.Checked = source.Enabled; foreground.Checked = source.ForegroundOnly;
            var options = new List<AudioDevice>(devices);
            if (!String.IsNullOrEmpty(source.DeviceId) && !options.Any(d => d.Id == source.DeviceId))
                options.Add(new AudioDevice { Id = source.DeviceId, Name = (source.DeviceName ?? source.DeviceId) + "（离线）" });
            device.Items.AddRange(options.Cast<object>().ToArray());
            device.SelectedItem = options.FirstOrDefault(d => d.Id == source.DeviceId) ?? options.FirstOrDefault();
            kind.SelectedIndex = Enum.IsDefined(typeof(TriggerKind), source.Kind) ? (int)source.Kind : 0;
            ResumeLayout(true);
        }
        private static void Add(TableLayoutPanel panel, int row, string caption, Control control)
        {
            panel.Controls.Add(Theme.Label(caption, 9, true), 0, row);
            control.Margin = new Padding(0, 4, 0, 12); panel.Controls.Add(control, 1, row);
        }
        private void UpdateHint()
        {
            // 两种应用规则都可以选文件；标题/域名规则不显示不适用的文件按钮。
            bool fileRule = kind.SelectedIndex == 0 || kind.SelectedIndex == 1;
            browse.Visible = fileRule;
            browse.Enabled = fileRule;
            string[] hints = {
                "例如 vlc 或 vlc.exe。可点击“浏览”选择程序，自动填写进程名。后台进程仍会命中；只在使用窗口时切换可勾选“仅当前台”。",
                "按完整路径精确匹配，例如 C:\\Program Files\\VideoLAN\\VLC\\vlc.exe。受保护进程的路径可能无法读取。",
                "匹配任意可见顶层窗口的标题，不区分大小写。请使用有辨识度的关键字，避免误匹配本工具或其他应用。",
                "只匹配常见浏览器窗口标题，例如 YouTube、哔哩哔哩。默认允许非前台浏览器窗口；后台标签页不可见。",
                "仅在浏览器窗口标题实际包含域名时生效，例如 example.com。不会读取真实 URL，普通网站标题通常不含域名，请优先使用“浏览器标题关键字”。"
            };
            if (kind.SelectedIndex >= 0) hint.Text = hints[kind.SelectedIndex];
        }
        private void Browse()
        {
            if (kind.SelectedIndex != 0 && kind.SelectedIndex != 1) return;
            string fileName = executablePicker(this);
            if (String.IsNullOrEmpty(fileName)) return; // 取消选择时保留原有内容。
            pattern.Text = kind.SelectedIndex == 0 ? Path.GetFileName(fileName) : fileName;
        }
        private static string PickExecutable(IWin32Window owner)
        {
            using (var dialog = new OpenFileDialog {
                Title = "选择要监听的应用程序", Filter = "应用程序 (*.exe)|*.exe",
                CheckFileExists = true, Multiselect = false, RestoreDirectory = true })
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        }
        private void SaveRule()
        {
            string value = pattern.Text.Trim(); AudioDevice target = device.SelectedItem as AudioDevice;
            if (String.IsNullOrWhiteSpace(name.Text) || String.IsNullOrWhiteSpace(value) || target == null)
            { MessageBox.Show(this, "请填写名称、匹配内容，并选择输出设备。", "规则未保存"); return; }
            TriggerKind selected = (TriggerKind)kind.SelectedIndex;
            if (selected == TriggerKind.ProcessName && (value.IndexOfAny(new char[] { '\\', '/', '*', '?' }) >= 0))
            { MessageBox.Show(this, "进程名不支持路径或通配符，请输入例如 vlc.exe。", "检查匹配内容"); return; }
            if (selected == TriggerKind.ExecutablePath && (!Path.IsPathRooted(value.Trim('"')) || !value.Trim('"').EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            { MessageBox.Show(this, "请输入 .exe 文件的完整路径。", "检查路径"); return; }
            if (selected == TriggerKind.BrowserDomain)
            {
                value = RuleMatcher.NormalizeDomain(value);
                if (value == null) { MessageBox.Show(this, "请输入有效域名，例如 example.com。", "检查域名"); return; }
            }
            Result = source.Copy(); Result.Name = name.Text.Trim(); Result.Pattern = value.Trim('"'); Result.Kind = selected;
            Result.DeviceId = target.Id; Result.DeviceName = target.Name; Result.Priority = (int)priority.Value;
            Result.Enabled = enabled.Checked; Result.ForegroundOnly = foreground.Checked;
            DialogResult = DialogResult.OK;
        }
    }
}
