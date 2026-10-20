using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ScreensaverApp
{
    // SS_BLACKRECT paints black from creation, including before the first frame.
    public sealed class NativeVideoHost : HwndHost
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string title,
            int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc callback, IntPtr parameter);
        [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
        private static extern IntPtr SetClassLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int index);
        private delegate bool EnumChildProc(IntPtr hwnd, IntPtr parameter);
        internal IntPtr VideoHandle { get; private set; }
        protected override HandleRef BuildWindowCore(HandleRef parent)
        {
            VideoHandle = CreateWindowEx(0, "static", "", 0x40000000 | 0x10000000 | 0x02000000 | 4,
                0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (VideoHandle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new HandleRef(this, VideoHandle);
        }
        internal void BlackenVideoChildren()
        {
            if (VideoHandle == IntPtr.Zero) return;
            // Stock brushes belong to Windows; no per-launch GDI allocation/leak.
            EnumChildProc callback = (child, _) => { SetClassLongPtr(child, -10, GetStockObject(4)); return true; };
            EnumChildWindows(VideoHandle, callback, IntPtr.Zero);
            GC.KeepAlive(callback);
        }
        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            DestroyWindow(hwnd.Handle); VideoHandle = IntPtr.Zero;
        }
    }
}
