using System;
using System.Drawing;
using System.Windows.Forms;
using ScreensaverApp.Configuration;

namespace ScreensaverApp.Management
{
    internal sealed partial class ManagerWindow
    {
        private static ComboBox Choice(string name, params string[] items)
        {
            var combo = new ComboBox { Name = name, Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat, BackColor = UiTheme.Surface, ForeColor = UiTheme.Text, Margin = new Padding(0, 2, 12, 5) };
            combo.Items.AddRange(items); combo.SelectedIndex = 0; return combo;
        }
        private readonly ComboBox _clockStyle = Choice("ClockStyle", "Off · video only", "Minimal digital", "Neon digital", "Analog", "Flip cards");
        private readonly ComboBox _clockPosition = Choice("ClockPosition", "Top left", "Top right", "Center", "Bottom left", "Bottom right");
        private readonly ComboBox _clockSize = Choice("ClockSize", "Small", "Medium", "Large");
        private readonly ComboBox _clockColor = Choice("ClockColor", "Ice cyan", "White", "Violet", "Amber");
        private readonly ComboBox _shortcut = Choice("LaunchShortcut", "Disabled");
        private readonly CheckBox _clock24 = new CheckBox { Name = "Clock24Hour", Text = "24-hour time", AutoSize = true };
        private readonly CheckBox _clockSeconds = new CheckBox { Name = "ClockSeconds", Text = "Show seconds", AutoSize = true };
        private readonly CheckBox _clockDate = new CheckBox { Name = "ClockDate", Text = "Show date", AutoSize = true };
        private sealed class ClockCanvas : Panel
        { internal ClockCanvas() { DoubleBuffered = true; ResizeRedraw = true; } }
        private readonly ClockCanvas _clockPreview = new ClockCanvas { Name = "ClockPreview", Dock = DockStyle.Fill, BackColor = Color.FromArgb(9, 14, 31) };
        private readonly System.Windows.Forms.Timer _clockPreviewTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        private Panel CustomizationPage()
        {
            var page = new Panel { Name = "CustomizationPage", Dock = DockStyle.Fill };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 196));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); page.Controls.Add(layout);
            var previewCard = new ThemeCard { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = new Padding(0, 0, 0, 12) };
            var hint = new ThemeLabel { Text = "LIVE CLOCK STYLE  ·  PC local time                              VIDEO PLACEMENT", ForeColor = UiTheme.Muted, Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 9) };
            previewCard.Controls.Add(_clockPreview); previewCard.Controls.Add(hint); layout.Controls.Add(previewCard, 0, 0);
            _clockPreview.Paint += (_, args) =>
            {
                if (_clockPreview.Width < 40 || _clockPreview.Height < 10) return;
                ClockOptions clock = DraftClock(); DateTime now = DateTime.Now;
                int leftWidth = _clockPreview.Width / 2;
                var styleArea = new Rectangle(5, 4, leftWidth - 10, _clockPreview.Height - 8);
                if (clock.Style != ClockStyle.Off)
                {
                    Size design = ClockRenderer.DesignSize(clock);
                    float zoom = Math.Min(styleArea.Width / (float)design.Width, styleArea.Height / (float)design.Height) * .96f;
                    var size = new Size(Math.Max(1, (int)(design.Width * zoom)), Math.Max(1, (int)(design.Height * zoom)));
                    using (var sample = ClockRenderer.Render(size, clock, now))
                        args.Graphics.DrawImageUnscaled(sample, styleArea.Left + (styleArea.Width - size.Width) / 2, styleArea.Top + (styleArea.Height - size.Height) / 2);
                }
                else TextRenderer.DrawText(args.Graphics, "Choose a clock style below", Font, styleArea, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                int availableWidth = _clockPreview.Width - leftWidth - 16;
                float fit = Math.Min(availableWidth / 16f, (_clockPreview.Height - 8) / 9f);
                var placement = new Rectangle(leftWidth + 8 + (availableWidth - (int)(16 * fit)) / 2,
                    (_clockPreview.Height - (int)(9 * fit)) / 2, Math.Max(1, (int)(16 * fit)), Math.Max(1, (int)(9 * fit)));
                if (_thumbnail.Image != null)
                    args.Graphics.DrawImage(_thumbnail.Image, placement);
                var state = args.Graphics.Save(); args.Graphics.TranslateTransform(placement.X, placement.Y);
                ClockRenderer.DrawOn(args.Graphics, placement.Size, clock, now); args.Graphics.Restore(state);
                using (var pen = new Pen(UiTheme.Border)) args.Graphics.DrawRectangle(pen, placement);
            };
            var clockCard = new ThemeCard { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 10, 6), Margin = new Padding(0, 0, 0, 10) };
            var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Margin = Padding.Empty };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            foreach (int height in new[] { 22, 35, 22, 35, 40 }) options.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            options.Controls.Add(Caption("CLOCK STYLE"), 0, 0); options.Controls.Add(Caption("POSITION"), 1, 0);
            options.Controls.Add(_clockStyle, 0, 1); options.Controls.Add(_clockPosition, 1, 1);
            options.Controls.Add(Caption("SIZE"), 0, 2); options.Controls.Add(Caption("ACCENT COLOR"), 1, 2);
            options.Controls.Add(_clockSize, 0, 3); options.Controls.Add(_clockColor, 1, 3);
            var checks = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 6, 0, 0) };
            checks.Controls.Add(_clock24); checks.Controls.Add(_clockSeconds); checks.Controls.Add(_clockDate);
            options.Controls.Add(checks, 0, 4); options.SetColumnSpan(checks, 2); clockCard.Controls.Add(options); layout.Controls.Add(clockCard, 0, 1);
            var hotkeyCard = new ThemeCard { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 8), Margin = new Padding(0, 0, 0, 10) };
            var hotkeyLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            hotkeyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); hotkeyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            hotkeyLayout.Controls.Add(Caption("START SCREENSAVER SHORTCUT"), 0, 0); hotkeyLayout.Controls.Add(_shortcut, 0, 1);
            var help = new ThemeLabel { Text = "Works with the manager closed. Choose an unused combination; Windows handles the shortcut.", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 9), Padding = new Padding(5, 2, 0, 0) };
            hotkeyLayout.Controls.Add(help, 1, 0); hotkeyLayout.SetRowSpan(help, 2); hotkeyCard.Controls.Add(hotkeyLayout); layout.Controls.Add(hotkeyCard, 0, 2);
            for (char key = 'A'; key <= 'Z'; key++) _shortcut.Items.Add("Ctrl + Alt + " + key);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            var apply = MakeButton("Apply changes", true); apply.Name = "ApplyCustomization"; apply.Click += async (_, __) => await ApplyAsync(); actions.Controls.Add(apply);
            var preview = MakeButton("Preview selection"); preview.Name = "PreviewCustomization"; preview.Click += (_, __) => PreviewSelection(); actions.Controls.Add(preview);
            var discard = MakeButton("Discard changes"); discard.Click += (_, __) => LoadSaved(); actions.Controls.Add(discard); layout.Controls.Add(actions, 0, 3);
            foreach (var combo in new[] { _clockStyle, _clockPosition, _clockSize, _clockColor, _shortcut }) combo.SelectedIndexChanged += (_, __) => CustomizationChanged();
            foreach (var check in new[] { _clock24, _clockSeconds, _clockDate }) check.CheckedChanged += (_, __) => CustomizationChanged();
            _clockPreviewTimer.Tick += (_, __) => { if (_clockPreview.Visible) _clockPreview.Invalidate(); if (_thumbnail.Visible && _clockStyle.SelectedIndex > 0) _thumbnail.Invalidate(); };
            page.VisibleChanged += (_, __) => { if (page.Visible) _clockPreview.Invalidate(); };
            Shown += (_, __) => _clockPreviewTimer.Start();
            return page;
        }
        private void CustomizationChanged()
        {
            bool enabled = _clockStyle.SelectedIndex > 0;
            _clockPosition.Enabled = _clockSize.Enabled = _clockColor.Enabled = _clock24.Enabled = _clockSeconds.Enabled = _clockDate.Enabled = enabled;
            _clockPreview.Invalidate(); _thumbnail.Invalidate(); Dirty();
        }
        private ClockOptions DraftClock() => new ClockOptions { Style = (ClockStyle)Math.Max(0, _clockStyle.SelectedIndex),
            Position = (ClockPosition)Math.Max(0, _clockPosition.SelectedIndex), Size = Math.Max(0, _clockSize.SelectedIndex),
            Color = Math.Max(0, _clockColor.SelectedIndex), Use24Hour = _clock24.Checked, Seconds = _clockSeconds.Checked, Date = _clockDate.Checked };
        private string DraftShortcut() => _shortcut.SelectedIndex <= 0 ? "" : ((char)('A' + _shortcut.SelectedIndex - 1)).ToString();
        private void LoadCustomization(SaverPreferences preferences)
        {
            var clock = preferences.Clock;
            _clockStyle.SelectedIndex = (int)clock.Style; _clockPosition.SelectedIndex = (int)clock.Position;
            _clockSize.SelectedIndex = clock.Size; _clockColor.SelectedIndex = clock.Color;
            _clock24.Checked = clock.Use24Hour; _clockSeconds.Checked = clock.Seconds; _clockDate.Checked = clock.Date;
            _shortcut.SelectedIndex = preferences.ShortcutKey.Length == 0 ? 0 : preferences.ShortcutKey[0] - 'A' + 1;
            CustomizationChanged();
        }
    }
}
