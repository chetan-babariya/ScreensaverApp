using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;

namespace ScreensaverApp
{
    // Native calls are serialized on this worker. Callbacks only queue work/update timestamps.
    internal sealed class PlaybackSession
    {
        private readonly BlockingCollection<Action> _commands = new BlockingCollection<Action>();
        private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Action<string> _fail;
        private readonly Action _videoCreated;
        private readonly LibVLC _libVlc;
        private readonly string _path;
        private readonly IntPtr _hwnd;
        private MediaPlayer _player;
        private Media _media;
        private long _lastProgress, _lastMediaTime = -1;
        private int _stopping, _loopQueued;
        private string _aspect;
        private readonly string _snapshot;
        private int _snapshotQueued;
        internal PlaybackSession(LibVLC libVlc, string path, IntPtr hwnd, string aspect,
            Action<string> fail, Action videoCreated, string snapshot = null)
        {
            _libVlc = libVlc; _path = path; _hwnd = hwnd; _aspect = aspect;
            _fail = fail; _videoCreated = videoCreated;
            _snapshot = snapshot;
            new Thread(Run) { IsBackground = true, Name = "Screensaver playback" }.Start();
        }
        internal bool IsStalled => Volatile.Read(ref _stopping) == 0 &&
            SafetyPolicy.Expired(_clock.ElapsedMilliseconds, Interlocked.Read(ref _lastProgress), SafetyPolicy.PlaybackTimeoutMs);
        private void Run()
        {
            try
            {
                if (Volatile.Read(ref _stopping) != 0) return;
                _player = new MediaPlayer(_libVlc) { Hwnd = _hwnd, EnableKeyInput = false, EnableMouseInput = false };
                _media = new Media(_libVlc, new Uri(_path));
                _media.AddOption(":input-repeat=65535");
                _player.TimeChanged += (_, e) =>
                {
                    long previous = Interlocked.Exchange(ref _lastMediaTime, e.Time);
                    if (previous != e.Time) Interlocked.Exchange(ref _lastProgress, _clock.ElapsedMilliseconds);
                    // Use the video's timestamp so slow player startup cannot capture
                    // the opening black frame. Short clips get a bounded fallback.
                    if (_snapshot != null && (e.Time >= 1800 || (_clock.ElapsedMilliseconds >= 6000 && e.Time > 0)) && Interlocked.Exchange(ref _snapshotQueued, 1) == 0)
                        Queue(() =>
                        {
                            bool captured = _player.TakeSnapshot(0, _snapshot, 640, 0);
                            _fail(captured ? "Thumbnail captured" : "Thumbnail capture failed");
                        });
                };
                _player.EncounteredError += (_, __) => _fail("Video playback error");
                _player.Vout += (_, e) => { if (e.Count > 0) { Queue(ApplyCrop); _videoCreated(); } };
                _player.EndReached += (_, __) =>
                {
                    if (Interlocked.Exchange(ref _loopQueued, 1) == 0)
                        Queue(() =>
                        {
                            Interlocked.Exchange(ref _loopQueued, 0);
                            if (!_player.Play(_media)) _fail("Video loop failed");
                        });
                };
                ApplyCrop();
                if (!_player.Play(_media)) { _fail("Video could not start"); return; }
                foreach (Action command in _commands.GetConsumingEnumerable())
                {
                    if (Volatile.Read(ref _stopping) != 0) break;
                    command();
                }
            }
            catch (Exception ex) { _fail("Playback: " + ex.Message); }
            finally
            {
                try { _player?.Stop(); _player?.Dispose(); _media?.Dispose(); }
                catch (Exception ex) { RuntimeLog.Write("Playback cleanup: " + ex.Message); }
                _completion.TrySetResult(true);
            }
        }
        private void Queue(Action command)
        {
            if (Volatile.Read(ref _stopping) != 0) return;
            try { _commands.Add(command); } catch (InvalidOperationException) { }
        }
        private void ApplyCrop()
        {
            if (_player != null && !string.IsNullOrEmpty(_aspect)) _player.CropGeometry = _aspect;
        }
        internal void Resize(string aspect) { _aspect = aspect; Queue(ApplyCrop); }
        internal Task StopAsync()
        {
            if (Interlocked.Exchange(ref _stopping, 1) == 0) _commands.CompleteAdding();
            return _completion.Task;
        }
    }
}
