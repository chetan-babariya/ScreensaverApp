using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using ScreensaverApp.Configuration;
using Screen = System.Windows.Forms.Screen;

namespace ScreensaverApp
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        private PlaybackSession _session;
        private ClockOverlay _clockOverlay;
        private DispatcherTimer _healthTimer;
        private readonly Screen _screen;
        private readonly IntPtr _previewParent;
        private readonly bool _preview;
        private readonly Stopwatch _startup = Stopwatch.StartNew();
        private int _previewWidth, _previewHeight;
        public MainWindow()
        {
            InitializeComponent();
            using (var icon = AppBranding.CreateIcon())
                Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        }
        internal MainWindow(Screen screen, IntPtr previewParent, bool preview, LibVLC libVlc, string videoPath, string snapshot = null, ClockOptions clock = null) : this()
        {
            _screen = screen; _previewParent = previewParent; _preview = preview;
            WindowStartupLocation = WindowStartupLocation.Manual;
            if (preview) { Topmost = false; Cursor = System.Windows.Input.Cursors.Arrow; }
            SourceInitialized += (_, __) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(hwnd).AddHook(WindowMessage);
                if (_preview)
                {
                    SetWindowLong(hwnd, -16, 0x40000000 | 0x10000000 | 0x02000000);
                    SetParent(hwnd, _previewParent);
                }
                FitWindow();
            };
            PreviewKeyDown += (_, args) => { if (!_preview) App.KeyboardInput(System.Windows.Input.KeyInterop.VirtualKeyFromKey(
                args.Key == System.Windows.Input.Key.System ? args.SystemKey : args.Key)); };
            PreviewMouseDown += (_, __) => { if (!_preview) App.RequestExit("Mouse input"); };
            PreviewMouseWheel += (_, __) => { if (!_preview) App.RequestExit("Mouse wheel"); };
            Closing += OnClosing;
            SizeChanged += (_, __) => _session?.Resize(Aspect());
            Loaded += (_, __) =>
            {
                if (_session != null) return;
                _session = new PlaybackSession(libVlc, videoPath, Player.VideoHandle, Aspect(),
                    App.RequestExit, () => Dispatcher.BeginInvoke(new Action(Player.BlackenVideoChildren)), snapshot);
                if (clock != null && clock.Style != ClockStyle.Off && snapshot == null)
                    _clockOverlay = new ClockOverlay(new WindowInteropHelper(this).Handle, clock);
            };
            // Start before Loaded as hidden/reparented previews can defer that event.
            _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _healthTimer.Tick += (_, __) =>
                {
                    if (_preview)
                    {
                        if (!IsWindow(_previewParent)) { App.RequestExit("Preview host closed"); return; }
                        FitWindow();
                    }
                    Player.BlackenVideoChildren();
                    _clockOverlay?.Update();
                    if ((_session == null && _startup.ElapsedMilliseconds >= SafetyPolicy.PlaybackTimeoutMs) || _session?.IsStalled == true)
                        App.RequestExit("No video progress for 15 seconds");
                };
            _healthTimer.Start();
        }
        private void FitWindow()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (_preview)
            {
                if (GetClientRect(_previewParent, out Rect r) && (r.Right != _previewWidth || r.Bottom != _previewHeight))
                {
                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, r.Right, r.Bottom, 0x14);
                    _previewWidth = r.Right; _previewHeight = r.Bottom;
                }
            }
            else if (_screen != null)
            {
                var bounds = _screen.Bounds;
                // HWND positions use physical pixels, independent of WPF DPI units.
                SetWindowPos(hwnd, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10);
            }
        }
        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!_preview && (message == 0x0100 || message == 0x0104)) App.KeyboardInput(wParam.ToInt32());
            // Reparenting a preview can itself generate WM_DISPLAYCHANGE (DPI reset).
            if (!_preview && IsLoaded && message == 0x007E) App.RequestExit("Display configuration changed");
            else if (message == 0x02E0) Dispatcher.BeginInvoke(new Action(FitWindow));
            return IntPtr.Zero;
        }
        private string Aspect() => $"{Math.Max(1, (int)Math.Round(ActualWidth))}:{Math.Max(1, (int)Math.Round(ActualHeight))}";
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (!App.CanCloseWindows) { e.Cancel = true; App.RequestExit("Window close"); }
        }
        internal Task StopAsync()
        {
            _healthTimer?.Stop(); _clockOverlay?.Dispose(); _clockOverlay = null; Hide();
            return _session?.StopAsync() ?? Task.CompletedTask;
        }
    }
}
