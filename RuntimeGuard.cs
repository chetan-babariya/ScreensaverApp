using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Forms = System.Windows.Forms;

namespace ScreensaverApp
{
    internal static class SafetyPolicy
    {
        internal const int UiTimeoutMs = 8000, ExitTimeoutMs = 2000, PlaybackTimeoutMs = 15000;
        internal static bool Expired(long now, long since, int limit) => now - since >= limit;
        internal static bool Moved(int x, int y, int originX, int originY) =>
            Math.Abs((long)x - originX) > 8 || Math.Abs((long)y - originY) > 8;
    }
    // Independent STA loop: receives raw input even if VLC owns focus or WPF is blocked.
    // Never calls VLC, performs disk I/O, or waits on the dispatcher.
    internal sealed class RuntimeGuard : Forms.NativeWindow, IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RawDevice { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
        private readonly Action<string> _requestExit;
        private readonly bool _watchInput;
        private readonly Thread _thread;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _heartbeat;
        private long _exitAt = -1;
        private int _disposed, _originX, _originY;
        private bool _armed;
        private int _shortcutStarting;
        private readonly bool[] _launchLetters = new bool[26];
        private IntPtr _buffer;
        internal RuntimeGuard(bool watchInput, Action<string> requestExit, bool shortcutStarting = false)
        {
            _watchInput = watchInput; _requestExit = requestExit;
            _shortcutStarting = shortcutStarting ? 1 : 0;
            if (shortcutStarting) for (int key = 'A'; key <= 'Z'; key++) _launchLetters[key - 'A'] = GetAsyncKeyState(key) < 0;
            _thread = new Thread(Run) { IsBackground = true, Name = "Screensaver safety monitor" };
            _thread.SetApartmentState(ApartmentState.STA); _thread.Start();
        }
        internal void Pulse() => Interlocked.Exchange(ref _heartbeat, _clock.ElapsedMilliseconds);
        internal void ArmExit() => Interlocked.CompareExchange(ref _exitAt, _clock.ElapsedMilliseconds, -1);
        private static bool LaunchKeysDown()
        {
            if (GetAsyncKeyState(0x11) < 0 || GetAsyncKeyState(0x12) < 0) return true;
            for (int key = 'A'; key <= 'Z'; key++) if (GetAsyncKeyState(key) < 0) return true;
            return false;
        }
        internal void KeyboardInput(int key)
        {
            // Ignore only the launch chord until released. Escape always dismisses.
            if (Volatile.Read(ref _shortcutStarting) != 0 && _clock.ElapsedMilliseconds < 5000 &&
                (key == 0x11 || key == 0x12 || key == 0xA2 || key == 0xA3 || key == 0xA4 || key == 0xA5 ||
                    (key >= 'A' && key <= 'Z' && _launchLetters[key - 'A'])) && LaunchKeysDown()) return;
            _requestExit("Keyboard input");
        }
        private void Run()
        {
            try
            {
                CreateHandle(new Forms.CreateParams { Caption = "Screensaver input monitor", Parent = new IntPtr(-3) });
                _buffer = Marshal.AllocHGlobal(64);
                if (_watchInput)
                {
                    var devices = new[]
                    {
                        new RawDevice { Page = 1, Usage = 2, Flags = 0x100, Target = Handle },
                        new RawDevice { Page = 1, Usage = 6, Flags = 0x100, Target = Handle }
                    };
                    if (!RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf(typeof(RawDevice))))
                    { _requestExit("Raw input registration failed"); ArmExit(); }
                }
                using (var timer = new Forms.Timer { Interval = 50 })
                {
                    timer.Tick += (_, __) => Tick(); timer.Start(); Forms.Application.Run();
                }
            }
            catch (Exception ex)
            {
                RuntimeLog.Write("Safety monitor failed: " + ex.Message);
                TerminateProcess(GetCurrentProcess(), 2);
            }
            finally
            {
                if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
                if (Handle != IntPtr.Zero) DestroyHandle();
            }
        }
        private void Tick()
        {
            if (Volatile.Read(ref _disposed) != 0) { Forms.Application.ExitThread(); return; }
            long now = _clock.ElapsedMilliseconds, exitAt = Interlocked.Read(ref _exitAt);
            if (Volatile.Read(ref _shortcutStarting) != 0 && (!LaunchKeysDown() || now >= 5000)) Interlocked.Exchange(ref _shortcutStarting, 0);
            if (exitAt >= 0 && SafetyPolicy.Expired(now, exitAt, SafetyPolicy.ExitTimeoutMs))
            { TerminateProcess(GetCurrentProcess(), 2); return; }
            if (exitAt < 0 && SafetyPolicy.Expired(now, Interlocked.Read(ref _heartbeat), SafetyPolicy.UiTimeoutMs))
            { TerminateProcess(GetCurrentProcess(), 3); return; }
            if (!_watchInput || exitAt >= 0 || !GetCursorPos(out Point cursor)) return;
            if (!_armed)
            { _originX = cursor.X; _originY = cursor.Y; _armed = now >= 600; }
            else if (SafetyPolicy.Moved(cursor.X, cursor.Y, _originX, _originY)) _requestExit("Mouse movement");
        }
        protected override void WndProc(ref Forms.Message message)
        {
            if (_watchInput && message.Msg == 0x00FF && _buffer != IntPtr.Zero)
            {
                uint size = 64, header = (uint)(IntPtr.Size == 8 ? 24 : 16);
                uint read = GetRawInputData(message.LParam, 0x10000003, _buffer, ref size, header);
                if (read != uint.MaxValue && read >= header + 8)
                {
                    int type = Marshal.ReadInt32(_buffer);
                    if (type == 1 && (Marshal.ReadInt16(_buffer, (int)header + 2) & 1) == 0)
                        KeyboardInput((ushort)Marshal.ReadInt16(_buffer, (int)header + 6));
                    else if (type == 0 && _armed && Marshal.ReadInt16(_buffer, (int)header + 4) != 0)
                        _requestExit("Mouse button or wheel");
                }
            }
            base.WndProc(ref message);
        }
        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
    }
}
