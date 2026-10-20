using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using ScreensaverApp.Configuration;
using ScreensaverApp.Management;
using Forms = System.Windows.Forms;

namespace ScreensaverApp.Tests
{
    internal static partial class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
        [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
        [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
        private static void TestCustomization(string payload)
        {
            string parent = Path.GetDirectoryName(payload), id = Guid.NewGuid().ToString("N");
            string root = Path.Combine(parent, "customization-test-" + id), key = "Software\\ScreensaverAppSmokeTests\\" + id;
            string results = Path.Combine(parent, "customization-results"); Directory.CreateDirectory(results);
            var noon = new DateTime(2026, 12, 31, 13, 5, 9);
            var clock = new ClockOptions { Style = ClockStyle.Neon, Position = ClockPosition.BottomRight, Size = 2, Color = 2 };
            Check(clock.TimeText(noon) == noon.ToString("HH:mm:ss"), "clock uses supplied local time in 24-hour format");
            clock.Use24Hour = false; clock.Seconds = false;
            Check(clock.TimeText(noon) == noon.ToString("hh:mm tt") && clock.TimeText(noon.Date) == noon.Date.ToString("hh:mm tt"), "12-hour formatting handles noon and midnight without seconds");
            Check(ClockOptions.Decode(clock.Encode()).Encode() == clock.Encode(), "clock options round trip");
            foreach (string bad in new[] { "-1,0,0,0,1,1,1", "9,0,0,0,1,1,1", "1,0,99,0,1,1,1", "1,0,1,0,1,1,1,x" })
            { bool rejected = false; try { ClockOptions.Decode(bad); } catch (FormatException) { rejected = true; } Check(rejected, "reject malformed clock: " + bad); }
            foreach (ClockStyle style in new[] { ClockStyle.Minimal, ClockStyle.Neon, ClockStyle.Analog, ClockStyle.Flip })
            {
                clock.Style = style;
                using (var bitmap = ClockRenderer.Render(ClockRenderer.DesignSize(clock), clock, noon))
                {
                    bitmap.Save(Path.Combine(results, style + ".png")); int ink = 0;
                    for (int y = 0; y < bitmap.Height; y += 2) for (int x = 0; x < bitmap.Width; x += 2)
                    { Color p = bitmap.GetPixel(x, y); if (p.A > 150 && (p.R > 120 || p.G > 120 || p.B > 120)) ink++; }
                    Check(ink > 150, style + " renders visible clock artwork");
                }
                foreach (ClockPosition position in Enum.GetValues(typeof(ClockPosition)))
                {
                    clock.Position = position;
                    foreach (Size size in new[] { new Size(48, 32), new Size(320, 180), new Size(1920, 1080) })
                        Check(new Rectangle(Point.Empty, size).Contains(ClockRenderer.Bounds(size, clock)), style + " fits " + position + " at " + size);
                }
            }
            using (RegistryKey registry = Registry.CurrentUser.CreateSubKey(key))
            {
                try
                {
                    using (var desktop = registry.CreateSubKey("Control Panel\\Desktop"))
                    { desktop.SetValue("SCRNSAVE.EXE", "C:\\Before.scr"); desktop.SetValue("ScreenSaveActive", "0"); desktop.SetValue("ScreenSaveTimeOut", "600"); }
                    var engine = new Setup.InstallEngine(root, registry, (_, __) => { }, () => File.OpenRead(payload));
                    string saver = engine.Install(null, true, 300);
                    var service = new ManagerService(root, registry, (_, __) => { });
                    Check(service.Load().Preferences.Clock.Style == ClockStyle.Off && service.Load().Preferences.ShortcutKey == "", "upgrade-safe defaults leave clock and hotkey off");
                    string settings = Path.Combine(root, "settings.xml");
                    File.WriteAllText(settings, "<screensaver compatibility=\"true\" idleSeconds=\"300\" clock=\"broken\" />");
                    var fallback = SaverSettings.Load(root, out string warning);
                    Check(warning != null && fallback.Clock.Style == ClockStyle.Off, "invalid optional clock falls back safely");
                    clock = new ClockOptions { Style = ClockStyle.Neon, Position = ClockPosition.TopRight, Color = 2, Size = 2, Use24Hour = false };
                    service.Apply(null, false, true, 300, false, clock, "S");
                    Check(service.Load().Preferences.Clock.Encode() == clock.Encode() && service.Load().Preferences.ShortcutKey == "S", "manager persists complete clock and shortcut preferences");
                    string linkPath = Path.Combine(engine.ShortcutFolder, QuickLaunch.FileName);
                    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); dynamic link = null;
                    try
                    {
                        link = shell.CreateShortcut(linkPath);
                        string hotkey = ((string)link.Hotkey).ToUpperInvariant();
                        Check((hotkey == "CTRL+ALT+S" || hotkey == "ALT+CTRL+S") && (string)link.Arguments == "/start" &&
                            (string)link.TargetPath == Path.Combine(Path.GetDirectoryName(saver), SaverSettings.ManagerFile), "Windows shortcut stores selected hotkey and executable launcher");
                    }
                    finally { if (link != null) Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
                    saver = engine.Install(null, true, 300);
                    Check(service.Load().Preferences.Clock.Encode() == clock.Encode() && service.Load().Preferences.ShortcutKey == "S", "update preserves custom clock and shortcut");
                    using (var form = new ManagerWindow(service))
                    {
                        form.Show(); Pump(400);
                        FindControl<Forms.Button>(form, c => c.Text == "Customization").PerformClick(); Pump(100);
                        FindControl<Forms.ComboBox>(form, c => c.Name == "ClockStyle").SelectedIndex = (int)ClockStyle.Analog;
                        FindControl<Forms.ComboBox>(form, c => c.Name == "ClockPosition").SelectedIndex = (int)ClockPosition.Center;
                        Pump(100);
                        var candidate = form.PreviewCandidate();
                        Check(candidate.Preferences.Clock.Style == ClockStyle.Analog && candidate.Preferences.Clock.Position == ClockPosition.Center &&
                            service.Load().Preferences.Clock.Style == ClockStyle.Neon, "preview uses unsaved clock preferences without changing saved settings");
                        Check(PreviewSupport.Arguments(new IntPtr(123), candidate).Contains(" /clock " + candidate.Preferences.Clock.Encode()), "draft clock travels only with its preview process");
                        Capture(form, Path.Combine(results, "manager-customization.png"));
                        form.Size = form.MinimumSize; Pump(100); Capture(form, Path.Combine(results, "manager-customization-minimum.png"));
                        var apply = FindControl<Forms.Button>(form, c => c.Name == "ApplyCustomization");
                        Check(apply.Visible && apply.Parent.ClientRectangle.Contains(apply.Bounds), "customization actions fit at minimum window size");
                        FindControl<Forms.ComboBox>(form, c => c.Name == "LaunchShortcut").SelectedIndex = 'Q' - 'A' + 1;
                        apply.PerformClick();
                        var saving = Stopwatch.StartNew(); while (!form.Enabled && saving.ElapsedMilliseconds < 10000) Pump(50);
                        var saved = service.Load().Preferences;
                        Check(form.Enabled && saved.Clock.Style == ClockStyle.Analog && saved.Clock.Position == ClockPosition.Center && saved.ShortcutKey == "Q",
                            "Customization Apply saves the actual controls through the background service");
                        FindControl<Forms.ComboBox>(form, c => c.Name == "ClockStyle").SelectedIndex = (int)ClockStyle.Flip;
                        FindControl<Forms.Button>(form, c => c.Text == "Discard changes" && c.Visible).PerformClick();
                        Check(form.PreviewCandidate().Preferences.Clock.Style == ClockStyle.Analog, "discard restores saved customization");
                        form.Close(); Pump(700);
                    }
                    TestNativeClocks(saver, service.Load(), results);
                    TestFullscreen(saver);
                    TestShortcutLauncher(saver);
                    service.Apply(null, false, true, 300, false, new ClockOptions(), "");
                    shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")); link = null;
                    try { link = shell.CreateShortcut(linkPath); Check((string)link.Hotkey == "", "disabling shortcut clears its Windows hotkey"); }
                    finally { if (link != null) Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
                    engine.Uninstall();
                    Check(!Directory.Exists(root) && !File.Exists(linkPath), "uninstall removes clock settings and quick launch shortcut");
                }
                finally
                {
                    if (Directory.Exists(root)) Setup.InstallEngine.DeleteOwnedDirectory(root, parent);
                    Registry.CurrentUser.DeleteSubKeyTree(key, false);
                }
            }
        }
        private static void TestNativeClocks(string saver, ManagerSnapshot snapshot, string results)
        {
            using (var host = new Forms.Form { Text = "CineSaver clock playback test", ClientSize = new Size(800, 500), BackColor = Color.Black, ShowInTaskbar = false })
            {
                host.Show(); Pump(100);
                foreach (ClockStyle style in new[] { ClockStyle.Minimal, ClockStyle.Neon, ClockStyle.Analog, ClockStyle.Flip })
                {
                    snapshot.Preferences.Clock.Style = style;
                    snapshot.Preferences.Compatibility = style != ClockStyle.Flip;
                    snapshot.Preferences.Clock.Position = ClockPosition.Center;
                    using (var process = StartSaver(saver, PreviewSupport.Arguments(host.Handle, snapshot)))
                    {
                        try
                        {
                            Pump(3000); Check(!process.HasExited, style + " clock plays above native video (" + (snapshot.Preferences.Compatibility ? "compatibility" : "automatic") + ")");
                            IntPtr overlay = IntPtr.Zero;
                            EnumProc callback = (hwnd, _) => { var caption = new StringBuilder(80); GetWindowText(hwnd, caption, 80); if (caption.ToString() == "CineSaver Clock") overlay = hwnd; return true; };
                            EnumChildWindows(host.Handle, callback, IntPtr.Zero); GC.KeepAlive(callback);
                            Check(overlay != IntPtr.Zero && GetParent(overlay) != host.Handle, style + " clock is contained in the saver child window");
                            Capture(host, Path.Combine(results, "playback-" + style + ".png"));
                            if (style == ClockStyle.Neon)
                            {
                                uint gdi = GetGuiResources(process.Handle, 0); Pump(8000);
                                Check(!process.HasExited && GetGuiResources(process.Handle, 0) <= gdi + 8, "clock ticks without accumulating GDI resources");
                            }
                            host.ClientSize = new Size(400, 260); Pump(800);
                            GetWindowRect(host.Handle, out WindowRect parent); GetWindowRect(overlay, out WindowRect child);
                            Check(child.Left >= parent.Left && child.Top >= parent.Top && child.Right <= parent.Right && child.Bottom <= parent.Bottom, style + " clock stays clipped after resizing");
                            process.Refresh(); Console.WriteLine("CLOCK " + style + ": working set=" + process.WorkingSet64 / 1048576 + " MiB; GDI=" + GetGuiResources(process.Handle, 0));
                            PostMessage(GetParent(overlay), 0x0010, IntPtr.Zero, IntPtr.Zero);
                            ExpectExit(process, 3500, style + " clock closes with video");
                            host.ClientSize = new Size(800, 500);
                        }
                        finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } }
                    }
                }
                snapshot.Preferences.Clock.Style = ClockStyle.Neon;
                snapshot.Preferences.Compatibility = true;
                using (var process = StartSaver(saver, PreviewSupport.Arguments(host.Handle, snapshot)))
                { Pump(1600); host.Close(); ExpectExit(process, 3500, "clock exits when preview parent disappears"); }
            }
        }
        private static void TestShortcutLauncher(string saver)
        {
            string manager = Path.Combine(Path.GetDirectoryName(saver), SaverSettings.ManagerFile);
            string name = "CineSaverShortcutTest-" + Guid.NewGuid().ToString("N");
            IntPtr desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x000F01FF, IntPtr.Zero);
            if (desktop == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Process child = null; ProcessInfo info = new ProcessInfo();
            try
            {
                var startup = new Startup { Size = (uint)Marshal.SizeOf(typeof(Startup)), Desktop = "WinSta0\\" + name };
                if (!CreateProcess(manager, new StringBuilder("\"" + manager + "\" /start"), IntPtr.Zero, IntPtr.Zero, false, 0x08000000,
                    IntPtr.Zero, Path.GetTempPath(), ref startup, out info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                Pump(3000); GetExitCodeProcess(info.Process, out uint launcherExit);
                Check(launcherExit == 0, "shortcut launcher exits without a resident manager");
                IntPtr saverWindow = IntPtr.Zero;
                EnumProc callback = (window, _) =>
                {
                    var title = new StringBuilder(80); GetWindowText(window, title, 80);
                    if (title.ToString() == "CineSaver")
                    { GetWindowThreadProcessId(window, out uint pid); child = Process.GetProcessById((int)pid); saverWindow = window; return false; }
                    return true;
                };
                EnumDesktopWindows(desktop, callback, IntPtr.Zero); GC.KeepAlive(callback);
                Check(child != null && !child.HasExited, "Start CineSaver launches fullscreen independently of idle activation");
                IntPtr childHandle = child.Handle;
                PostMessage(saverWindow, 0x0100, new IntPtr(0x1B), IntPtr.Zero);
                var watch = Stopwatch.StartNew();
                while (!child.HasExited && watch.ElapsedMilliseconds < 3500) Pump(20);
                GetExitCodeProcess(childHandle, out uint code);
                Check(child.HasExited && code == 0, "Escape dismisses shortcut-launched saver (" + watch.ElapsedMilliseconds + "ms)");
            }
            finally
            {
                if (child != null) { if (!child.HasExited) { child.Kill(); child.WaitForExit(3000); } child.Dispose(); }
                if (info.Process != IntPtr.Zero) { CloseHandle(info.Thread); CloseHandle(info.Process); }
                CloseDesktop(desktop);
            }
        }
    }
}
