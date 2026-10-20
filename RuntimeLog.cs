using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using ScreensaverApp.Configuration;

namespace ScreensaverApp
{
    // Logging never holds up input, VLC callbacks, or the watchdog.
    internal static class RuntimeLog
    {
        private static readonly ConcurrentQueue<string> Queue = new ConcurrentQueue<string>();
        private static readonly Timer FlushTimer = new Timer(_ => Flush(), null, 500, 500);
        private static int _flushing;
        internal static string DirectoryPath => Path.Combine(SaverSettings.DefaultRoot, "Logs");
        internal static void Write(string message)
        {
            if (Queue.Count < 128) Queue.Enqueue(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message);
        }
        internal static void Flush()
        {
            if (Interlocked.Exchange(ref _flushing, 1) != 0) return;
            try
            {
                if (Queue.IsEmpty) return;
                Directory.CreateDirectory(DirectoryPath);
                string path = Path.Combine(DirectoryPath, "saver.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                {
                    string previous = path + ".previous";
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(path, previous);
                }
                using (var writer = File.AppendText(path))
                    while (Queue.TryDequeue(out string line)) writer.WriteLine(line);
            }
            catch { }
            finally { Volatile.Write(ref _flushing, 0); }
        }
    }
}
