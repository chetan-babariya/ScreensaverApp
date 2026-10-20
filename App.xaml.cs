using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using ScreensaverApp.Configuration;
using Screen = System.Windows.Forms.Screen;

namespace ScreensaverApp
{
    public partial class App : Application
    {
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        private readonly List<MainWindow> _windows = new List<MainWindow>();
        private RuntimeGuard _guard;
        private LibVLC _libVlc;
        private DispatcherTimer _heartbeat;
        private int _exitRequested;
        private Mutex _instance;
        internal static bool CanCloseWindows { get; private set; }
        internal static void KeyboardInput(int key) => (Current as App)?._guard?.KeyboardInput(key);
        internal static void RequestExit(string reason)
        {
            var app = Current as App;
            if (app == null || Interlocked.Exchange(ref app._exitRequested, 1) != 0) return;
            app._guard?.ArmExit(); RuntimeLog.Write("Exit: " + reason);
            app.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(app.ExitSafely));
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e); ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string mode = e.Args.Length == 0 ? "/c" : e.Args[0].Split(':')[0].ToLowerInvariant().Replace('-', '/');
            if (mode == "/c")
            {
                string manager = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), SaverSettings.ManagerFile);
                if (File.Exists(manager)) Process.Start(new ProcessStartInfo(manager) { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
                else MessageBox.Show("Open CineSaver Manager from the Start menu to choose a video or change settings.", "CineSaver settings");
                Shutdown(); return;
            }
            bool preview = mode == "/p";
            if (!preview && mode != "/s") { Shutdown(); return; }
            IntPtr parent = IntPtr.Zero;
            if (preview)
            {
                string handle = e.Args[0].Contains(":") ? e.Args[0].Split(':')[1] : e.Args.ElementAtOrDefault(1);
                if (!long.TryParse(handle, out long value) || value == 0 || !IsWindow(new IntPtr(value))) { Shutdown(); return; }
                parent = new IntPtr(value);
            }
            else
            {
                _instance = new Mutex(false, "Local\\ScreensaverApp.Fullscreen");
                bool owns;
                try { owns = _instance.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
                if (!owns) { Shutdown(); return; }
            }
            _guard = new RuntimeGuard(!preview, RequestExit, !preview && e.Args.Contains("/shortcut"));
            if (Volatile.Read(ref _exitRequested) != 0) _guard.ArmExit();
            _heartbeat = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(100) };
            _heartbeat.Tick += (_, __) => _guard.Pulse(); _heartbeat.Start();
            DispatcherUnhandledException += (_, args) =>
            { RuntimeLog.Write(args.Exception.ToString()); args.Handled = true; RequestExit("UI error"); };
            try
            {
                string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string root = SaverSettings.FindRoot(directory);
                bool compatibility = root != directory || !File.Exists(Path.Combine(directory, "hardware-playback.enabled"));
                var preferences = SaverSettings.Load(root, out string warning, compatibility);
                if (warning != null) RuntimeLog.Write(warning);
                string video = SaverSettings.ResolveVideo(root, directory, preferences);
                compatibility = preferences.Compatibility;
                // Manager drafts are scoped to this preview process; never persist them.
                string snapshot = null;
                if (preview)
                {
                    for (int i = 2; i < e.Args.Length; i++)
                    {
                        if (e.Args[i] == "/video" && i + 1 < e.Args.Length) video = Path.GetFullPath(e.Args[++i]);
                        else if (e.Args[i] == "/playback" && i + 1 < e.Args.Length) compatibility = e.Args[++i] != "auto";
                        else if (e.Args[i] == "/snapshot" && i + 1 < e.Args.Length) snapshot = Path.GetFullPath(e.Args[++i]);
                        else if (e.Args[i] == "/clock" && i + 1 < e.Args.Length) preferences.Clock = ClockOptions.Decode(e.Args[++i]);
                    }
                    if (!File.Exists(video)) { RequestExit("Preview video is missing"); return; }
                }
                if (!File.Exists(video)) { RequestExit("Video file is missing"); return; }
                if (compatibility) RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                Core.Initialize(Path.Combine(directory, "libvlc", "win-x64"));
                var options = new List<string> { "--no-audio", "--no-video-title-show", "--no-osd", "--no-snapshot-preview" };
                if (compatibility) { options.Add("--avcodec-hw=none"); options.Add("--vout=wingdi"); }
                _libVlc = new LibVLC(options.ToArray());
                RuntimeLog.Write("Start 1.6.0; mode=" + mode + "; compatibility=" + compatibility + "; video=" + video + "; path=" + directory);
                if (preview)
                {
                    var window = new MainWindow(null, parent, true, _libVlc, video, snapshot, preferences.Clock);
                    _windows.Add(window); window.Show();
                }
                else foreach (Screen screen in Screen.AllScreens)
                {
                    if (Volatile.Read(ref _exitRequested) != 0) break;
                    var window = new MainWindow(screen, IntPtr.Zero, false, _libVlc, video, null, preferences.Clock);
                    _windows.Add(window); window.Show();
                }
            }
            catch (Exception ex) { RuntimeLog.Write(ex.ToString()); RequestExit("Startup failed"); }
        }
        private async void ExitSafely()
        {
            try
            {
                await Task.WhenAll(_windows.Select(window => window.StopAsync()));
                await Task.Run(() => { _libVlc?.Dispose(); RuntimeLog.Flush(); });
            }
            catch (Exception ex) { RuntimeLog.Write("Exit cleanup: " + ex.Message); }
            finally { CanCloseWindows = true; Shutdown(); }
        }
        protected override void OnExit(ExitEventArgs e)
        {
            _heartbeat?.Stop(); _instance?.Dispose(); base.OnExit(e);
            // Leave the watchdog armed until the process actually exits.
        }
    }
}
