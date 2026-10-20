using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using ScreensaverApp.Configuration;
using ScreensaverApp.Management;
using Forms = System.Windows.Forms;

namespace ScreensaverApp.Tests
{
    internal static partial class Program
    {
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr hwnd, EnumProc callback, IntPtr state);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        private delegate bool EnumProc(IntPtr hwnd, IntPtr state);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
        [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll")] private static extern bool EnumDesktopWindows(IntPtr desktop, EnumProc callback, IntPtr state);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(string application, System.Text.StringBuilder command, IntPtr processSecurity,
            IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string directory, ref Startup startup, out ProcessInfo information);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Startup
        {
            public uint Size; public string Reserved, Desktop, Title;
            public uint X, Y, Width, Height, CharWidth, CharHeight, Fill, Flags;
            public ushort Show, ReservedSize; public IntPtr Reserved2, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public IntPtr Process, Thread; public uint Id, ThreadId; }
        private static int _passed;
        private static void Check(bool condition, string name)
        { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); _passed++; }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--hung-ui")
            { var guard = new RuntimeGuard(false, _ => { }); Thread.Sleep(Timeout.Infinite); GC.KeepAlive(guard); return 9; }
            if (args.Length > 0 && args[0] == "--hung-exit")
            { var guard = new RuntimeGuard(false, _ => { }); guard.ArmExit(); Thread.Sleep(Timeout.Infinite); GC.KeepAlive(guard); return 9; }
            if (args.Length > 0 && args[0] == "--input-registration")
            { using (var guard = new RuntimeGuard(true, _ => { })) { guard.Pulse(); Thread.Sleep(400); } return 0; }
            if (args.Length == 3 && args[0] == "--hold-directory")
            { Environment.CurrentDirectory = args[1]; File.WriteAllText(args[2], "ready"); Thread.Sleep(1800); return 0; }
            try
            {
                if (args.Length == 2 && args[0] == "--preview")
                { TestPreview(Path.GetFullPath(args[1])); Console.WriteLine("TOTAL: " + _passed + " checks passed."); return 0; }
                if (args.Length == 2 && args[0] == "--installer")
                { TestInstaller(Path.GetFullPath(args[1])); Console.WriteLine("TOTAL: " + _passed + " checks passed."); return 0; }
                if (args.Length == 2 && args[0] == "--uninstall-locks")
                { TestUninstallLocks(Path.GetFullPath(args[1])); Console.WriteLine("TOTAL: " + _passed + " checks passed."); return 0; }
                if (args.Length == 2 && args[0] == "--manager")
                { RunWithMessageLoop(() => TestManager(Path.GetFullPath(args[1]))); Console.WriteLine("TOTAL: " + _passed + " checks passed."); return 0; }
                if (args.Length == 2 && args[0] == "--customization")
                { RunWithMessageLoop(() => TestCustomization(Path.GetFullPath(args[1]))); Console.WriteLine("TOTAL: " + _passed + " checks passed."); return 0; }
                if (args.Length == 2 && args[0] == "--branding")
                { TestBranding(Path.GetFullPath(args[1])); Console.WriteLine("TOTAL: " + _passed + " checks passed."); return 0; }
                if (args.Length == 2 && args[0] == "--migration")
                { TestMigration(Path.GetFullPath(args[1])); Console.WriteLine("TOTAL: " + _passed + " checks passed."); return 0; }
                if (args.Length == 2 && args[0] == "--fullscreen")
                { TestFullscreen(Path.GetFullPath(args[1])); Console.WriteLine("TOTAL: " + _passed + " checks passed."); return 0; }
                if (args.Length == 2 && args[0] == "--failures")
                { TestFailures(Path.GetFullPath(args[1])); Console.WriteLine("TOTAL: " + _passed + " checks passed."); return 0; }
                if (args.Length == 2 && args[0] == "--setup-ui")
                {
                    using (var form = new Setup.SetupWindow())
                    {
                        form.Show(); Pump(500); Capture(form, Path.GetFullPath(args[1]));
                        var content = FindControl<Forms.TableLayoutPanel>(form, control => control.Name == "SetupContent");
                        Check(content != null && content.DisplayRectangle.Height >= content.ClientSize.Height - content.Padding.Vertical,
                            "installer controls fit without scrolling");
                        var videoName = FindControl<Forms.Label>(form, control => control.Name == "SetupVideoName");
                        Check(videoName != null && videoName.Height >= videoName.Font.Height + 2 && videoName.Parent.ClientRectangle.Contains(videoName.Bounds),
                            "installer video filename has enough room and stays inside its card");
                        form.Enabled = false; Pump(50); Capture(form, Path.ChangeExtension(Path.GetFullPath(args[1]), ".saving.png"));
                        Check(WhiteTextPixels(FindControl<Forms.Button>(form, control => control.Text == "Install / Update")) > 20,
                            "installer primary button stays readable while installing");
                        form.Enabled = true;
                        form.Close();
                    }
                    return 0;
                }
                Check(!SafetyPolicy.Moved(-100, 200, -108, 192) && SafetyPolicy.Moved(-99, 200, -108, 192), "mouse tolerance and negative screen coordinates");
                Check(!SafetyPolicy.Expired(1999, 0, 2000) && SafetyPolicy.Expired(2000, 0, 2000), "deadline boundary");
                ChildDeadline("--hung-exit", 2, 1600, 5000);
                ChildDeadline("--hung-ui", 3, 7500, 11000);
                ChildDeadline("--input-registration", 0, 200, 4000);
                TestArchiveTraversal();
                if (args.Length == 1) TestPreview(Path.GetFullPath(args[0]));
                Console.WriteLine("TOTAL: " + _passed + " checks passed."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static void ChildDeadline(string argument, int code, int minimum, int maximum)
        {
            var watch = Stopwatch.StartNew();
            using (var process = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, argument) { UseShellExecute = false, CreateNoWindow = true }))
            {
                bool exited = process.WaitForExit(maximum);
                if (!exited) process.Kill();
                Check(exited && process.ExitCode == code && watch.ElapsedMilliseconds >= minimum, argument + " (" + watch.ElapsedMilliseconds + "ms)");
            }
        }
        private static void TestArchiveTraversal()
        {
            string root = Path.Combine(Path.GetTempPath(), "ScreensaverApp-zip-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var stream = new MemoryStream())
                {
                    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                        using (var writer = new StreamWriter(zip.CreateEntry("../escaped.txt").Open())) writer.Write("must not escape");
                    stream.Position = 0;
                    bool rejected = false;
                    try { Setup.InstallEngine.Extract(root, stream); } catch (InvalidDataException) { rejected = true; }
                    Check(rejected && !File.Exists(Path.Combine(root, "..", "escaped.txt")), "installer rejects archive path traversal");
                }
                bool rejectedDelete = false;
                try { Setup.InstallEngine.DeleteOwnedDirectory(Path.GetDirectoryName(root), root); } catch (IOException) { rejectedDelete = true; }
                Check(rejectedDelete, "installer refuses deletion outside its managed directory");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root); }
        }
        private static Process StartSaver(string saver, string arguments) => Process.Start(new ProcessStartInfo(saver, arguments)
        { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(saver), CreateNoWindow = true });
        private static void TestInstaller(string payload)
        {
            string id = Guid.NewGuid().ToString("N");
            string root = Path.Combine(Path.GetDirectoryName(payload), "install-test-" + id);
            string keyName = "Software\\ScreensaverAppSmokeTests\\" + id;
            using (RegistryKey registry = Registry.CurrentUser.CreateSubKey(keyName))
            {
                try
                {
                    using (RegistryKey desktop = registry.CreateSubKey("Control Panel\\Desktop"))
                    {
                        desktop.SetValue("SCRNSAVE.EXE", "C:\\Previous.scr"); desktop.SetValue("ScreenSaveTimeOut", "600");
                        desktop.SetValue("ScreenSaveActive", "0"); desktop.SetValue("ScreenSaverIsSecure", "1");
                    }
                    int notifications = 0;
                    var engine = new Setup.InstallEngine(root, registry, (_, __) => notifications++, () => File.OpenRead(payload));
                    string first = engine.Install(null, true, 300);
                    using (RegistryKey desktop = registry.OpenSubKey("Control Panel\\Desktop", true))
                    {
                        Check(File.Exists(first) && desktop.GetValue("SCRNSAVE.EXE").ToString() == first, "installer selects the extracted .scr");
                        Check(Path.GetFileName(first) == "CineSaver.scr" && File.Exists(Path.Combine(root, SaverSettings.SetupFile)), "installed player and setup use the CineSaver filenames");
                        using (RegistryKey entry = registry.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\ScreensaverApp"))
                            Check(entry.GetValue("DisplayName").ToString() == "CineSaver" && entry.GetValue("UninstallString").ToString().Contains(SaverSettings.SetupFile),
                                "Windows Apps displays CineSaver with the renamed uninstaller");
                        Check(!File.Exists(Path.Combine(Path.GetDirectoryName(first), "hardware-playback.enabled")), "compatibility mode is the installation default");
                        Check(desktop.GetValue("ScreenSaverIsSecure").ToString() == "1", "installer preserves resume/security preference");
                        string custom = Path.Combine(Path.GetDirectoryName(payload), "test-video-" + id + ".mp4");
                        try
                        {
                            File.WriteAllText(custom, "fixture-video");
                            string second = engine.Install(custom, false, 120);
                            Check(second != first && !File.Exists(first), "update replaces selection and removes previous managed release");
                            var preferences = SaverSettings.Load(root, out _);
                            Check(File.ReadAllText(SaverSettings.ResolveVideo(root, Path.GetDirectoryName(second), preferences)) == "fixture-video", "chosen video is copied into managed storage");
                            Check(File.Exists(Path.Combine(Path.GetDirectoryName(second), "hardware-playback.enabled")), "hardware mode is opt-in");
                            engine.Uninstall();
                            Check(desktop.GetValue("SCRNSAVE.EXE").ToString() == "C:\\Previous.scr" && desktop.GetValue("ScreenSaveTimeOut").ToString() == "600" && desktop.GetValue("ScreenSaveActive").ToString() == "0", "uninstall restores original settings across updates");
                            Check(!Directory.Exists(root), "uninstall removes application files");
                            engine.Install(null, true, 300);
                            desktop.SetValue("SCRNSAVE.EXE", "C:\\Another.scr"); desktop.SetValue("ScreenSaveTimeOut", "123");
                            engine.Uninstall();
                            Check(desktop.GetValue("SCRNSAVE.EXE").ToString() == "C:\\Another.scr" && desktop.GetValue("ScreenSaveTimeOut").ToString() == "123", "uninstall preserves subsequently selected third-party saver");
                            Check(notifications == 8, "settings notifications apply only to managed selection");
                        }
                        finally { if (File.Exists(custom)) File.Delete(custom); }
                    }
                }
                finally
                {
                    if (Directory.Exists(root)) Setup.InstallEngine.DeleteOwnedDirectory(root, Path.GetDirectoryName(root));
                    Registry.CurrentUser.DeleteSubKeyTree(keyName, false);
                }
            }
        }
        private static void TestUninstallLocks(string payload)
        {
            string parent = Path.GetDirectoryName(payload), id = Guid.NewGuid().ToString("N");
            string root = Path.Combine(parent, "uninstall-lock-test-" + id), key = "Software\\ScreensaverAppSmokeTests\\" + id;
            string originalDirectory = Environment.CurrentDirectory;
            using (RegistryKey registry = Registry.CurrentUser.CreateSubKey(key))
            {
                try
                {
                    using (RegistryKey desktop = registry.CreateSubKey("Control Panel\\Desktop"))
                    { desktop.SetValue("SCRNSAVE.EXE", "C:\\Before.scr"); desktop.SetValue("ScreenSaveActive", "0"); desktop.SetValue("ScreenSaveTimeOut", "600"); }
                    var engine = new Setup.InstallEngine(root, registry, (_, __) => { }, () => File.OpenRead(payload));
                    string saver = engine.Install(null, true, 300);
                    Environment.CurrentDirectory = Path.GetDirectoryName(saver);
                    engine.Uninstall();
                    Check(!Directory.Exists(root) && !Environment.CurrentDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase),
                        "uninstall relocates its own working directory before removing the installation");
                    Environment.CurrentDirectory = originalDirectory;

                    engine.Install(null, true, 300);
                    string oldRelease = Path.Combine(root, "Releases", "empty-old-release"), ready = Path.Combine(parent, "ready-" + id);
                    Directory.CreateDirectory(oldRelease);
                    using (var holder = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                        "--hold-directory \"" + oldRelease + "\" \"" + ready + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() }))
                    {
                        try
                        {
                            var startup = Stopwatch.StartNew();
                            while (!File.Exists(ready) && startup.ElapsedMilliseconds < 4000) Thread.Sleep(20);
                            Check(File.Exists(ready), "test child holds an empty old release directory open");
                            var wait = Stopwatch.StartNew(); engine.Uninstall();
                            Check(!Directory.Exists(root) && wait.ElapsedMilliseconds >= 500,
                                "uninstall retries a transient directory lock and completes after the holder exits");
                        }
                        finally { if (!holder.HasExited) { holder.Kill(); holder.WaitForExit(3000); } File.Delete(ready); }
                    }

                    engine.Install(null, true, 300);
                    bool blocked = false;
                    using (var locked = new FileStream(Path.Combine(root, "settings.xml"), FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        try { engine.Uninstall(); } catch (IOException) { blocked = true; }
                        using (var state = registry.OpenSubKey(SaverSettings.StateKey))
                            Check(blocked && File.Exists(Path.Combine(root, ".installation")) && state?.GetValue("Saved") != null,
                                "a persistent file lock preserves the installation marker and original-settings backup");
                    }
                    engine.Uninstall();
                    using (var desktop = registry.OpenSubKey("Control Panel\\Desktop"))
                        Check(!Directory.Exists(root) && desktop.GetValue("SCRNSAVE.EXE").ToString() == "C:\\Before.scr",
                            "a failed uninstall can be retried without losing the original Windows settings");
                }
                finally
                {
                    Environment.CurrentDirectory = originalDirectory;
                    if (Directory.Exists(root)) Setup.InstallEngine.DeleteOwnedDirectory(root, parent);
                    Registry.CurrentUser.DeleteSubKeyTree(key, false);
                }
            }
        }
        private static void RunWithMessageLoop(Action action)
        {
            // Match production Application.Run: transient preview/thumbnail Forms must not
            // uninstall the UI synchronization context between asynchronous operations.
            Exception failure = null;
            using (var host = new Forms.Form { ShowInTaskbar = false, Opacity = 0, StartPosition = Forms.FormStartPosition.Manual, Location = new Point(-16000, -16000) })
            {
                host.Shown += (_, __) => host.BeginInvoke(new Action(() =>
                { try { action(); } catch (Exception ex) { failure = ex; } finally { host.Close(); } }));
                Forms.Application.Run(host);
            }
            if (failure != null) throw new Exception("UI integration test failed", failure);
        }
        private static void TestManager(string payload)
        {
            string id = Guid.NewGuid().ToString("N"), parent = Path.GetDirectoryName(payload);
            string root = Path.Combine(parent, "manager-test-" + id), key = "Software\\ScreensaverAppSmokeTests\\" + id;
            string source = Path.Combine(parent, "Selected clip " + id + ".mp4");
            string results = Path.Combine(parent, "manager-test-results"); Directory.CreateDirectory(results);
            using (RegistryKey registry = Registry.CurrentUser.CreateSubKey(key))
            {
                try
                {
                    using (RegistryKey desktop = registry.CreateSubKey("Control Panel\\Desktop"))
                    {
                        desktop.SetValue("SCRNSAVE.EXE", "C:\\Previous.scr"); desktop.SetValue("ScreenSaveActive", "1");
                        desktop.SetValue("ScreenSaveTimeOut", "600"); desktop.SetValue("ScreenSaverIsSecure", "1");
                    }
                    var engine = new Setup.InstallEngine(root, registry, (_, __) => { }, () => File.OpenRead(payload));
                    string saver = engine.Install(null, true, 300);
                    var service = new ManagerService(root, registry, (_, __) => { });
                    ManagerSnapshot initial = service.Load();
                    Check(initial.Enabled && initial.Preferences.Compatibility && initial.VideoPath == Path.Combine(Path.GetDirectoryName(saver), "screensaver.mp4"), "manager loads included video and current activation");
                    string linkPath = Path.Combine(engine.ShortcutFolder, "CineSaver Manager.lnk");
                    Check(File.Exists(linkPath), "installer creates Start menu manager shortcut");
                    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                    dynamic link = shell.CreateShortcut(linkPath);
                    try
                    {
                        Check(string.Equals((string)link.TargetPath, Path.Combine(Path.GetDirectoryName(saver), SaverSettings.ManagerFile), StringComparison.OrdinalIgnoreCase), "Start menu shortcut targets the installed manager");
                        Check(string.Equals((string)link.IconLocation, (string)link.TargetPath + ",0", StringComparison.OrdinalIgnoreCase), "shortcut explicitly uses the embedded application icon");
                        Check(string.Equals(((string)link.WorkingDirectory).TrimEnd(Path.DirectorySeparatorChar), Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase),
                            "manager shortcut starts outside the installation so external tools cannot inherit release-directory locks");
                    }
                    finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
                    File.Copy(initial.VideoPath, source);
                    File.SetAttributes(source, File.GetAttributes(source) | FileAttributes.ReadOnly);
                    ManagerSnapshot custom = service.Apply(source, false, false, 120, false);
                    File.SetAttributes(source, FileAttributes.Normal);
                    File.Delete(source);
                    Check(File.Exists(custom.VideoPath) && custom.VideoPath.StartsWith(Path.Combine(root, "Videos")) && custom.Preferences.VideoName.StartsWith("Selected clip"), "chosen video is independent of the original file");
                    Check((File.GetAttributes(custom.VideoPath) & FileAttributes.ReadOnly) == 0, "imported read-only videos remain replaceable and uninstallable");
                    Check(!custom.Enabled && !custom.Preferences.Compatibility && custom.Preferences.IdleSeconds == 120, "manager saves disable, idle time and playback mode");
                    Check(SaverSettings.ResolveVideo(SaverSettings.FindRoot(Path.GetDirectoryName(saver)), Path.GetDirectoryName(saver), SaverSettings.Load(root, out _)) == custom.VideoPath, "runtime resolves the manager-selected video");
                    string updated = engine.Install(null, false, 120);
                    Check(updated != saver && service.Load().VideoPath == custom.VideoPath && !service.Load().Preferences.Compatibility && !service.Load().Enabled,
                        "app update preserves video, playback mode and disabled state");
                    using (RegistryKey desktop = registry.OpenSubKey("Control Panel\\Desktop", true))
                    {
                        desktop.SetValue("SCRNSAVE.EXE", "C:\\Another.scr"); desktop.SetValue("ScreenSaveActive", "1"); desktop.SetValue("ScreenSaveTimeOut", "900");
                        service.Apply(null, false, true, 180, false);
                        Check(desktop.GetValue("SCRNSAVE.EXE").ToString() == "C:\\Another.scr" && desktop.GetValue("ScreenSaveActive").ToString() == "1" && desktop.GetValue("ScreenSaveTimeOut").ToString() == "900", "disabled manager changes preserve another screensaver");
                        updated = engine.Install(null, true, 180);
                        Check(desktop.GetValue("SCRNSAVE.EXE").ToString() == "C:\\Another.scr" && desktop.GetValue("ScreenSaveActive").ToString() == "1" && desktop.GetValue("ScreenSaveTimeOut").ToString() == "900",
                            "updating the app preserves another selected Windows screensaver");
                    }
                    service.Apply(null, false, true, 180, true);
                    using (var form = new ManagerWindow(service))
                    {
                        form.Show();
                        var thumbnail = FindControl<Forms.PictureBox>(form, control => control.Name == "VideoThumbnail");
                        var thumbnailWatch = Stopwatch.StartNew();
                        while (thumbnail.Image == null && thumbnailWatch.ElapsedMilliseconds < 14000) Pump(50);
                        if (thumbnail.Image == null)
                        {
                            Capture(form, Path.Combine(results, "thumbnail-failure.png"));
                            foreach (string field in new[] { "_status", "_thumbStatus", "_saved" })
                            {
                                object value = typeof(ManagerWindow).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                                Console.WriteLine(field + "=" + (value is Forms.Control control ? control.Text + "; parent=" + control.Parent : value?.ToString() ?? "null"));
                            }
                            Console.WriteLine("manager visible=" + form.Visible);
                        }
                        Check(thumbnail.Image != null && thumbnail.Image.Width == 640, "manager automatically generates a video thumbnail");
                        bool hasVideoPixels = false;
                        using (var pixels = new Bitmap(thumbnail.Image))
                            for (int y = 0; y < pixels.Height; y += 8)
                                for (int x = 0; x < pixels.Width; x += 8)
                                { Color pixel = pixels.GetPixel(x, y); if (pixel.R + pixel.G + pixel.B > 120) hasVideoPixels = true; }
                        Check(hasVideoPixels, "thumbnail contains decoded video rather than an empty black surface");
                        Capture(form, Path.Combine(results, "manager.png"));
                        var applyButton = FindControl<Forms.Button>(form, control => control.Name == "ApplyChanges");
                        var brandLabel = FindControl<Forms.Label>(form, control => control.Text == "CineSaver");
                        int enabledWhite = WhiteTextPixels(applyButton);
                        int enabledBrandWhite = WhiteTextPixels(brandLabel);
                        form.Enabled = false; Pump(50);
                        int disabledWhite = WhiteTextPixels(applyButton);
                        Capture(form, Path.Combine(results, "manager-saving.png"));
                        Check(disabledWhite > 20 && disabledWhite >= enabledWhite / 2,
                            "Apply retains white text while the manager is disabled during saving");
                        Check(WhiteTextPixels(brandLabel) > 50 && WhiteTextPixels(brandLabel) >= enabledBrandWhite / 2,
                            "sidebar heading retains white text while saving");
                        form.Enabled = true; Pump(50);
                        Check(WhiteTextPixels(applyButton) > 20, "Apply returns to readable white text after saving");
                        form.Size = form.MinimumSize; Pump(100);
                        Capture(form, Path.Combine(results, "manager-minimum.png"));
                        Check(FindControl<Forms.Button>(form, control => control.Name == "ApplyChanges").Visible,
                            "manager primary actions are visible");
                        FindControl<Forms.Button>(form, control => control.Text == "Developer").PerformClick(); Pump(100);
                        Check(FindControl<Forms.Label>(form, control => control.Text == DeveloperInfo.Name).Visible &&
                            FindControl<Forms.Label>(form, control => control.Name == "DeveloperCredit").Text == "Developed by " + DeveloperInfo.Name,
                            "Developer page and footer share editable profile details");
                        Capture(form, Path.Combine(results, "developer.png"));
                        FindControl<Forms.Button>(form, control => control.Text == "Screensaver").PerformClick();
                        File.Copy(custom.VideoPath, source);
                        string beforeDraft = File.ReadAllText(Path.Combine(root, "settings.xml"));
                        form.SelectVideo(source, false);
                        ManagerSnapshot candidate = form.PreviewCandidate();
                        Check(candidate.VideoPath == source && service.Load().VideoPath != source, "preview candidate uses unsaved selected file directly");
                        var mode = FindControl<Forms.ComboBox>(form, control => control.Name == "PlaybackMode");
                        mode.SelectedIndex = 1;
                        Check(!form.PreviewCandidate().Preferences.Compatibility && service.Load().Preferences.Compatibility,
                            "preview candidate uses pending playback mode without saving it");
                        mode.SelectedIndex = 0;
                        bool playedDraft = false, sawDialog = false;
                        using (var closeDialog = new Forms.Timer { Interval = 100 })
                        {
                            var dialogWatch = Stopwatch.StartNew();
                            closeDialog.Tick += (_, __) =>
                            {
                                PreviewWindow dialog = null;
                                foreach (Forms.Form window in Forms.Application.OpenForms) if (window is PreviewWindow found) { dialog = found; break; }
                                if (dialog == null) return;
                                if (!sawDialog) { sawDialog = true; dialogWatch.Restart(); }
                                var child = (Process)typeof(PreviewWindow).GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dialog);
                                playedDraft |= child != null && !child.HasExited && child.StartInfo.Arguments.Contains(source);
                                if (dialogWatch.ElapsedMilliseconds >= 2000) { closeDialog.Stop(); dialog.Close(); }
                            };
                            closeDialog.Start();
                            FindControl<Forms.Button>(form, control => control.Name == "PreviewSelection").PerformClick();
                            var openWatch = Stopwatch.StartNew();
                            while (closeDialog.Enabled && openWatch.ElapsedMilliseconds < 15000) Pump(30);
                            Pump(500);
                            Check(sawDialog && playedDraft, "Preview selection button plays the draft path directly");
                        }
                        Check(File.ReadAllText(Path.Combine(root, "settings.xml")) == beforeDraft && service.Load().Enabled,
                            "draft preview leaves saved video and Windows activation unchanged");
                        form.SelectVideo(null, true);
                        Check(form.PreviewCandidate().VideoPath == Path.Combine(Path.GetDirectoryName(updated), "screensaver.mp4"), "included-video draft previews bundled file before applying");
                        FindControl<Forms.Button>(form, control => control.Text == "Discard changes").PerformClick();
                        Forms.CheckBox enabled = FindControl<Forms.CheckBox>(form, control => control.Text == "Enable this screensaver");
                        enabled.Checked = false;
                        FindControl<Forms.Button>(form, control => control.Text == "Apply changes").PerformClick();
                        var watch = Stopwatch.StartNew();
                        while ((!form.Enabled || service.Load().Enabled) && watch.ElapsedMilliseconds < 15000) Pump(30);
                        Check(form.Enabled && !service.Load().Enabled, "manager Apply button saves without rerunning Setup; status=" +
                            FindControl<Forms.Label>(form, control => control.Text.StartsWith("Changes could") || control.Text.StartsWith("Saving") || control.Text.StartsWith("Saved.") || control.Text.StartsWith("Unsaved"))?.Text);
                        form.Close(); var closeWatch = Stopwatch.StartNew();
                        while (!form.IsDisposed && closeWatch.ElapsedMilliseconds < 5500) Pump(30);
                        Check(form.IsDisposed, "manager close waits for thumbnail cleanup");
                    }
                    File.Delete(source);
                    ManagerSnapshot previewSettings = service.Load();
                    using (var preview = new PreviewWindow(previewSettings))
                    {
                        preview.Show(); Pump(3000);
                        var process = (Process)typeof(PreviewWindow).GetField("_process", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
                        Check(process != null && !process.HasExited, "manager preview plays saved custom video while saver is disabled");
                        int pid = process.Id;
                        Capture(preview, Path.Combine(results, "manager-preview.png"));
                        preview.ClientSize = new Size(640, 440); Pump(500);
                        preview.Close(); var watch = Stopwatch.StartNew();
                        while (!preview.IsDisposed && watch.ElapsedMilliseconds < 5500) Pump(30);
                        Check(preview.IsDisposed, "closing manager preview stops its playback process");
                        bool alive;
                        try { using (Process child = Process.GetProcessById(pid)) alive = !child.HasExited; } catch (ArgumentException) { alive = false; }
                        Check(!alive, "manager preview leaves no renderer running");
                    }
                    ManagerSnapshot restored = service.Apply(null, true, true, 300, true);
                    Check(restored.VideoPath == Path.Combine(Path.GetDirectoryName(updated), "screensaver.mp4") && string.IsNullOrEmpty(restored.Preferences.VideoFile), "Use included video restores the bundled default");
                    string saved = File.ReadAllText(Path.Combine(root, "settings.xml"));
                    bool rejected = false;
                    try { service.Apply(source, false, true, 300, true); } catch (IOException) { rejected = true; }
                    Check(rejected && File.ReadAllText(Path.Combine(root, "settings.xml")) == saved, "failed video selection preserves saved settings");
                    File.WriteAllText(Path.Combine(root, "settings.xml"), "<screensaver video=\"..\\outside.mp4\" />");
                    var safe = SaverSettings.Load(root, out string warning);
                    Check(warning != null && string.IsNullOrEmpty(safe.VideoFile), "invalid video paths fall back to included video");
                    SaverSettings.Save(root, restored.Preferences);
                    string log = Path.Combine(root, "Logs", "saver.log"); Directory.CreateDirectory(Path.GetDirectoryName(log)); File.WriteAllText(log, new string('x', 100000));
                    Check(service.ReadDiagnostics().Length <= 65536, "diagnostics read is bounded");
                    engine.Uninstall();
                    Check(!Directory.Exists(root), "uninstall removes manager, settings, video and owned shortcuts");
                }
                finally
                {
                    if (File.Exists(source)) { File.SetAttributes(source, FileAttributes.Normal); File.Delete(source); }
                    if (Directory.Exists(root)) Setup.InstallEngine.DeleteOwnedDirectory(root, parent);
                    Registry.CurrentUser.DeleteSubKeyTree(key, false);
                }
            }
        }
        private static void TestMigration(string payload)
        {
            string selectionRoot = Path.Combine(Path.GetTempPath(), "CineSaver-root-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                string current = Path.Combine(selectionRoot, "CineSaver"), previous = Path.Combine(selectionRoot, "ScreensaverApp");
                Check(SaverSettings.PreferredRoot(selectionRoot) == current, "new installations use the CineSaver data folder");
                Directory.CreateDirectory(previous);
                Check(SaverSettings.PreferredRoot(selectionRoot) == current, "an unrelated legacy-named folder does not hijack installation");
                File.WriteAllText(Path.Combine(previous, ".installation"), SaverSettings.Marker);
                Check(SaverSettings.PreferredRoot(selectionRoot) == previous, "existing managed installations keep their data folder after renaming");
                Directory.CreateDirectory(current); File.WriteAllText(Path.Combine(current, ".installation"), SaverSettings.Marker);
                Check(SaverSettings.PreferredRoot(selectionRoot) == current, "an established CineSaver installation remains the preferred root");
            }
            finally { if (Directory.Exists(selectionRoot)) Setup.InstallEngine.DeleteOwnedDirectory(selectionRoot, Path.GetDirectoryName(selectionRoot)); }
            string id = Guid.NewGuid().ToString("N"), parent = Path.GetDirectoryName(payload);
            string root = Path.Combine(parent, "migration-test-" + id), key = "Software\\ScreensaverAppSmokeTests\\" + id;
            using (RegistryKey registry = Registry.CurrentUser.CreateSubKey(key))
            {
                try
                {
                    string legacy = Path.Combine(root, "Releases", "1.1.0-legacy"); Directory.CreateDirectory(legacy);
                    File.WriteAllText(Path.Combine(root, ".installation"), SaverSettings.Marker);
                    string oldSaver = Path.Combine(legacy, "ScreensaverApp.scr"); File.WriteAllText(oldSaver, "legacy fixture");
                    File.WriteAllText(Path.Combine(legacy, "screensaver.mp4"), "legacy custom video");
                    using (RegistryKey desktop = registry.CreateSubKey("Control Panel\\Desktop"))
                    {
                        desktop.SetValue("SCRNSAVE.EXE", oldSaver); desktop.SetValue("ScreenSaveActive", "1"); desktop.SetValue("ScreenSaveTimeOut", "420");
                    }
                    using (RegistryKey state = registry.CreateSubKey(SaverSettings.StateKey))
                    {
                        state.SetValue("Saved", 1);
                        foreach (string name in new[] { "SCRNSAVE.EXE", "ScreenSaveActive", "ScreenSaveTimeOut" }) state.SetValue(name + ".Present", 1);
                        state.SetValue("SCRNSAVE.EXE", "C:\\Original.scr"); state.SetValue("ScreenSaveActive", "0"); state.SetValue("ScreenSaveTimeOut", "600");
                    }
                    var engine = new Setup.InstallEngine(root, registry, (_, __) => { }, () => File.OpenRead(payload));
                    Directory.CreateDirectory(engine.LegacyShortcutFolder);
                    Setup.Shortcut.Create(Path.Combine(engine.LegacyShortcutFolder, "Screensaver Manager.lnk"), Path.Combine(legacy, "ScreensaverApp.Manager.exe"), "");
                    Setup.Shortcut.Create(Path.Combine(engine.LegacyShortcutFolder, "Uninstall ScreensaverApp.lnk"), Path.Combine(root, "Setup.exe"), "/uninstall");
                    File.WriteAllText(Path.Combine(engine.LegacyShortcutFolder, "Personal note.txt"), "keep unrelated files");
                    string saver = engine.Install(null, true, 420);
                    ManagerSnapshot snapshot = new ManagerService(root, registry, (_, __) => { }).Load();
                    Check(File.ReadAllText(snapshot.VideoPath) == "legacy custom video", "upgrade imports an earlier installation's selected video");
                    Check(!Directory.Exists(legacy) && snapshot.SaverPath == saver, "upgrade selects new runtime and removes obsolete release");
                    Check(File.Exists(Path.Combine(engine.ShortcutFolder, "CineSaver Manager.lnk")) &&
                        !File.Exists(Path.Combine(engine.LegacyShortcutFolder, "Screensaver Manager.lnk")) &&
                        !File.Exists(Path.Combine(engine.LegacyShortcutFolder, "Uninstall ScreensaverApp.lnk")) &&
                        File.Exists(Path.Combine(engine.LegacyShortcutFolder, "Personal note.txt")), "rename replaces old owned shortcuts and preserves unrelated files");
                    engine.Uninstall();
                    using (RegistryKey desktop = registry.OpenSubKey("Control Panel\\Desktop"))
                        Check(desktop.GetValue("SCRNSAVE.EXE").ToString() == "C:\\Original.scr", "upgrade/uninstall preserves the original pre-install settings backup");
                }
                finally
                {
                    if (Directory.Exists(root)) Setup.InstallEngine.DeleteOwnedDirectory(root, parent);
                    Registry.CurrentUser.DeleteSubKeyTree(key, false);
                }
            }
        }
        private static int WhiteTextPixels(Forms.Control button)
        {
            int count = 0;
            using (var bitmap = new Bitmap(button.Width, button.Height))
            {
                button.DrawToBitmap(bitmap, button.ClientRectangle);
                for (int y = 6; y < bitmap.Height - 6; y++)
                    for (int x = 9; x < bitmap.Width - 9; x++)
                    { Color pixel = bitmap.GetPixel(x, y); if (pixel.R > 220 && pixel.G > 220 && pixel.B > 220) count++; }
            }
            return count;
        }
        private static void TestBranding(string stage)
        {
            foreach (string relative in new[] { "app\\CineSaver.scr", "app\\CineSaver.Manager.exe", "setup\\CineSaver-Setup.exe" })
            {
                var handles = new IntPtr[1];
                try
                {
                    Check(ExtractIconEx(Path.Combine(stage, relative), 0, handles, null, 1) == 1 && handles[0] != IntPtr.Zero,
                        relative + " has a native executable icon");
                    using (var icon = Icon.FromHandle(handles[0]))
                    using (var bitmap = icon.ToBitmap())
                    {
                        using (var expectedIcon = AppBranding.CreateIcon())
                        using (var expected = expectedIcon.ToBitmap())
                        {
                            int matched = 0, checkedPixels = 0;
                            for (int y = 2; y < bitmap.Height - 2; y += 2)
                                for (int x = 2; x < bitmap.Width - 2; x += 2)
                                {
                                    Color pixel = bitmap.GetPixel(x, y), target = expected.GetPixel(x, y);
                                    if (target.A < 240) continue;
                                    checkedPixels++;
                                    if (Math.Abs(pixel.R - target.R) + Math.Abs(pixel.G - target.G) + Math.Abs(pixel.B - target.B) < 24) matched++;
                                }
                            Check(checkedPixels > 20 && matched >= checkedPixels * 9 / 10, relative + " displays the supplied artwork");
                        }
                    }
                }
                finally { if (handles[0] != IntPtr.Zero) DestroyIcon(handles[0]); }
            }
            using (Icon icon = AppBranding.CreateIcon()) Check(icon.Width == 32 && icon.Height == 32, "window icon loads from the embedded asset");
        }
        private static T FindControl<T>(Forms.Control parent, Func<T, bool> predicate) where T : Forms.Control
        {
            foreach (Forms.Control child in parent.Controls)
            {
                if (child is T match && predicate(match)) return match;
                T result = FindControl<T>(child, predicate); if (result != null) return result;
            }
            return null;
        }
        private static void TestFullscreen(string saver)
        {
            string name = "ScreensaverTest-" + Guid.NewGuid().ToString("N");
            IntPtr desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x000F01FF, IntPtr.Zero);
            if (desktop == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var startup = new Startup { Size = (uint)Marshal.SizeOf(typeof(Startup)), Desktop = "WinSta0\\" + name };
                var command = new System.Text.StringBuilder("\"" + saver + "\" /s");
                if (!CreateProcess(saver, command, IntPtr.Zero, IntPtr.Zero, false, 0x08000000, IntPtr.Zero,
                    Path.GetDirectoryName(saver), ref startup, out ProcessInfo info))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    using (Process process = Process.GetProcessById((int)info.Id))
                    {
                        try
                        {
                            Pump(3000); Check(!process.HasExited, "fullscreen starts on isolated desktop");
                            process.Refresh(); TimeSpan cpuStart = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
                            long working = process.WorkingSet64, memory = process.PrivateMemorySize64;
                            for (int i = 0; i < 30; i++)
                            {
                                Pump(1000);
                                if (process.HasExited) throw new Exception("Fullscreen exited unexpectedly, code=" + process.ExitCode);
                                process.Refresh(); working = Math.Max(working, process.WorkingSet64); memory = Math.Max(memory, process.PrivateMemorySize64);
                            }
                            Check(true, "fullscreen survives two complete video loops");
                            double cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / watch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
                            Console.WriteLine("MEASURED isolated fullscreen: CPU=" + cpu.ToString("F2") + "% total; peak working=" + working / 1048576 + " MiB; peak private=" + memory / 1048576 + " MiB");
                            IntPtr window = IntPtr.Zero;
                            EnumProc callback = (hwnd, _) => { GetWindowThreadProcessId(hwnd, out uint pid);
                                var caption = new System.Text.StringBuilder(80); GetWindowText(hwnd, caption, 80);
                                if (pid == info.Id && caption.ToString() == "CineSaver") window = hwnd; return true; };
                            EnumDesktopWindows(desktop, callback, IntPtr.Zero); GC.KeepAlive(callback);
                            Check(window != IntPtr.Zero, "fullscreen native window exists on the test desktop");
                            var exitWatch = Stopwatch.StartNew();
                            PostMessage(window, 0x0100, new IntPtr(0x1B), IntPtr.Zero);
                            while (!process.HasExited && exitWatch.ElapsedMilliseconds < 3500) Pump(20);
                            bool exited = process.HasExited;
                            GetExitCodeProcess(info.Process, out uint code);
                            Check(exited && code == 0, "Escape exits fullscreen (" + exitWatch.ElapsedMilliseconds + "ms, code=" + code + ")");
                        }
                        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } }
                    }
                }
                finally { CloseHandle(info.Thread); CloseHandle(info.Process); }
            }
            finally { CloseDesktop(desktop); }
        }
        private static void Pump(int milliseconds)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds) { Forms.Application.DoEvents(); Thread.Sleep(10); }
        }
        private static void ExpectExit(Process process, int timeout, string name)
        {
            var watch = Stopwatch.StartNew();
            while (!process.HasExited && watch.ElapsedMilliseconds < timeout) Pump(20);
            if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); throw new Exception("FAIL: " + name + " timed out"); }
            Check(process.ExitCode == 0, name + " (" + watch.ElapsedMilliseconds + "ms, exit=" + process.ExitCode + ")");
        }
        private static void Capture(Forms.Form host, string path)
        {
            using (var bitmap = new Bitmap(host.Width, host.Height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                IntPtr dc = graphics.GetHdc();
                try { PrintWindow(host.Handle, dc, 2); } finally { graphics.ReleaseHdc(dc); }
                bitmap.Save(path, ImageFormat.Png);
            }
        }
        private static void TestPreview(string saver)
        {
            Check(File.Exists(saver), "extracted .scr exists");
            string directory = Path.GetDirectoryName(saver), video = Path.Combine(directory, "screensaver.mp4");
            string results = Path.Combine(directory, "test-results"); Directory.CreateDirectory(results);
            using (var host = new Forms.Form { Text = "Screensaver contained playback test", ClientSize = new Size(640, 400), BackColor = Color.Black,
                StartPosition = Forms.FormStartPosition.Manual, Location = new Point(50, 50), ShowInTaskbar = false })
            {
                host.Show(); Pump(100);
                using (var process = StartSaver(saver, "/p " + host.Handle.ToInt64()))
                {
                    try
                    {
                        Pump(3000); Check(!process.HasExited, "preview starts in compatibility mode");
                        Capture(host, Path.Combine(results, "preview-3s.png"));
                        process.Refresh(); TimeSpan startCpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
                        long peakWorking = process.WorkingSet64, peakPrivate = process.PrivateMemorySize64;
                        for (int i = 0; i < 30; i++)
                        {
                            Pump(1000); Check(!process.HasExited, "playback alive at " + (i + 4) + "s" + (process.HasExited ? "; exit=" + process.ExitCode : ""));
                            process.Refresh(); peakWorking = Math.Max(peakWorking, process.WorkingSet64); peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
                            if (i == 10) host.ClientSize = new Size(800, 500);
                            if (i == 20) host.ClientSize = new Size(640, 400);
                            if (i == 14) Capture(host, Path.Combine(results, "preview-after-loop.png"));
                        }
                        Capture(host, Path.Combine(results, "preview-33s.png"));
                        double cpu = (process.TotalProcessorTime - startCpu).TotalMilliseconds / watch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
                        Console.WriteLine("MEASURED preview: CPU=" + cpu.ToString("F2") + "% of total CPU; peak working=" + peakWorking / 1048576 + " MiB; peak private=" + peakPrivate / 1048576 + " MiB");
                        IntPtr child = IntPtr.Zero;
                        EnumProc callback = (hwnd, _) => { GetWindowThreadProcessId(hwnd, out uint pid); if (pid == process.Id && child == IntPtr.Zero) child = hwnd; return true; };
                        EnumChildWindows(host.Handle, callback, IntPtr.Zero); GC.KeepAlive(callback);
                        Check(child != IntPtr.Zero, "preview embeds into supplied parent");
                        PostMessage(child, 0x0010, IntPtr.Zero, IntPtr.Zero);
                        ExpectExit(process, 3500, "normal close is prompt");
                    }
                    finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } }
                }
                using (var process = StartSaver(saver, "/p " + host.Handle.ToInt64()))
                {
                    Pump(1500); host.Close(); ExpectExit(process, 3500, "destroyed preview host exits");
                }
            }
            TestFailures(saver);
        }
        private static void TestFailures(string saver)
        {
            string video = Path.Combine(Path.GetDirectoryName(saver), "screensaver.mp4");
            using (var process = StartSaver(saver, "/p:0")) ExpectExit(process, 3000, "invalid preview parent exits");
            string backup = video + ".test-backup";
            File.Move(video, backup);
            try
            {
                using (var host = new Forms.Form { ShowInTaskbar = false })
                {
                    host.Show(); Pump(100); IntPtr hwnd = host.Handle;
                    using (var process = StartSaver(saver, "/p " + hwnd.ToInt64())) ExpectExit(process, 3500, "missing video exits");
                    File.WriteAllText(video, "invalid video data");
                    using (var process = StartSaver(saver, "/p " + hwnd.ToInt64())) ExpectExit(process, 18000, "corrupt video exits by playback/startup deadline");
                }
            }
            finally { if (File.Exists(video)) File.Delete(video); File.Move(backup, video); }
        }
    }
}
