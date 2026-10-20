using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ScreensaverApp.Configuration;

namespace ScreensaverApp
{
    // A native child stays above LibVLC's HWND (WPF text cannot cross its airspace).
    // It is clipped to its saver/preview parent and never takes focus or input.
    internal sealed class ClockOverlay : NativeWindow, IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDC, IntPtr destination,
            ref NativeSize size, IntPtr sourceDC, ref Point source, uint color, ref Blend blend, uint flags);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr bitmap);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr bitmap);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        private readonly IntPtr _parent;
        private readonly ClockOptions _options;
        private string _last;
        private Rectangle _bounds;
        internal ClockOverlay(IntPtr parent, ClockOptions options)
        {
            _parent = parent; _options = options.Copy();
            CreateHandle(new CreateParams { Caption = "CineSaver Clock", Parent = parent,
                Style = 0x40000000 | 0x10000000, ExStyle = 0x00080000 | 0x00000020 | 0x08000000 });
            try { Update(); } catch { DestroyHandle(); throw; }
        }
        internal void Update()
        {
            if (Handle == IntPtr.Zero || !GetClientRect(_parent, out Rect client)) return;
            Rectangle bounds = ClockRenderer.Bounds(new Size(client.Right, client.Bottom), _options);
            if (bounds.IsEmpty) return;
            DateTime now = DateTime.Now; // Read wall time every tick, including clock/time-zone changes.
            string current = now.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
            SetWindowPos(Handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, bounds == _bounds ? 0x13u : 0x10u);
            if (current == _last && bounds == _bounds) return;
            using (var bitmap = ClockRenderer.Render(bounds.Size, _options, now))
            {
                IntPtr dc = CreateCompatibleDC(IntPtr.Zero), native = bitmap.GetHbitmap(Color.FromArgb(0));
                IntPtr previous = SelectObject(dc, native);
                try
                {
                    var size = new NativeSize { Width = bounds.Width, Height = bounds.Height }; var source = new Point();
                    var blend = new Blend { Alpha = 255, Format = 1 };
                    if (!UpdateLayeredWindow(Handle, IntPtr.Zero, IntPtr.Zero, ref size, dc, ref source, 0, ref blend, 2))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Clock overlay could not be drawn.");
                }
                finally { SelectObject(dc, previous); DeleteObject(native); DeleteDC(dc); }
            }
            _bounds = bounds; _last = current;
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; } // HTTRANSPARENT
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            base.WndProc(ref message);
        }
        public void Dispose() { if (Handle != IntPtr.Zero) DestroyHandle(); }
    }
}
