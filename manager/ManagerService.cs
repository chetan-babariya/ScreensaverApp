using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using ScreensaverApp.Configuration;

namespace ScreensaverApp.Management
{
    internal sealed class ManagerSnapshot
    {
        internal string SaverPath, VideoPath, Warning;
        internal bool Enabled, Selected;
        internal SaverPreferences Preferences;
    }

    internal sealed class ManagerService
    {
        internal string Root { get; }
        private readonly RegistryKey _registry;
        private readonly Action<uint, uint> _notify;
        private readonly string _shortcutFolder;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint value, IntPtr data, uint flags);
        internal ManagerService() : this(SaverSettings.DefaultRoot, Registry.CurrentUser,
            (action, value) => { if (!SystemParametersInfo(action, value, IntPtr.Zero, 3)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); },
            Environment.GetFolderPath(Environment.SpecialFolder.Programs)) { }
        internal ManagerService(string root, RegistryKey registry, Action<uint, uint> notify, string programsDirectory = null)
        { Root = Path.GetFullPath(root); _registry = registry; _notify = notify; _shortcutFolder = Path.Combine(programsDirectory ?? Path.Combine(Root, "StartMenu"), "CineSaver"); }

        internal ManagerSnapshot Load()
        {
            string marker = Path.Combine(Root, ".installation");
            if (!File.Exists(marker) || File.ReadAllText(marker) != SaverSettings.Marker) throw new IOException("Install CineSaver to use the manager.");
            string saver;
            using (RegistryKey state = _registry.OpenSubKey(SaverSettings.StateKey)) saver = state?.GetValue("InstalledSaver", "").ToString();
            string prefix = Path.Combine(Root, "Releases") + Path.DirectorySeparatorChar;
            if (string.IsNullOrEmpty(saver) || !Path.GetFullPath(saver).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(saver))
                throw new IOException("The installed screensaver could not be found. Run Setup to repair the installation.");
            using (RegistryKey desktop = _registry.OpenSubKey("Control Panel\\Desktop"))
            {
                var preferences = SaverSettings.Load(Root, out string warning);
                bool selected = string.Equals(desktop?.GetValue("SCRNSAVE.EXE", "").ToString(), saver, StringComparison.OrdinalIgnoreCase);
                if (selected && int.TryParse(desktop?.GetValue("ScreenSaveTimeOut", "300").ToString(), out int seconds) && seconds >= 60 && seconds <= 7200)
                    preferences.IdleSeconds = seconds;
                string video = SaverSettings.ResolveVideo(Root, Path.GetDirectoryName(saver), preferences);
                if (!File.Exists(video)) warning = "The saved video is missing. Choose another video or use the included video.";
                return new ManagerSnapshot { SaverPath = saver, Preferences = preferences, Warning = warning,
                    VideoPath = video, Selected = selected,
                    Enabled = selected && desktop?.GetValue("ScreenSaveActive", "0").ToString() == "1" };
            }
        }

        internal ManagerSnapshot Apply(string newVideo, bool useIncluded, bool compatibility, int idleSeconds, bool enabled, ClockOptions clock = null, string shortcutKey = null)
        {
            using (SaverSettings.Lock(Root))
            {
                ManagerSnapshot current = Load();
                var preferences = current.Preferences;
                string oldShortcut = preferences.ShortcutKey;
                if (useIncluded) { preferences.VideoFile = ""; preferences.VideoName = "Included video"; }
                else if (!string.IsNullOrEmpty(newVideo)) SaverSettings.ImportVideo(Root, newVideo, preferences);
                preferences.Compatibility = compatibility; preferences.IdleSeconds = idleSeconds;
                if (clock != null) preferences.Clock = ClockOptions.Decode(clock.Encode());
                if (shortcutKey != null) preferences.ShortcutKey = shortcutKey;
                if (!File.Exists(SaverSettings.ResolveVideo(Root, Path.GetDirectoryName(current.SaverPath), preferences)))
                    throw new IOException("The selected video is missing. Choose another video or use the included video.");
                // Publish the complete video file and XML before selecting/enabling the saver.
                QuickLaunch.Update(_shortcutFolder, current.SaverPath, preferences.ShortcutKey);
                try { SaverSettings.Save(Root, preferences); }
                catch { QuickLaunch.Update(_shortcutFolder, current.SaverPath, oldShortcut); throw; }
                using (RegistryKey desktop = _registry.OpenSubKey("Control Panel\\Desktop", true))
                {
                    if (enabled)
                    {
                        desktop.SetValue("SCRNSAVE.EXE", current.SaverPath);
                        desktop.SetValue("ScreenSaveTimeOut", idleSeconds.ToString()); desktop.SetValue("ScreenSaveActive", "1");
                        _notify(0x000F, (uint)idleSeconds); _notify(0x0011, 1);
                    }
                    else if (current.Selected)
                    {
                        desktop.SetValue("ScreenSaveTimeOut", idleSeconds.ToString()); desktop.SetValue("ScreenSaveActive", "0");
                        _notify(0x000F, (uint)idleSeconds); _notify(0x0011, 0);
                    }
                    // Editing our video while another saver is selected must not disable that saver.
                }
                SaverSettings.PruneVideos(Root, preferences);
                return Load();
            }
        }

        internal string ReadDiagnostics()
        {
            string path = Path.Combine(Root, "Logs", "saver.log");
            if (!File.Exists(path)) return "No diagnostics yet. Run a preview to create playback logs.";
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > 65536) stream.Seek(-65536, SeekOrigin.End);
                using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }
    }
}
