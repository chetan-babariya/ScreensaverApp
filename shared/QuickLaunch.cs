using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ScreensaverApp.Configuration
{
    internal static class QuickLaunch
    {
        internal const string FileName = "Start CineSaver.lnk";
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint change, uint flags, string path, IntPtr unused);
        internal static void Update(string folder, string saver, string key)
        {
            if (!SaverSettings.ValidShortcut(key)) throw new ArgumentException("Invalid shortcut key.");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, FileName);
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            dynamic link = null;
            try
            {
                link = shell.CreateShortcut(path);
                // An EXE target avoids .scr file-association handlers changing arguments.
                link.TargetPath = Path.Combine(Path.GetDirectoryName(saver), SaverSettings.ManagerFile); link.Arguments = "/start";
                link.WorkingDirectory = Path.GetTempPath(); link.IconLocation = saver + ",0";
                link.Description = "Start CineSaver now";
                link.Hotkey = key.Length == 0 ? "" : "CTRL+ALT+" + key;
                link.Save();
                SHChangeNotify(0x00002000, 5, path, IntPtr.Zero);
            }
            finally
            {
                if (link != null) Marshal.FinalReleaseComObject(link);
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
