using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreensaverApp.Management
{
    internal static class PreviewSupport
    {
        internal static string Arguments(IntPtr parent, ManagerSnapshot snapshot, string output = null)
        {
            // Windows filenames cannot contain double quotes. All paths are quoted.
            return "/p " + parent.ToInt64() + " /video \"" + snapshot.VideoPath + "\" /playback " +
                (snapshot.Preferences.Compatibility ? "compatibility" : "auto") +
                " /clock " + snapshot.Preferences.Clock.Encode() +
                (output == null ? "" : " /snapshot \"" + output + "\"");
        }
        private sealed class HiddenHost : Form
        {
            protected override bool ShowWithoutActivation => true;
            internal HiddenHost()
            {
                ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual; Location = new Point(-16000, -16000);
                ClientSize = new Size(640, 360); Opacity = 0;
            }
        }
        // No persistent decoder or disk cache. The small still lives only in manager memory.
        internal static async Task<Bitmap> ThumbnailAsync(ManagerSnapshot snapshot, CancellationToken cancellation)
        {
            string output = Path.Combine(Path.GetTempPath(), "CineSaver-thumb-" + Guid.NewGuid().ToString("N") + ".png");
            Process process = null;
            using (var host = new HiddenHost())
            {
                try
                {
                    host.Show();
                    process = Process.Start(new ProcessStartInfo(snapshot.SaverPath, Arguments(host.Handle, snapshot, output))
                    { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() });
                    var clock = Stopwatch.StartNew();
                    while (!process.HasExited && clock.ElapsedMilliseconds < 12000)
                    { await Task.Delay(100, cancellation); }
                    cancellation.ThrowIfCancellationRequested();
                    if (process.HasExited && File.Exists(output))
                        using (var image = Image.FromFile(output)) return new Bitmap(image);
                    return null;
                }
                finally
                {
                    if (process != null)
                    {
                        try { if (!process.HasExited) { process.Kill(); await Task.Run(() => process.WaitForExit(3000)); } }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                        process.Dispose();
                    }
                    try { File.Delete(output); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }
    }
}
