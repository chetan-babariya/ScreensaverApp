using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace ScreensaverApp.Configuration
{
    internal sealed class SaverPreferences
    {
        internal string VideoFile = "";
        internal string VideoName = "Included video";
        internal bool Compatibility = true;
        internal int IdleSeconds = 300;
        internal ClockOptions Clock = new ClockOptions();
        internal string ShortcutKey = ""; // Empty disables the Windows shortcut hotkey.
    }

    internal static class SaverSettings
    {
        // Stable identifiers keep earlier installations linked to their original
        // Windows-settings backup. Public product/file names use CineSaver.
        internal const string Marker = "ScreensaverApp.PerUser.Installation.v1";
        internal const string StateKey = "Software\\ScreensaverApp\\Installer";
        internal const string ManagerFile = "CineSaver.Manager.exe";
        internal const string SaverFile = "CineSaver.scr", SetupFile = "CineSaver-Setup.exe";
        internal static string DefaultRoot => PreferredRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        internal static string PreferredRoot(string localData)
        {
            string current = Path.Combine(localData, "CineSaver"), legacy = Path.Combine(localData, "ScreensaverApp");
            if (!IsInstallation(current) && IsInstallation(legacy)) return legacy;
            return current;
        }
        private static bool IsInstallation(string root)
        {
            try { string marker = Path.Combine(root, ".installation"); return File.Exists(marker) && File.ReadAllText(marker) == Marker; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        internal const string VideoFilter = "Video files|*.mp4;*.m4v;*.mkv;*.mov;*.avi;*.webm;*.wmv|MP4 video|*.mp4;*.m4v";

        internal static string FindRoot(string release)
        {
            var parent = Directory.GetParent(release);
            if (parent != null && parent.Name.Equals("Releases", StringComparison.OrdinalIgnoreCase) && parent.Parent != null)
            {
                string root = parent.Parent.FullName, marker = Path.Combine(root, ".installation");
                if (File.Exists(marker) && File.ReadAllText(marker) == Marker) return root;
            }
            return release; // Portable/development runs use their own directory.
        }

        internal static SaverPreferences Load(string root, out string warning, bool compatibility = true, int idleSeconds = 300)
        {
            warning = null;
            var result = new SaverPreferences { Compatibility = compatibility, IdleSeconds = idleSeconds };
            string path = Path.Combine(root, "settings.xml");
            if (!File.Exists(path)) return result;
            try
            {
                if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Settings file is too large.");
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                {
                    XElement element = XElement.Load(reader);
                    if (element.Name != "screensaver") throw new InvalidDataException("Invalid settings format.");
                    result.VideoFile = (string)element.Attribute("video") ?? "";
                    result.VideoName = (string)element.Attribute("videoName") ?? "Included video";
                    if (bool.TryParse((string)element.Attribute("compatibility"), out bool mode)) result.Compatibility = mode;
                    if (int.TryParse((string)element.Attribute("idleSeconds"), out int seconds) && seconds >= 60 && seconds <= 7200) result.IdleSeconds = seconds;
                    // Invalid optional customization must never discard a user's video selection.
                    try { result.Clock = ClockOptions.Decode((string)element.Attribute("clock")); }
                    catch (FormatException) { warning = "Invalid clock settings were reset."; }
                    string shortcut = (string)element.Attribute("shortcut") ?? "";
                    if (ValidShortcut(shortcut)) result.ShortcutKey = shortcut;
                    ResolveVideo(root, root, result); // Validate stored paths before using them.
                }
                return result;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is XmlException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                warning = "Settings could not be read; using the included video and default preferences. " + ex.Message;
                return new SaverPreferences { Compatibility = compatibility, IdleSeconds = idleSeconds };
            }
        }

        internal static string ResolveVideo(string root, string release, SaverPreferences preferences)
        {
            if (string.IsNullOrEmpty(preferences.VideoFile)) return Path.Combine(release, "screensaver.mp4");
            string prefix = Path.GetFullPath(Path.Combine(root, "Videos")) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(root, preferences.VideoFile));
            if (Path.IsPathRooted(preferences.VideoFile) || !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The selected video is outside the managed video folder.");
            return candidate;
        }

        internal static void Save(string root, SaverPreferences preferences)
        {
            ResolveVideo(root, root, preferences);
            if (preferences.IdleSeconds < 60 || preferences.IdleSeconds > 7200) throw new ArgumentOutOfRangeException(nameof(preferences.IdleSeconds));
            ClockOptions.Decode(preferences.Clock.Encode());
            if (!ValidShortcut(preferences.ShortcutKey)) throw new ArgumentException("Choose a shortcut letter from A to Z, or disable the shortcut.");
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "settings.xml"), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                new XElement("screensaver", new XAttribute("video", preferences.VideoFile ?? ""),
                    new XAttribute("videoName", preferences.VideoName ?? "Included video"),
                    new XAttribute("compatibility", preferences.Compatibility), new XAttribute("idleSeconds", preferences.IdleSeconds),
                    new XAttribute("clock", preferences.Clock.Encode()), new XAttribute("shortcut", preferences.ShortcutKey)).Save(temp);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        internal static void ImportVideo(string root, string source, SaverPreferences preferences)
        {
            string extension = Path.GetExtension(source).ToLowerInvariant();
            if (!new[] { ".mp4", ".m4v", ".mkv", ".mov", ".avi", ".webm", ".wmv" }.Contains(extension))
                throw new InvalidDataException("Choose an MP4, M4V, MKV, MOV, AVI, WebM, or WMV video.");
            if (!File.Exists(source) || new FileInfo(source).Length == 0) throw new IOException("The selected video is missing or empty.");
            Directory.CreateDirectory(Path.Combine(root, "Videos"));
            string relative = Path.Combine("Videos", Guid.NewGuid().ToString("N") + extension);
            string destination = Path.Combine(root, relative);
            try
            {
                File.Copy(source, destination);
                File.SetAttributes(destination, File.GetAttributes(destination) & ~FileAttributes.ReadOnly);
            }
            catch { if (File.Exists(destination)) File.Delete(destination); throw; }
            preferences.VideoFile = relative; preferences.VideoName = Path.GetFileName(source);
        }

        internal static void PruneVideos(string root, SaverPreferences preferences)
        {
            string folder = Path.Combine(root, "Videos");
            if (!Directory.Exists(folder)) return;
            string keep = string.IsNullOrEmpty(preferences.VideoFile) ? "" : ResolveVideo(root, root, preferences);
            foreach (string file in Directory.GetFiles(folder))
                if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _) && !file.Equals(keep, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        internal static bool ValidShortcut(string key) => key == "" || (key != null && key.Length == 1 && key[0] >= 'A' && key[0] <= 'Z');
        internal static IDisposable Lock(string root) => new SettingsLock(root);
        private sealed class SettingsLock : IDisposable
        {
            private readonly Mutex _mutex;
            internal SettingsLock(string root)
            {
                string name;
                using (var hash = SHA256.Create()) name = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant()))).Replace("-", "").Substring(0, 20);
                _mutex = new Mutex(false, "Local\\ScreensaverApp.Settings." + name);
                bool owns;
                try { owns = _mutex.WaitOne(5000); } catch (AbandonedMutexException) { owns = true; }
                if (!owns) { _mutex.Dispose(); throw new IOException("Screensaver settings are being updated. Try again shortly."); }
            }
            public void Dispose() { _mutex.ReleaseMutex(); _mutex.Dispose(); }
        }
    }
}
