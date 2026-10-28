using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace AudioRoute
{
    internal static class Theme
    {
        public static readonly Color Ink = Color.FromArgb(24, 43, 68);
        public static readonly Color Blue = Color.FromArgb(25, 90, 166);
        public static readonly Color Canvas = Color.FromArgb(242, 246, 250);
        public static readonly Color Muted = Color.FromArgb(80, 98, 119);
        public static readonly Icon AppIcon = LoadIcon();
        private static Icon LoadIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AudioRoute.App.ico"))
            {
                if (stream == null) return SystemIcons.Application;
                using (var icon = new Icon(stream, 32, 32)) return (Icon)icon.Clone();
            }
        }
        public static void Form(Form form)
        {
            form.SuspendLayout();
            form.Font = new Font("Microsoft YaHei UI", 9F);
            form.BackColor = Canvas; form.ForeColor = Ink;
            form.AutoScaleDimensions = new SizeF(96F, 96F);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.StartPosition = FormStartPosition.CenterScreen;
            form.Icon = AppIcon;
        }
        public static Label Label(string text, float size, bool bold)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Ink,
                Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                Margin = new Padding(0, 6, 0, 8) };
        }
        public static Button Button(string text, System.Action action, bool primary)
        {
            var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(82, 34),
                Padding = new Padding(8, 3, 8, 3), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Ink,
                Margin = new Padding(0, 0, 8, 6), UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(200, 211, 223);
            button.Click += delegate { action(); }; return button;
        }
    }
}
