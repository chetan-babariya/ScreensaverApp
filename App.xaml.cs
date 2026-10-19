using System;
using System.IO;
using System.Reflection;
using System.Windows;
using LibVLCSharp.Shared;
using Screen = System.Windows.Forms.Screen;

namespace ScreensaverApp
{
    public partial class App : Application
    {
        public static event Action ExitRequested;

        /// <summary>Any window can call this to tear down every screensaver window at once.</summary>
        public static void RequestExit() => ExitRequested?.Invoke();

        private enum SaverMode { FullScreen, Config, Preview }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // We manage window lifetime ourselves (config mode shows no window at all).
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (!InitializeLibVlc())
            {
                Shutdown();
                return;
            }

            var mode = ParseMode(e.Args, out IntPtr previewHandle);

            switch (mode)
            {
                case SaverMode.Preview:
                    RunPreview(previewHandle);
                    break;
                case SaverMode.Config:
                    RunConfig();
                    break;
                default:
                    RunFullScreen();
                    break;
            }
        }

        /// <summary>
        /// Locates and loads the native libvlc DLLs. Checked against a couple of known
        /// folder layouts rather than relying on Core.Initialize()'s own guess, since a
        /// wrong guess throws before any window exists.
        /// </summary>
        private bool InitializeLibVlc()
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";

            string[] candidates =
            {
                Path.Combine(exeDir, "libvlc", "win-x64"),
                Path.Combine(exeDir, "libvlc"),
                exeDir
            };

            foreach (string dir in candidates)
            {
                bool hasCore = File.Exists(Path.Combine(dir, "libvlc.dll"))
                            && File.Exists(Path.Combine(dir, "libvlccore.dll"));
                bool hasPlugins = Directory.Exists(Path.Combine(dir, "plugins"));

                if (!hasCore || !hasPlugins) continue;

                try
                {
                    Core.Initialize(dir);
                    return true;
                }
                catch
                {
                    // Try the next candidate layout.
                }
            }

            return false;
        }

        /// <summary>
        /// Windows invokes screensavers with one of:
        ///   /s              -> run fullscreen
        ///   /c or /c:HWND   -> show configuration dialog
        ///   /p HWND         -> render into the small preview box in Settings
        /// Real-world callers are inconsistent about "/" vs "-" and ":" vs space,
        /// so we normalize before matching.
        /// </summary>
        private SaverMode ParseMode(string[] args, out IntPtr previewHandle)
        {
            previewHandle = IntPtr.Zero;

            if (args == null || args.Length == 0)
                return SaverMode.Config;

            string first = args[0].Trim();
            string flag = first.Split(':')[0].ToLowerInvariant();

            if (flag == "/p" || flag == "-p")
            {
                string handleText = first.Contains(":")
                    ? first.Substring(first.IndexOf(':') + 1)
                    : (args.Length > 1 ? args[1] : "0");

                if (long.TryParse(handleText, out long h))
                    previewHandle = new IntPtr(h);

                return SaverMode.Preview;
            }

            if (flag == "/c" || flag == "-c")
                return SaverMode.Config;

            if (flag == "/s" || flag == "-s")
                return SaverMode.FullScreen;

            return SaverMode.FullScreen;
        }

        private void RunConfig()
        {
            MessageBox.Show(
                "This screensaver has no configurable options.",
                "Screensaver Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
        }

        private void RunPreview(IntPtr previewHandle)
        {
            if (previewHandle == IntPtr.Zero)
            {
                Shutdown();
                return;
            }

            new MainWindow(isPreview: true, previewParent: previewHandle).Show();
        }

        private void RunFullScreen()
        {
            // One borderless topmost window per monitor, all torn down together.
            ExitRequested += () => Dispatcher.Invoke(Shutdown);

            foreach (var screen in Screen.AllScreens)
                new MainWindow(screen).Show();
        }
    }
}
