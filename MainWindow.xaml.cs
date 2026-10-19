using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using Screen = System.Windows.Forms.Screen;
using WinFormsCursor = System.Windows.Forms.Cursor;

namespace ScreensaverApp
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        // x64 only (this project is pinned to x64) -- SetClassLongPtr doesn't exist on 32-bit Windows.
        [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
        private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(int crColor);

        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int GCLP_HBRBACKGROUND = -10;

        // How far the mouse must move (px, window coords) before we treat it as "the user is here",
        // not "the mouse settled a pixel off from where it was". Without this, a screensaver on a
        // laptop with a twitchy trackpad exits almost immediately after starting.
        private const double MouseExitThresholdPx = 8.0;

        private Point _armedMousePosition;
        private bool _mouseTrackingArmed;

        private LibVLC _libVlc;
        private MediaPlayer _mediaPlayer;

        // Held in fields so the GDI handle isn't recreated per call and the delegate isn't
        // collected while native code still holds a pointer to it.
        private IntPtr _blackBrush = IntPtr.Zero;
        private EnumChildProc _enumChildProc;

        // Parameterless constructor kept so the XAML designer/compiler is happy; not used directly.
        public MainWindow()
        {
            InitializeComponent();
            Closed += (s, e) => TearDownPlayer();
        }

        /// <summary>Fullscreen mode: one instance per monitor.</summary>
        public MainWindow(Screen screen) : this()
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = screen.Bounds.Left;
            Top = screen.Bounds.Top;
            Width = screen.Bounds.Width;
            Height = screen.Bounds.Height;

            AttachExitHandlers();
            Loaded += (s, e) => StartVideo();
        }

        /// <summary>Preview mode: embedded as a child window inside the Settings preview box.</summary>
        public MainWindow(bool isPreview, IntPtr previewParent) : this()
        {
            Cursor = Cursors.Arrow;
            SourceInitialized += (s, e) => EmbedInPreview(previewParent);
            Loaded += (s, e) => StartVideo();
        }

        private void EmbedInPreview(IntPtr previewParent)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;

            SetParent(hwnd, previewParent);
            SetWindowLong(hwnd, GWL_STYLE, WS_CHILD | WS_VISIBLE);

            if (GetClientRect(previewParent, out RECT rect))
            {
                Width = rect.Right - rect.Left;
                Height = rect.Bottom - rect.Top;
            }
            Left = 0;
            Top = 0;
        }

        private void AttachExitHandlers()
        {
            KeyDown += (s, e) => App.RequestExit();
            MouseDown += (s, e) => App.RequestExit();
            MouseWheel += (s, e) => App.RequestExit();

            // Arm mouse-move tracking after a short delay so the window materializing under
            // the cursor doesn't itself count as "movement".
            Loaded += (s, e) =>
            {
                _armedMousePosition = CurrentCursorPosition();
                var armTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                armTimer.Tick += (ts, te) =>
                {
                    _mouseTrackingArmed = true;
                    armTimer.Stop();
                };
                armTimer.Start();
            };

            MouseMove += (s, e) =>
            {
                if (!_mouseTrackingArmed) return;

                var current = e.GetPosition(this);
                double dx = current.X - _armedMousePosition.X;
                double dy = current.Y - _armedMousePosition.Y;

                if (Math.Sqrt(dx * dx + dy * dy) > MouseExitThresholdPx)
                    App.RequestExit();
            };
        }

        private Point CurrentCursorPosition()
        {
            var p = WinFormsCursor.Position;
            return new Point(p.X, p.Y);
        }

        private void StartVideo()
        {
            // Paint the native host surface black before a single frame exists. The WPF
            // Background="Black" in the XAML only colors the managed layer; the video lives
            // in a win32 child window whose class background is the default system color.
            PaintNativeHostBlack();

            string path = ResolveVideoPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            // LibVLC's decode + color-conversion pipeline outputs proper full-range RGB,
            // avoiding the washed-out colors that WPF's built-in MediaElement produces on
            // many H.264 files (a long-standing Media Foundation limited-range bug).
            _libVlc = new LibVLC(enableDebugLogs: false);
            _mediaPlayer = new MediaPlayer(_libVlc);
            Player.MediaPlayer = _mediaPlayer;

            using var media = new Media(_libVlc, new Uri(path));
            // Loop the single file indefinitely at the libvlc level.
            media.AddOption(":input-repeat=65535");

            // Crop-to-fill, applied as a media option so it's in force from the very first
            // decoded frame. libvlc's crop takes a plain W:H ratio (not the video's actual
            // pixel size), so the window's own aspect can be handed over immediately with
            // no need to wait for the decoder to report anything.
            string aspect = WindowAspectRatio();
            if (aspect != null)
                media.AddOption($":crop={aspect}");

            _mediaPlayer.Play(media);

            // Belt-and-suspenders loop: some libvlc builds ignore input-repeat on certain
            // containers, so also restart manually when playback ends.
            _mediaPlayer.EndReached += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _mediaPlayer?.Play();
                }));
            };

            // Re-assert the crop and the black background once playback is actually running --
            // harmless if the media option already took, and covers builds that drop
            // per-media crop options on some container formats.
            _mediaPlayer.Playing += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplyFillCrop();
                    PaintNativeHostBlack();
                }));
            };
        }

        /// <summary>
        /// The window's aspect as a "W:H" ratio string, or null if the window hasn't been
        /// laid out yet.
        /// </summary>
        private string WindowAspectRatio()
        {
            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;

            if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0)
                return null;

            return $"{(int)Math.Round(w)}:{(int)Math.Round(h)}";
        }

        private void ApplyFillCrop()
        {
            if (_mediaPlayer == null) return;

            string aspect = WindowAspectRatio();
            if (aspect == null) return;

            _mediaPlayer.CropGeometry = aspect;
        }

        /// <summary>
        /// Forces the background of every native child window under this one to solid black,
        /// so anything the video doesn't cover reads as black instead of the win32 default
        /// system grey.
        /// </summary>
        private void PaintNativeHostBlack()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            if (_blackBrush == IntPtr.Zero)
                _blackBrush = CreateSolidBrush(0x00000000);

            if (_blackBrush == IntPtr.Zero) return;

            _enumChildProc = (child, lParam) =>
            {
                SetClassLongPtr(child, GCLP_HBRBACKGROUND, _blackBrush);
                InvalidateRect(child, IntPtr.Zero, true);
                return true;
            };

            try
            {
                EnumChildWindows(hwnd, _enumChildProc, IntPtr.Zero);
                InvalidateRect(hwnd, IntPtr.Zero, true);
            }
            catch
            {
                // Cosmetic only -- never let this stop playback.
            }
        }

        /// <summary>
        /// Looks for "screensaver.mp4" next to the .scr itself.
        /// </summary>
        private string ResolveVideoPath()
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            return Path.Combine(exeDir, "screensaver.mp4");
        }

        private void TearDownPlayer()
        {
            // Dispose in this order: stop playback, then MediaPlayer, then the LibVLC instance.
            // Skipping this on exit can leave a lingering native handle per monitor window.
            try
            {
                _mediaPlayer?.Stop();
                _mediaPlayer?.Dispose();
                _libVlc?.Dispose();
            }
            catch
            {
                // Never let teardown errors block the screensaver from closing.
            }
        }
    }
}
