using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using ScreensaverApp.Configuration;

namespace ScreensaverApp.Setup
{
    internal sealed class InstallEngine
    {
        private const string Marker = SaverSettings.Marker;
        private const string StateKey = SaverSettings.StateKey;
        // Keep the registration identity so upgrades replace the existing Apps entry.
        private const string UninstallKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\ScreensaverApp";
        internal string Root { get; }
        private readonly RegistryKey _registry;
        private readonly Action<uint, uint> _notify;
        private readonly Func<Stream> _payload;
        internal string ShortcutFolder { get; }
        internal string LegacyShortcutFolder { get; }
        internal InstallEngine() : this(SaverSettings.DefaultRoot,
            Registry.CurrentUser, (action, value) => SystemParametersInfo(action, value, IntPtr.Zero, 3), Payload,
            Environment.GetFolderPath(Environment.SpecialFolder.Programs)) { }
        internal InstallEngine(string root, RegistryKey registry, Action<uint, uint> notify, Func<Stream> payload, string programsDirectory = null)
        {
            Root = Path.GetFullPath(root); _registry = registry; _notify = notify; _payload = payload;
            string programs = programsDirectory ?? Path.Combine(Root, "StartMenu");
            ShortcutFolder = Path.Combine(programs, "CineSaver");
            LegacyShortcutFolder = Path.Combine(programs, "ScreensaverApp");
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, IntPtr value, uint flags);

        internal static Stream Payload() => Assembly.GetExecutingAssembly().GetManifestResourceStream("CineSaver.Payload.zip")
            ?? throw new InvalidOperationException("The installation payload is missing.");

        // Also used by the smoke tests. Extraction alone never modifies Windows settings.
        internal static void Extract(string destination, Stream payload)
        {
            string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(root);
            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains(":"))
                        throw new InvalidDataException("Unsafe path in installer archive.");
                    if (entry.Name.Length == 0) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream input = entry.Open())
                    using (Stream output = File.Create(target)) input.CopyTo(output);
                }
            }
            foreach (string file in new[] { "CineSaver.scr", "CineSaver.scr.config", "screensaver.mp4", "LibVLCSharp.dll", "CineSaver.Manager.exe", "CineSaver.Manager.exe.config",
                "libvlc\\win-x64\\libvlc.dll", "libvlc\\win-x64\\libvlccore.dll", "libvlc\\win-x64\\plugins\\video_output\\libwingdi_plugin.dll" })
                if (!File.Exists(Path.Combine(root, file))) throw new InvalidDataException("Incomplete package: " + file);
        }

        internal string Install(string video, bool compatibility, int seconds)
        {
            using (SaverSettings.Lock(Root)) return InstallCore(video, compatibility, seconds);
        }
        private string InstallCore(string video, bool compatibility, int seconds)
        {
            string releases = Path.Combine(Root, "Releases");
            // Short version folders leave room for VLC's nested resource filenames.
            string target;
            do { target = Path.Combine(releases, "1.6.0-" + Guid.NewGuid().ToString("N").Substring(0, 16)); }
            while (Directory.Exists(target));
            bool registered = false;
            Directory.CreateDirectory(Root);
            string marker = Path.Combine(Root, ".installation");
            if (File.Exists(marker) && File.ReadAllText(marker) != Marker)
                throw new IOException("The installation directory belongs to another application.");
            File.WriteAllText(marker, Marker);
            bool knownInstall, selectedBefore, enabledBefore;
            using (RegistryKey state = _registry.OpenSubKey(StateKey)) knownInstall = state?.GetValue("Saved") != null;
            using (RegistryKey desktop = _registry.OpenSubKey("Control Panel\\Desktop"))
            {
                selectedBefore = (desktop?.GetValue("SCRNSAVE.EXE", "").ToString() ?? "").StartsWith(releases + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                enabledBefore = desktop?.GetValue("ScreenSaveActive", "0").ToString() == "1";
            }
            try
            {
                using (Stream payload = _payload()) Extract(target, payload);
                var preferences = SaverSettings.Load(Root, out _, compatibility, seconds);
                if (!string.IsNullOrEmpty(video)) SaverSettings.ImportVideo(Root, video, preferences);
                else if (!File.Exists(Path.Combine(Root, "settings.xml")))
                {
                    // Preserve a custom video from the earlier installer before removing its release.
                    using (RegistryKey desktop = _registry.OpenSubKey("Control Panel\\Desktop"))
                    {
                        string oldSaver = desktop?.GetValue("SCRNSAVE.EXE", "").ToString() ?? "";
                        if (oldSaver.StartsWith(releases + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        {
                            string oldVideo = Path.Combine(Path.GetDirectoryName(oldSaver), "screensaver.mp4");
                            if (File.Exists(oldVideo)) SaverSettings.ImportVideo(Root, oldVideo, preferences);
                        }
                    }
                }
                preferences.Compatibility = compatibility; preferences.IdleSeconds = seconds;
                SaverSettings.Save(Root, preferences);
                if (!compatibility) File.WriteAllText(Path.Combine(target, "hardware-playback.enabled"), "Hardware playback requested by user.");
                string setup = Path.Combine(Root, SaverSettings.SetupFile);
                string self = Assembly.GetExecutingAssembly().Location;
                if (!string.Equals(Path.GetFullPath(self), setup, StringComparison.OrdinalIgnoreCase)) File.Copy(self, setup, true);
                using (RegistryKey desktop = _registry.OpenSubKey("Control Panel\\Desktop", true))
                using (RegistryKey state = _registry.CreateSubKey(StateKey))
                {
                    if (state.GetValue("Saved") == null)
                    {
                        foreach (string name in new[] { "SCRNSAVE.EXE", "ScreenSaveActive", "ScreenSaveTimeOut" })
                        {
                            object value = desktop.GetValue(name);
                            state.SetValue(name + ".Present", value != null ? 1 : 0);
                            if (value != null) state.SetValue(name, value.ToString());
                        }
                        state.SetValue("Saved", 1);
                    }
                    state.SetValue("InstalledSaver", Path.Combine(target, "CineSaver.scr"));
                    if (!knownInstall || selectedBefore)
                    {
                        bool activate = !knownInstall || enabledBefore;
                        desktop.SetValue("SCRNSAVE.EXE", Path.Combine(target, "CineSaver.scr"));
                        desktop.SetValue("ScreenSaveTimeOut", seconds.ToString());
                        desktop.SetValue("ScreenSaveActive", activate ? "1" : "0");
                        _notify(0x000F, (uint)seconds);
                        _notify(0x0011, activate ? 1u : 0u);
                    }
                    registered = true;
                }
                using (RegistryKey uninstall = _registry.CreateSubKey(UninstallKey))
                {
                    uninstall.SetValue("DisplayName", "CineSaver");
                    uninstall.SetValue("DisplayVersion", "1.6.0");
                    uninstall.SetValue("Publisher", "CineSaver");
                    uninstall.SetValue("InstallLocation", Root);
                    uninstall.SetValue("DisplayIcon", Path.Combine(target, SaverSettings.ManagerFile));
                    uninstall.SetValue("UninstallString", "\"" + setup + "\" /uninstall");
                    uninstall.SetValue("NoModify", 1); uninstall.SetValue("NoRepair", 1);
                }
                Directory.CreateDirectory(ShortcutFolder);
                Shortcut.Create(Path.Combine(ShortcutFolder, "CineSaver Manager.lnk"), Path.Combine(target, SaverSettings.ManagerFile), "");
                Shortcut.Create(Path.Combine(ShortcutFolder, "Uninstall CineSaver.lnk"), setup, "/uninstall");
                QuickLaunch.Update(ShortcutFolder, Path.Combine(target, SaverSettings.SaverFile), preferences.ShortcutKey);
                RemoveLegacyShortcuts();
                if (knownInstall)
                {
                    string legacySetup = Path.Combine(Root, "Setup.exe");
                    try { if (File.Exists(legacySetup)) File.Delete(legacySetup); }
                    catch (IOException) { } // May still be running during an upgrade.
                    catch (UnauthorizedAccessException) { }
                }
                SaverSettings.PruneVideos(Root, preferences);
                // Old releases are ours and are no longer selected. Locked files are left alone.
                foreach (string old in Directory.GetDirectories(releases))
                    if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase))
                        try { DeleteOwnedDirectory(old, releases); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return Path.Combine(target, "CineSaver.scr");
            }
            catch
            {
                // Never remove a release after it has become the selected saver.
                if (!registered && Directory.Exists(target)) DeleteOwnedDirectory(target, releases);
                throw;
            }
        }

        internal static void DeleteOwnedDirectory(string directory, string parent)
        {
            string absolute = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            string prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Directory is outside the installation.");
            // Do not follow junctions/symlinks while deleting application-owned files.
            if ((File.GetAttributes(absolute) & FileAttributes.ReparsePoint) != 0) { Directory.Delete(absolute); return; }
            foreach (string child in Directory.GetDirectories(absolute)) DeleteOwnedDirectory(child, parent);
            foreach (string file in Directory.GetFiles(absolute)) File.Delete(file);
            Directory.Delete(absolute);
        }

        internal void Uninstall()
        {
            using (SaverSettings.Lock(Root)) UninstallCore();
        }
        private void UninstallCore()
        {
            string marker = Path.Combine(Root, ".installation");
            if (!File.Exists(marker) || File.ReadAllText(marker) != Marker) throw new IOException("No managed installation found.");
            // A helper launched by Manager can inherit a release directory. Windows
            // keeps that directory locked even though the helper EXE lives in Temp.
            string working = Path.GetFullPath(Environment.CurrentDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string absoluteRoot = Root.TrimEnd(Path.DirectorySeparatorChar);
            if (working.Equals(absoluteRoot, StringComparison.OrdinalIgnoreCase) ||
                working.StartsWith(absoluteRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                Environment.CurrentDirectory = Path.GetTempPath();
            using (RegistryKey desktop = _registry.OpenSubKey("Control Panel\\Desktop", true))
            using (RegistryKey state = _registry.OpenSubKey(StateKey))
            {
                string selected = desktop.GetValue("SCRNSAVE.EXE", "").ToString();
                string releases = Path.Combine(Root, "Releases") + Path.DirectorySeparatorChar;
                if (selected.StartsWith(releases, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string name in new[] { "SCRNSAVE.EXE", "ScreenSaveActive", "ScreenSaveTimeOut" })
                    {
                        if (state?.GetValue(name + ".Present", 0).ToString() == "1") desktop.SetValue(name, state.GetValue(name, "").ToString());
                        else desktop.DeleteValue(name, false);
                    }
                    if (uint.TryParse(desktop.GetValue("ScreenSaveTimeOut", "300").ToString(), out uint seconds))
                        _notify(0x000F, seconds);
                    _notify(0x0011, desktop.GetValue("ScreenSaveActive", "0").ToString() == "1" ? 1u : 0u);
                }
            }
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (!new[] { "CineSaver", "CineSaver.scr", "CineSaver.Manager", "ScreensaverApp", "ScreensaverApp.scr", "ScreensaverApp.Manager", "CineSaver-Setup", "Setup" }.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase)) continue;
                    string executable = process.MainModule.FileName;
                    if (executable.StartsWith(Path.Combine(Root, "Releases") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        executable.Equals(Path.Combine(Root, SaverSettings.SetupFile), StringComparison.OrdinalIgnoreCase) ||
                        executable.Equals(Path.Combine(Root, "Setup.exe"), StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill();
                        if (!process.WaitForExit(3000)) throw new IOException("Close " + process.ProcessName + " and try uninstalling again.");
                    }
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
            }
            foreach (string name in new[] { "CineSaver Manager.lnk", "Uninstall CineSaver.lnk", QuickLaunch.FileName })
            {
                string shortcut = Path.Combine(ShortcutFolder, name);
                if (File.Exists(shortcut)) File.Delete(shortcut);
            }
            if (Directory.Exists(ShortcutFolder) && !Directory.EnumerateFileSystemEntries(ShortcutFolder).Any()) Directory.Delete(ShortcutFolder);
            RemoveLegacyShortcuts();
            DeleteInstallation();
            _registry.DeleteSubKeyTree(StateKey, false);
            _registry.DeleteSubKeyTree(UninstallKey, false);
        }
        private void DeleteInstallation()
        {
            string marker = Path.Combine(Root, ".installation");
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("The installation folder is a link. Close it and check its location before retrying.");
                    // Preserve the marker and registry backup until removal succeeds.
                    // Child deletion still validates boundaries and never follows links.
                    foreach (string directory in Directory.GetDirectories(Root)) DeleteOwnedDirectory(directory, Root);
                    foreach (string file in Directory.GetFiles(Root))
                        if (!file.Equals(marker, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
                    File.Delete(marker);
                    try { Directory.Delete(Root); }
                    catch
                    {
                        if (Directory.Exists(Root)) File.WriteAllText(marker, Marker);
                        throw;
                    }
                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (attempt >= 7)
                        throw new IOException("Some CineSaver files are still in use. Close windows or programs using this folder, then run uninstall again:\n" + Root +
                            "\nYour uninstall registration has been kept so you can retry.", ex);
                    Thread.Sleep(250);
                }
            }
        }
        private void RemoveLegacyShortcuts()
        {
            foreach (string name in new[] { "Screensaver Manager.lnk", "Uninstall ScreensaverApp.lnk" })
                Shortcut.RemoveOwned(Path.Combine(LegacyShortcutFolder, name), Root);
            if (Directory.Exists(LegacyShortcutFolder) && !Directory.EnumerateFileSystemEntries(LegacyShortcutFolder).Any()) Directory.Delete(LegacyShortcutFolder);
        }
    }

    internal static class Shortcut
    {
        internal static void Create(string path, string target, string arguments)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            dynamic link = null;
            try
            {
                link = shell.CreateShortcut(path);
                link.TargetPath = target; link.Arguments = arguments;
                link.IconLocation = target + ",0";
                link.WorkingDirectory = Path.GetTempPath(); link.Description = "CineSaver";
                link.Save();
            }
            finally
            {
                if (link != null) Marshal.FinalReleaseComObject(link);
                Marshal.FinalReleaseComObject(shell);
            }
        }
        internal static void RemoveOwned(string path, string root)
        {
            if (!File.Exists(path)) return;
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            dynamic link = null;
            try
            {
                link = shell.CreateShortcut(path);
                string target = (string)link.TargetPath;
                string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!string.IsNullOrEmpty(target) && Path.GetFullPath(target).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) File.Delete(path);
            }
            finally { if (link != null) Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
        }
    }

    internal sealed class SetupWindow : Form
    {
        private string _video;
        private bool _installing;
        private readonly Label _videoLabel = new ThemeLabel { AutoSize = false, Height = 42, Text = "Video: included screensaver.mp4", AutoEllipsis = true };
        private readonly CheckBox _compatibility = new CheckBox { Text = "Compatibility playback", Checked = true, AutoSize = true };
        private readonly NumericUpDown _minutes = new NumericUpDown { Minimum = 1, Maximum = 120, Value = 5, Width = 65 };
        private readonly Button _install = UiTheme.Button("Install / Update", true);
        private readonly Button _manager = UiTheme.Button("Open manager");
        private string _managerPath;
        private readonly Label _status = new ThemeLabel { Name = "InstallStatus", AutoSize = false, Height = 74, Text = "Installs for your Windows account. Administrator access is not needed." };
        internal SetupWindow()
        {
            UiTheme.Apply(this);
            Text = "CineSaver Setup 1.6.0"; ClientSize = new Size(900, 650);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            _manager.Enabled = false;
            _minutes.BackColor = UiTheme.Surface; _minutes.ForeColor = UiTheme.Text; _minutes.BorderStyle = BorderStyle.FixedSingle;
            _status.ForeColor = UiTheme.Muted;
            FormClosing += (_, e) => { if (_installing) e.Cancel = true; };
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(root);
            var side = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Sidebar, Margin = Padding.Empty, Padding = new Padding(28) };
            var logo = UiTheme.Logo(new Size(190, 190)); logo.Location = new Point(20, 64); side.Controls.Add(logo);
            side.Controls.Add(new ThemeLabel { Text = "CineSaver", Font = new Font("Segoe UI", 23, FontStyle.Bold), Location = new Point(23, 290), Size = new Size(190, 65) });
            side.Controls.Add(new ThemeLabel { Text = "Choose your video.\nLet it loop.", ForeColor = UiTheme.Cyan, Location = new Point(28, 400), Size = new Size(176, 68) });
            side.Controls.Add(new ThemeLabel { Text = "SETUP  /  1.6.0", Font = new Font("Segoe UI", 9), ForeColor = UiTheme.Muted, Dock = DockStyle.Bottom, Height = 30 }); root.Controls.Add(side, 0, 0);
            var layout = new TableLayoutPanel { Name = "SetupContent", Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Padding = new Padding(28, 28, 28, 20), Margin = Padding.Empty };
            foreach (int height in new[] { 96, 126, 154, 52 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); root.Controls.Add(layout, 1, 0);
            var heading = new Panel { Dock = DockStyle.Fill };
            heading.Controls.Add(new ThemeLabel { Text = "Your video. An endless loop.", ForeColor = UiTheme.Muted, Dock = DockStyle.Bottom, Height = 36 });
            heading.Controls.Add(new ThemeLabel { Text = "Install CineSaver", Font = new Font("Segoe UI", 22, FontStyle.Bold), Dock = DockStyle.Top, Height = 48 }); layout.Controls.Add(heading, 0, 0);
            var videoCard = new ThemeCard { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 0, 0, 16) };
            var videoGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            videoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); videoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
            videoGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var videoText = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            videoText.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); videoText.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            videoText.Controls.Add(new ThemeLabel { Text = "LOOPING VIDEO", ForeColor = UiTheme.Cyan, Font = new Font("Segoe UI", 9, FontStyle.Bold), Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
            _videoLabel.Name = "SetupVideoName"; _videoLabel.Dock = DockStyle.Fill; _videoLabel.Margin = Padding.Empty; _videoLabel.TextAlign = ContentAlignment.MiddleLeft;
            videoText.Controls.Add(_videoLabel, 0, 1); videoGrid.Controls.Add(videoText, 0, 0);
            var choose = UiTheme.Button("Choose video…"); choose.Anchor = AnchorStyles.Left;
            choose.Click += (_, __) =>
            {
                using (var dialog = new System.Windows.Forms.OpenFileDialog { Filter = SaverSettings.VideoFilter, CheckFileExists = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) { _video = dialog.FileName; _videoLabel.Text = "Video: " + _video; }
            };
            videoGrid.Controls.Add(choose, 1, 0); videoCard.Controls.Add(videoGrid); layout.Controls.Add(videoCard, 0, 1);
            var preferencesCard = new ThemeCard { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 0, 0, 16) };
            var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            options.Controls.Add(_compatibility, 0, 0);
            options.Controls.Add(new ThemeLabel { Text = "Recommended for older graphics drivers.", ForeColor = UiTheme.Muted, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9) }, 0, 1);
            var interval = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            interval.Controls.Add(new ThemeLabel { Text = "Start after", AutoSize = true, Padding = new Padding(0, 5, 5, 0) });
            interval.Controls.Add(_minutes); interval.Controls.Add(new ThemeLabel { Text = "minutes idle", AutoSize = true, Padding = new Padding(5, 5, 0, 0) });
            options.Controls.Add(interval, 0, 2); preferencesCard.Controls.Add(options); layout.Controls.Add(preferencesCard, 0, 2);
            using (RegistryKey desktop = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop"))
            {
                if (int.TryParse(desktop?.GetValue("ScreenSaveTimeOut", "300").ToString(), out int seconds))
                    _minutes.Value = Math.Max(1, Math.Min(120, seconds / 60));
                string selected = desktop?.GetValue("SCRNSAVE.EXE", "").ToString() ?? "";
                string managed = Path.Combine(SaverSettings.DefaultRoot, "Releases") + Path.DirectorySeparatorChar;
                if (selected.StartsWith(managed, StringComparison.OrdinalIgnoreCase))
                {
                    string directory = Path.GetDirectoryName(selected), video = Path.Combine(directory, "screensaver.mp4");
                    if (File.Exists(video)) _videoLabel.Text = "Video: current installed video";
                    _compatibility.Checked = !File.Exists(Path.Combine(directory, "hardware-playback.enabled"));
                    var preferences = SaverSettings.Load(SaverSettings.DefaultRoot, out _, _compatibility.Checked);
                    _compatibility.Checked = preferences.Compatibility;
                    if (File.Exists(Path.Combine(SaverSettings.DefaultRoot, "settings.xml")))
                    {
                        _minutes.Value = Math.Max(1, Math.Min(120, preferences.IdleSeconds / 60));
                        _videoLabel.Text = "Video: " + preferences.VideoName;
                    }
                    _managerPath = Path.Combine(directory, SaverSettings.ManagerFile);
                    _manager.Enabled = File.Exists(_managerPath);
                }
            }
            // Saved preferences also matter when this saver is disabled/not currently selected.
            if (File.Exists(Path.Combine(SaverSettings.DefaultRoot, "settings.xml")))
            {
                var preferences = SaverSettings.Load(SaverSettings.DefaultRoot, out _);
                _minutes.Value = Math.Max(1, Math.Min(120, preferences.IdleSeconds / 60));
                _compatibility.Checked = preferences.Compatibility;
                _videoLabel.Text = "Video: " + preferences.VideoName;
                using (RegistryKey state = Registry.CurrentUser.OpenSubKey(SaverSettings.StateKey))
                {
                    string saver = state?.GetValue("InstalledSaver", "").ToString() ?? "";
                    if (!string.IsNullOrEmpty(saver)) { _managerPath = Path.Combine(Path.GetDirectoryName(saver), SaverSettings.ManagerFile); _manager.Enabled = File.Exists(_managerPath); }
                }
            }
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            _install.Click += InstallClick; buttons.Controls.Add(_install);
            var settings = UiTheme.Button("Windows settings");
            settings.Click += (_, __) => Process.Start("rundll32.exe", "shell32.dll,Control_RunDLL desk.cpl,,1");
            _manager.Click += (_, __) => Process.Start(_managerPath);
            buttons.Controls.Add(settings); buttons.Controls.Add(_manager); layout.Controls.Add(buttons, 0, 3);
            _status.Dock = DockStyle.Fill; _status.Padding = new Padding(0, 10, 0, 0); layout.Controls.Add(_status, 0, 4);
            layout.Controls.Add(new ThemeLabel { Text = "CINESAVER  /  WINDOWS x64", ForeColor = UiTheme.Muted, Font = new Font("Segoe UI", 8), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight }, 0, 5);
        }
        private async void InstallClick(object sender, EventArgs e)
        {
            _installing = true;
            _install.Enabled = false;
            string video = _video; bool compatibility = _compatibility.Checked; int seconds = (int)_minutes.Value * 60;
            Enabled = false; _status.Text = "Installing…";
            try
            {
                string path = await Task.Run(() => new InstallEngine().Install(video, compatibility, seconds));
                _managerPath = Path.Combine(Path.GetDirectoryName(path), SaverSettings.ManagerFile); _manager.Enabled = true;
                bool active;
                using (RegistryKey desktop = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop"))
                    active = string.Equals(desktop?.GetValue("SCRNSAVE.EXE", "").ToString(), path, StringComparison.OrdinalIgnoreCase) && desktop?.GetValue("ScreenSaveActive", "0").ToString() == "1";
                _status.Text = (active ? "Installed. Starts after " + seconds / 60 + " minute(s)." : "Installed. Your Windows activation preference was preserved.") +
                    "\nOpen CineSaver Manager from the Start menu to change settings.";
            }
            catch (Exception ex) { _status.Text = "Installation failed: " + ex.Message; }
            finally { _installing = false; Enabled = true; _install.Enabled = true; }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                // Developer/package verification: only extracts files; no registry or live installation changes.
                if (args.Length == 2 && args[0] == "/extract")
                { using (Stream payload = InstallEngine.Payload()) InstallEngine.Extract(args[1], payload); return 0; }
                // External settings/preview processes must not inherit a release folder.
                Environment.CurrentDirectory = Path.GetTempPath();
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                if (args.Length == 1 && args[0] == "/uninstall")
                {
                    // Both the helper EXE and its working directory must be outside
                    // the installation, including when launched from Manager.
                    string helper = Path.Combine(Path.GetTempPath(), "CineSaver-Remove-" + Guid.NewGuid().ToString("N") + ".exe");
                    File.Copy(Assembly.GetExecutingAssembly().Location, helper);
                    Process.Start(new ProcessStartInfo(helper, "/remove " + Process.GetCurrentProcess().Id)
                    { WorkingDirectory = Path.GetTempPath(), UseShellExecute = false }); return 0;
                }
                if (args.Length == 2 && args[0] == "/remove")
                {
                    Environment.CurrentDirectory = Path.GetTempPath();
                    if (int.TryParse(args[1], out int pid))
                        try { using (Process previous = Process.GetProcessById(pid)) previous.WaitForExit(5000); } catch (ArgumentException) { }
                    if (MessageBox.Show("Remove CineSaver and restore your previous screensaver settings?", "Uninstall CineSaver",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    { new InstallEngine().Uninstall(); MessageBox.Show("CineSaver was removed."); }
                    return 0;
                }
                using (var mutex = new Mutex(true, "Local\\ScreensaverApp.Setup", out bool created))
                {
                    if (!created) { MessageBox.Show("CineSaver Setup is already open."); return 1; }
                    Application.Run(new SetupWindow());
                }
                return 0;
            }
            catch (Exception ex)
            {
                if (args.Length > 0 && args[0] == "/extract") return 1;
                MessageBox.Show(ex.Message, "CineSaver Setup", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1;
            }
        }
    }
}
