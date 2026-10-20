using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreensaverApp.Configuration
{
    internal static class UiTheme
    {
        internal static readonly Color Background = Color.FromArgb(9, 14, 31), Sidebar = Color.FromArgb(11, 18, 38),
            Surface = Color.FromArgb(18, 28, 51), Hover = Color.FromArgb(36, 49, 80), Border = Color.FromArgb(45, 60, 92),
            Text = Color.FromArgb(238, 243, 255), Muted = Color.FromArgb(160, 177, 204),
            Violet = Color.FromArgb(113, 76, 237), Cyan = Color.FromArgb(89, 221, 255);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        internal static void Apply(Form form)
        {
            form.BackColor = Background; form.ForeColor = Text;
            form.Font = new Font("Segoe UI", 10); form.AutoScaleMode = AutoScaleMode.Dpi;
            AppBranding.SetWindowIcon(form);
            form.HandleCreated += (_, __) => { int dark = 1; DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); };
        }
        internal static Button Button(string text, bool primary = false)
        {
            var button = new ThemeButton { Text = text, AutoSize = true, MinimumSize = new Size(116, 36), Height = 36,
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, BackColor = primary ? Violet : Surface, ForeColor = Text,
                Cursor = Cursors.Hand, Padding = new Padding(9, 2, 9, 2), Margin = new Padding(0, 0, 10, 6) };
            button.FlatAppearance.BorderColor = primary ? Violet : Border; button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(132, 97, 255) : Hover;
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(91, 58, 211) : Background;
            return button;
        }
        internal static PictureBox Logo(Size size)
        {
            var logo = new PictureBox { Image = AppBranding.CreateLogo(), SizeMode = PictureBoxSizeMode.Zoom, Size = size, BackColor = Color.Transparent };
            logo.Disposed += (_, __) => logo.Image?.Dispose(); return logo;
        }
    }
    internal sealed class ThemeLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Enabled) { base.OnPaint(e); return; }
            // Preserve the configured color instead of Windows' embossed disabled text.
            var bounds = new Rectangle(Padding.Left, Padding.Top, Math.Max(1, Width - Padding.Horizontal), Math.Max(1, Height - Padding.Vertical));
            var flags = TextFormatFlags.NoPadding | (AutoSize ? TextFormatFlags.SingleLine : TextFormatFlags.WordBreak);
            if (AutoEllipsis) flags |= TextFormatFlags.EndEllipsis;
            if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            if (TextAlign == ContentAlignment.MiddleLeft || TextAlign == ContentAlignment.MiddleCenter || TextAlign == ContentAlignment.MiddleRight) flags |= TextFormatFlags.VerticalCenter;
            else if (TextAlign == ContentAlignment.BottomLeft || TextAlign == ContentAlignment.BottomCenter || TextAlign == ContentAlignment.BottomRight) flags |= TextFormatFlags.Bottom;
            if (TextAlign == ContentAlignment.TopCenter || TextAlign == ContentAlignment.MiddleCenter || TextAlign == ContentAlignment.BottomCenter) flags |= TextFormatFlags.HorizontalCenter;
            else if (TextAlign == ContentAlignment.TopRight || TextAlign == ContentAlignment.MiddleRight || TextAlign == ContentAlignment.BottomRight) flags |= TextFormatFlags.Right;
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor, flags);
        }
    }
    internal sealed class ThemeButton : Button
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Enabled) { base.OnPaint(e); return; }
            e.Graphics.Clear(BackColor);
            if (FlatAppearance.BorderSize > 0)
                using (var pen = new Pen(FlatAppearance.BorderColor, FlatAppearance.BorderSize)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            var bounds = new Rectangle(Padding.Left, Padding.Top, Math.Max(1, Width - Padding.Horizontal), Math.Max(1, Height - Padding.Vertical));
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            flags |= TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor, BackColor, flags);
        }
    }
    internal sealed class ThemeCard : Panel
    {
        internal ThemeCard() { DoubleBuffered = true; BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 22 || Height < 22) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                const int diameter = 20; var r = new Rectangle(0, 0, Width - 1, Height - 1);
                path.AddArc(r.Left, r.Top, diameter, diameter, 180, 90); path.AddArc(r.Right - diameter, r.Top, diameter, diameter, 270, 90);
                path.AddArc(r.Right - diameter, r.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(r.Left, r.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
                using (var brush = new LinearGradientBrush(r, Color.FromArgb(22, 34, 61), Color.FromArgb(16, 25, 46), 45f)) e.Graphics.FillPath(brush, path);
                using (var pen = new Pen(UiTheme.Border)) e.Graphics.DrawPath(pen, path);
            }
            base.OnPaint(e);
        }
    }
}
