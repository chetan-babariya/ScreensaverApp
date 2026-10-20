using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace ScreensaverApp.Configuration
{
    // Shared by the native video overlay and manager previews. No decoder, fonts,
    // network requests, image files, or animation framework is needed for a clock.
    internal static class ClockRenderer
    {
        internal static readonly Color[] Colors = { Color.FromArgb(89, 221, 255), Color.White, Color.FromArgb(189, 157, 255), Color.FromArgb(255, 199, 104) };
        internal static Size DesignSize(ClockOptions options) => options.Style == ClockStyle.Analog ? new Size(240, 280) : new Size(440, 152);
        internal static Rectangle Bounds(Size surface, ClockOptions options)
        {
            if (options.Style == ClockStyle.Off || surface.Width < 24 || surface.Height < 24) return Rectangle.Empty;
            Size design = DesignSize(options);
            float scale = Math.Min(surface.Width / 1280f, surface.Height / 720f) * new[] { .75f, 1f, 1.35f }[options.Size];
            int margin = Math.Max(5, (int)(Math.Min(surface.Width, surface.Height) * .025));
            int w = Math.Max(1, (int)(design.Width * scale)), h = Math.Max(1, (int)(design.Height * scale));
            w = Math.Min(w, surface.Width - margin * 2); h = Math.Min(h, surface.Height - margin * 2);
            int x = options.Position == ClockPosition.TopLeft || options.Position == ClockPosition.BottomLeft ? margin : surface.Width - margin - w;
            int y = options.Position == ClockPosition.TopLeft || options.Position == ClockPosition.TopRight ? margin : surface.Height - margin - h;
            if (options.Position == ClockPosition.Center) { x = (surface.Width - w) / 2; y = (surface.Height - h) / 2; }
            return new Rectangle(x, y, w, h);
        }
        internal static void DrawOn(Graphics graphics, Size surface, ClockOptions options, DateTime localTime)
        {
            Rectangle bounds = Bounds(surface, options);
            if (bounds.IsEmpty) return;
            var state = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y); graphics.ScaleTransform(bounds.Width / (float)DesignSize(options).Width, bounds.Height / (float)DesignSize(options).Height);
            Draw(graphics, options, localTime); graphics.Restore(state);
        }
        internal static Bitmap Render(Size pixels, ClockOptions options, DateTime localTime)
        {
            var bitmap = new Bitmap(pixels.Width, pixels.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                var design = DesignSize(options); graphics.ScaleTransform(pixels.Width / (float)design.Width, pixels.Height / (float)design.Height);
                Draw(graphics, options, localTime);
            }
            return bitmap;
        }
        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath(); float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        private static void Text(Graphics g, string text, float size, Color color, RectangleF bounds, FontStyle style = FontStyle.Regular)
        {
            using (var font = new Font("Segoe UI", size, style, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            {
                // Measure 12-hour strings including localized AM/PM before drawing.
                float width = g.MeasureString(text, font, int.MaxValue, format).Width;
                if (width > bounds.Width)
                {
                    using (var fit = new Font("Segoe UI", size * bounds.Width / width, style, GraphicsUnit.Pixel)) g.DrawString(text, fit, brush, bounds, format);
                }
                else g.DrawString(text, font, brush, bounds, format);
            }
        }
        private static void Draw(Graphics g, ClockOptions o, DateTime now)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var design = DesignSize(o); Color accent = Colors[o.Color];
            using (var card = Rounded(new RectangleF(1, 1, design.Width - 2, design.Height - 2), 20))
            using (var bg = new SolidBrush(Color.FromArgb(210, 9, 14, 31)))
            using (var border = new Pen(Color.FromArgb(100, accent), 1))
            { g.FillPath(bg, card); g.DrawPath(border, card); }
            string date = now.ToString("ddd, d MMM yyyy", CultureInfo.CurrentCulture);
            if (o.Style == ClockStyle.Analog)
            {
                const float cx = 120, cy = 113, radius = 86;
                using (var pen = new Pen(Color.FromArgb(100, accent), 2)) g.DrawEllipse(pen, cx - radius, cy - radius, radius * 2, radius * 2);
                for (int i = 0; i < 60; i++)
                {
                    double a = i * Math.PI / 30 - Math.PI / 2; float inside = i % 5 == 0 ? 72 : 79;
                    using (var pen = new Pen(i % 5 == 0 ? Color.White : Color.FromArgb(110, accent), i % 5 == 0 ? 3 : 1))
                        g.DrawLine(pen, cx + (float)Math.Cos(a) * inside, cy + (float)Math.Sin(a) * inside, cx + (float)Math.Cos(a) * 83, cy + (float)Math.Sin(a) * 83);
                }
                Hand(g, cx, cy, (now.Hour % 12 + now.Minute / 60d) * 30, 43, 6, Color.White);
                Hand(g, cx, cy, (now.Minute + now.Second / 60d) * 6, 64, 4, accent);
                if (o.Seconds) Hand(g, cx, cy, now.Second * 6, 69, 1.5f, Color.FromArgb(189, 157, 255));
                using (var brush = new SolidBrush(accent)) g.FillEllipse(brush, cx - 4, cy - 4, 8, 8);
                Text(g, o.TimeText(now), 22, Color.White, new RectangleF(12, 211, 216, 28));
                if (o.Date) Text(g, date, 16, Color.FromArgb(184, 198, 222), new RectangleF(12, 243, 216, 25));
                return;
            }
            float timeY = o.Date ? 20 : 38;
            if (o.Style == ClockStyle.Flip)
            {
                string[] units = now.ToString((o.Use24Hour ? "HH:mm" : "hh:mm") + (o.Seconds ? ":ss" : ""), CultureInfo.InvariantCulture).Split(':');
                float total = units.Length * 105 + (units.Length - 1) * 9, start = (440 - total) / 2;
                for (int i = 0; i < units.Length; i++)
                {
                    var r = new RectangleF(start + i * 114, timeY - 3, 105, 77);
                    using (var path = Rounded(r, 8)) using (var fill = new SolidBrush(Color.FromArgb(255, 30, 42, 68))) g.FillPath(fill, path);
                    Text(g, units[i], 55, accent, r, FontStyle.Bold);
                    using (var pen = new Pen(Color.FromArgb(180, 9, 14, 31), 2)) g.DrawLine(pen, r.Left, r.Top + 38, r.Right, r.Top + 38);
                }
                if (!o.Use24Hour) Text(g, now.ToString("tt"), 13, Color.White, new RectangleF(390, timeY + 47, 44, 23));
            }
            else
            {
                if (o.Style == ClockStyle.Neon)
                {
                    using (var pen = new Pen(accent, 3)) g.DrawLine(pen, 32, 16, 408, 16);
                    Text(g, o.TimeText(now), 63, Color.FromArgb(80, accent), new RectangleF(22, timeY + 2, 400, 78), FontStyle.Bold);
                }
                Text(g, o.TimeText(now), 63, o.Style == ClockStyle.Minimal ? Color.White : accent, new RectangleF(20, timeY, 400, 78),
                    o.Style == ClockStyle.Neon ? FontStyle.Bold : FontStyle.Regular);
            }
            if (o.Date) Text(g, date, 22, Color.FromArgb(184, 198, 222), new RectangleF(20, 105, 400, 30));
        }
        private static void Hand(Graphics g, float x, float y, double degrees, float length, float width, Color color)
        {
            double angle = (degrees - 90) * Math.PI / 180;
            using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, x, y, x + (float)Math.Cos(angle) * length, y + (float)Math.Sin(angle) * length);
        }
    }
}
