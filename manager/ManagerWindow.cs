using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScreensaverApp.Configuration;

namespace ScreensaverApp.Management
{
    internal sealed partial class ManagerWindow : Form
    {
        private static readonly Color Ink = UiTheme.Text, Muted = UiTheme.Muted, Accent = UiTheme.Cyan;
        private readonly ManagerService _service;
        private readonly Label _video = new ThemeLabel { AutoEllipsis = true, Dock = DockStyle.Fill };
        private readonly CheckBox _enabled = new CheckBox { Text = "Enable this screensaver", AutoSize = true, Name = "Enabled", Padding = new Padding(0, 6, 0, 0) };
        private readonly ComboBox _mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, Name = "PlaybackMode" };
        private readonly NumericUpDown _minutes = new NumericUpDown { Minimum = 1, Maximum = 120, Width = 80, Name = "IdleMinutes" };
        private readonly Label _state = new ThemeLabel { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = Accent };
        private readonly Label _status = new ThemeLabel { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Muted, Padding = new Padding(0, 10, 0, 0) };
        private readonly Label _selection = new ThemeLabel { Text = "CURRENT VIDEO", ForeColor = Muted, AutoSize = true };
        private readonly Label _thumbStatus = new ThemeLabel { Text = "Loading thumbnail…", Dock = DockStyle.Bottom, Height = 26, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft };
        private readonly PictureBox _thumbnail = new PictureBox { Name = "VideoThumbnail", Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(17, 24, 39) };
        private readonly TextBox _logs = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9), BorderStyle = BorderStyle.None, BackColor = UiTheme.Surface, ForeColor = UiTheme.Text };
        private readonly Button _apply = MakeButton("Apply changes", true);
        private readonly Panel _settings = new Panel { Dock = DockStyle.Fill, Name = "ScreensaverPage" };
        private ManagerSnapshot _saved;
        private string _newVideo;
        private bool _included, _busy, _loading, _closing, _closed, _previewing;
        private CancellationTokenSource _thumbnailCancellation;
        private Task _thumbnailTask = Task.CompletedTask;
        private readonly List<Task> _thumbnailTasks = new List<Task>();

        internal ManagerWindow(ManagerService service)
        {
            _service = service;
            UiTheme.Apply(this);
            Text = "CineSaver Manager"; ClientSize = new Size(1040, 730); MinimumSize = new Size(990, 740);
            StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiTheme.Background; ForeColor = Ink;
            _mode.BackColor = _minutes.BackColor = UiTheme.Surface; _mode.ForeColor = _minutes.ForeColor = Ink;
            _minutes.BorderStyle = BorderStyle.FixedSingle;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);
            var side = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Sidebar, Margin = Padding.Empty, Padding = new Padding(18, 28, 18, 20) };
            root.Controls.Add(side, 0, 0);
            var logo = UiTheme.Logo(new Size(140, 140)); logo.Location = new Point(25, 24); side.Controls.Add(logo);
            var brand = new ThemeLabel { Text = "CineSaver", ForeColor = UiTheme.Text, Font = new Font("Segoe UI", 18, FontStyle.Bold), Location = new Point(23, 180), Size = new Size(150, 44) };
            side.Controls.Add(brand);
            side.Controls.Add(new ThemeLabel { Text = "MANAGER  /  " + DeveloperInfo.Version, Dock = DockStyle.Bottom, Height = 24, ForeColor = Muted, Font = new Font("Segoe UI", 8) });
            var nav = new FlowLayoutPanel { Location = new Point(18, 278), Size = new Size(154, 230), FlowDirection = FlowDirection.TopDown, WrapContents = false };
            side.Controls.Add(nav);
            var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(26, 20, 26, 12), Margin = Padding.Empty };
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); main.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); main.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); root.Controls.Add(main, 1, 0);
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            var title = new ThemeLabel { Text = "Your screensaver", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 23, FontStyle.Bold), Padding = new Padding(0, 6, 0, 0) };
            heading.Controls.Add(title, 0, 0); heading.Controls.Add(_state, 1, 0); main.Controls.Add(heading, 0, 0);
            var pages = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }; main.Controls.Add(pages, 0, 1);
            var diagnostics = DiagnosticsPage(); var developer = DeveloperPage(); var customization = CustomizationPage();
            pages.Controls.Add(_settings); pages.Controls.Add(diagnostics); pages.Controls.Add(developer); pages.Controls.Add(customization);
            Action<Panel, string, Button> showPage = (page, text, active) =>
            {
                foreach (Control item in pages.Controls) item.Visible = item == page;
                title.Text = text;
                foreach (Button item in nav.Controls) { item.BackColor = item == active ? UiTheme.Violet : UiTheme.Sidebar; item.ForeColor = Ink; }
                if (page == diagnostics) RefreshLogs();
            };
            AddNavigation(nav, "Screensaver", button => showPage(_settings, "Your screensaver", button));
            AddNavigation(nav, "Customization", button => showPage(customization, "Make it yours", button));
            AddNavigation(nav, "Diagnostics", button => showPage(diagnostics, "Playback diagnostics", button));
            AddNavigation(nav, "Developer", button => showPage(developer, "Meet the developer", button));
            showPage(_settings, "Your screensaver", (Button)nav.Controls[0]);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            footer.Controls.Add(_status, 0, 0);
            footer.Controls.Add(new ThemeLabel { Name = "DeveloperCredit", Text = "Developed by " + DeveloperInfo.Name, Dock = DockStyle.Fill, ForeColor = Muted, Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            main.Controls.Add(footer, 0, 2);
            BuildSettings();
            _thumbnail.Paint += (_, args) => ClockRenderer.DrawOn(args.Graphics, _thumbnail.ClientSize, DraftClock(), DateTime.Now);
            _enabled.CheckedChanged += (_, __) => Dirty(); _minutes.ValueChanged += (_, __) => Dirty();
            _mode.SelectedIndexChanged += (_, __) => { Dirty(); if (!_loading && _saved != null) RefreshThumbnail(); };
            FormClosing += CloseManager;
            FormClosed += (_, __) => { _clockPreviewTimer.Dispose(); _thumbnail.Image?.Dispose(); };
            Shown += (_, __) => RefreshThumbnail();
            LoadSaved();
        }
        private void BuildSettings()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 162)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94)); _settings.Controls.Add(layout);
            var videoCard = new ThemeCard { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 0, 0, 16) }; layout.Controls.Add(videoCard, 0, 0);
            var videoLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            videoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); videoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); videoCard.Controls.Add(videoLayout);
            var imageBox = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 20, 0) }; imageBox.Controls.Add(_thumbnail); imageBox.Controls.Add(_thumbStatus); videoLayout.Controls.Add(imageBox, 0, 0);
            var info = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
            info.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); info.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); info.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); info.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); info.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            info.Controls.Add(_selection, 0, 0); _video.Font = new Font("Segoe UI", 14, FontStyle.Bold); info.Controls.Add(_video, 0, 1);
            var choose = MakeButton("Choose video…"); choose.Name = "ChooseVideo"; choose.Dock = DockStyle.Fill; choose.Click += (_, __) => ChooseVideo(); info.Controls.Add(choose, 0, 2);
            var included = MakeButton("Use included video"); included.Dock = DockStyle.Fill; included.Click += (_, __) => SelectVideo(null, true); info.Controls.Add(included, 0, 3);
            info.Controls.Add(new ThemeLabel { Text = "MP4 / H.264 recommended", ForeColor = Muted, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9), Padding = new Padding(0, 6, 0, 0) }, 0, 4); videoLayout.Controls.Add(info, 1, 0);
            var preferences = new ThemeCard { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 0, 0, 16) }; layout.Controls.Add(preferences, 0, 1);
            var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); options.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            options.Controls.Add(Caption("ACTIVATION"), 0, 0); options.Controls.Add(Caption("PLAYBACK MODE"), 1, 0);
            options.Controls.Add(_enabled, 0, 1);
            _mode.Items.AddRange(new object[] { "Compatibility (recommended)", "Automatic / hardware" }); options.Controls.Add(_mode, 1, 1);
            var idle = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            idle.Controls.Add(new ThemeLabel { Text = "Start after", AutoSize = true, Padding = new Padding(0, 4, 4, 0) }); idle.Controls.Add(_minutes); idle.Controls.Add(new ThemeLabel { Text = "min idle", AutoSize = true, Padding = new Padding(4, 4, 0, 0) }); options.Controls.Add(idle, 0, 2);
            options.Controls.Add(new ThemeLabel { Text = "Compatibility helps older graphics drivers.", ForeColor = Muted, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9), Padding = new Padding(0, 4, 0, 0) }, 1, 2); preferences.Controls.Add(options);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var primary = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            _apply.Name = "ApplyChanges"; _apply.Click += async (_, __) => await ApplyAsync(); primary.Controls.Add(_apply);
            var preview = MakeButton("Preview selection"); preview.Name = "PreviewSelection"; preview.Click += (_, __) => PreviewSelection(); primary.Controls.Add(preview);
            var reload = MakeButton("Discard changes"); reload.Click += (_, __) => LoadSaved(); primary.Controls.Add(reload); actions.Controls.Add(primary, 0, 0);
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            AddTool(tools, "Windows settings", () => Process.Start("rundll32.exe", "shell32.dll,Control_RunDLL desk.cpl,,1"));
            AddTool(tools, "Open Setup", () => Process.Start(Path.Combine(_service.Root, SaverSettings.SetupFile)));
            AddTool(tools, "Uninstall…", () => { Process.Start(Path.Combine(_service.Root, SaverSettings.SetupFile), "/uninstall"); Close(); }); actions.Controls.Add(tools, 0, 1); layout.Controls.Add(actions, 0, 2);
        }
        private Panel DiagnosticsPage()
        {
            var page = new Panel { Dock = DockStyle.Fill };
            var card = new ThemeCard { Dock = DockStyle.Fill, Padding = new Padding(18) };
            var tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50 };
            AddTool(tools, "Refresh logs", RefreshLogs);
            AddTool(tools, "Open log folder", () => { string folder = Path.Combine(_service.Root, "Logs"); Directory.CreateDirectory(folder); Process.Start("explorer.exe", "\"" + folder + "\""); });
            card.Controls.Add(_logs); card.Controls.Add(tools); page.Controls.Add(card); return page;
        }
        private Panel DeveloperPage()
        {
            var page = new Panel { Dock = DockStyle.Fill };
            var card = new ThemeCard { Dock = DockStyle.Fill, Padding = new Padding(28) }; page.Controls.Add(card);
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
            foreach (int height in new[] { 32, 62, 36, 90, 44, 44, 44 }) content.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); card.Controls.Add(content);
            content.Controls.Add(Caption("THE PERSON BEHIND CINESAVER"), 0, 0);
            content.Controls.Add(new ThemeLabel { Text = DeveloperInfo.Name, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 28, FontStyle.Bold) }, 0, 1);
            content.Controls.Add(new ThemeLabel { Text = DeveloperInfo.Role, Dock = DockStyle.Fill, ForeColor = Accent, Font = new Font("Segoe UI", 12) }, 0, 2);
            content.Controls.Add(new ThemeLabel { Text = DeveloperInfo.Bio, Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) }, 0, 3);
            content.Controls.Add(DeveloperLink(DeveloperInfo.Website, DeveloperInfo.Website), 0, 4);
            content.Controls.Add(DeveloperLink(DeveloperInfo.Email, "mailto:" + DeveloperInfo.Email), 0, 5);
            content.Controls.Add(new ThemeLabel { Text = "CineSaver  /  Version " + DeveloperInfo.Version, Dock = DockStyle.Fill, ForeColor = Muted }, 0, 6);
            content.Controls.Add(new ThemeLabel { Text = "Sample developer profile · customize with your own details.", Dock = DockStyle.Fill, ForeColor = Muted, Padding = new Padding(0, 14, 0, 0) }, 0, 7);
            return page;
        }
        private LinkLabel DeveloperLink(string text, string target)
        {
            var link = new LinkLabel { Text = text, Dock = DockStyle.Fill, LinkColor = Accent, ActiveLinkColor = Accent, VisitedLinkColor = Accent, AutoEllipsis = true };
            link.LinkClicked += (_, __) => { try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch (Exception ex) { _status.Text = ex.Message; } }; return link;
        }
        private static Label Caption(string text) => new ThemeLabel { Text = text, ForeColor = Muted, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        private static Button MakeButton(string text, bool primary = false)
        {
            return UiTheme.Button(text, primary);
        }
        private static void AddNavigation(FlowLayoutPanel nav, string text, Action<Button> action)
        {
            var button = MakeButton(text); button.Width = 154; button.Height = 44; button.AutoSize = false; button.TextAlign = ContentAlignment.MiddleLeft; button.FlatAppearance.BorderSize = 0;
            button.Click += (_, __) => action(button); nav.Controls.Add(button);
        }
        private void AddTool(FlowLayoutPanel row, string title, Action action)
        {
            var button = MakeButton(title); button.Height = 32; button.MinimumSize = new Size(90, 32); button.Font = new Font("Segoe UI", 9);
            button.Click += (_, __) => { try { action(); } catch (Exception ex) { _status.Text = ex.Message; } }; row.Controls.Add(button);
        }
        private void Dirty() { if (!_loading) _status.Text = "Unsaved changes. Preview your selection, then apply when ready."; }
        private void ChooseVideo()
        {
            using (var dialog = new OpenFileDialog { Filter = SaverSettings.VideoFilter, CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) SelectVideo(dialog.FileName, false);
        }
        internal void SelectVideo(string path, bool included)
        {
            _newVideo = path; _included = included;
            _video.Text = included ? "Included video" : Path.GetFileName(path);
            _selection.Text = "SELECTED VIDEO · NOT APPLIED"; Dirty(); RefreshThumbnail();
        }
        internal ManagerSnapshot PreviewCandidate()
        {
            ManagerSnapshot current = _service.Load();
            return new ManagerSnapshot { SaverPath = current.SaverPath,
                VideoPath = _included ? Path.Combine(Path.GetDirectoryName(current.SaverPath), "screensaver.mp4") : _newVideo ?? current.VideoPath,
                Preferences = new SaverPreferences { Compatibility = _mode.SelectedIndex == 0, Clock = DraftClock() } };
        }
        private void RefreshThumbnail()
        {
            _thumbnailCancellation?.Cancel();
            var cancellation = new CancellationTokenSource(); _thumbnailCancellation = cancellation;
            _thumbnailTasks.RemoveAll(task => task.IsCompleted);
            _thumbnailTasks.Add(UpdateThumbnailAsync(cancellation));
            _thumbnailTask = Task.WhenAll(_thumbnailTasks);
        }
        private async Task UpdateThumbnailAsync(CancellationTokenSource cancellation)
        {
            try
            {
                // Debounce quick selections, with no dependency on an earlier UI task.
                await Task.Delay(180, cancellation.Token);
                if (IsDisposed || !Visible || _saved == null) return;
                var old = _thumbnail.Image; _thumbnail.Image = null; old?.Dispose(); _thumbStatus.Text = "Loading thumbnail…";
                Bitmap bitmap = await PreviewSupport.ThumbnailAsync(PreviewCandidate(), cancellation.Token);
                if (cancellation.IsCancellationRequested || IsDisposed) { bitmap?.Dispose(); return; }
                _thumbnail.Image = bitmap; _clockPreview.Invalidate(); _thumbStatus.Text = bitmap == null ? "Thumbnail unavailable · try Preview selection" : "Still preview · use Preview selection to play";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed && !cancellation.IsCancellationRequested)
                { _thumbStatus.Text = "Thumbnail unavailable · try Preview selection"; _thumbStatus.AccessibleDescription = ex.Message; }
            }
            finally { if (ReferenceEquals(_thumbnailCancellation, cancellation)) _thumbnailCancellation = null; cancellation.Dispose(); }
        }
        private async void CloseManager(object sender, FormClosingEventArgs e)
        {
            if (_closed) return;
            e.Cancel = true;
            if (_busy || _closing) return;
            _closing = true; _thumbnailCancellation?.Cancel();
            await _thumbnailTask; _closed = true; Close();
        }
        private void LoadSaved()
        {
            try
            {
                _loading = true; _saved = _service.Load();
                _video.Text = string.IsNullOrEmpty(_saved.Preferences.VideoFile) ? "Included video" : _saved.Preferences.VideoName;
                _selection.Text = "CURRENT VIDEO";
                _enabled.Checked = _saved.Enabled; _minutes.Value = Math.Max(1, Math.Min(120, _saved.Preferences.IdleSeconds / 60));
                _mode.SelectedIndex = _saved.Preferences.Compatibility ? 0 : 1; _newVideo = null; _included = false;
                LoadCustomization(_saved.Preferences);
                _state.Text = _saved.Enabled ? "●  Enabled" : _saved.Selected ? "○  Disabled" : "Another saver selected";
                _status.Text = _saved.Warning ?? "Ready. Your settings stay active when you close the manager."; _settings.Enabled = true;
            }
            catch (Exception ex) { _settings.Enabled = false; _status.Text = ex.Message; }
            finally { _loading = false; }
            if (Visible) RefreshThumbnail();
        }
        private async Task ApplyAsync()
        {
            string video = _newVideo; bool included = _included, compatibility = _mode.SelectedIndex == 0, enabled = _enabled.Checked;
            int seconds = (int)_minutes.Value * 60;
            ClockOptions clock = DraftClock(); string shortcut = DraftShortcut();
            _busy = true; Enabled = false; _status.Text = "Saving changes…";
            try
            {
                _thumbnailCancellation?.Cancel(); await _thumbnailTask;
                _status.Text = "Applying settings…";
                await Task.Run(() => _service.Apply(video, included, compatibility, seconds, enabled, clock, shortcut));
                LoadSaved(); _status.Text = "Saved. Changes apply the next time the screensaver starts.";
            }
            catch (Exception ex) { _status.Text = "Changes could not be saved: " + ex.Message; }
            finally { _busy = false; Enabled = true; }
        }
        private async void PreviewSelection()
        {
            if (_previewing) return;
            _previewing = true;
            try
            {
                _thumbnailCancellation?.Cancel(); await _thumbnailTask;
                if (IsDisposed || _closing) return;
                using (var preview = new PreviewWindow(PreviewCandidate())) preview.ShowDialog(this);
            }
            catch (Exception ex) { _status.Text = "Preview could not start: " + ex.Message; }
            finally { _previewing = false; }
        }
        private void RefreshLogs()
        { try { _logs.Text = _service.ReadDiagnostics(); _logs.SelectionStart = _logs.TextLength; _logs.ScrollToCaret(); } catch (Exception ex) { _logs.Text = ex.Message; } }
    }
    internal sealed class PreviewWindow : Form
    {
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr state);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        private delegate bool EnumProc(IntPtr hwnd, IntPtr state);
        private readonly Panel _surface = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        private readonly Label _status = new Label { Text = "Playing your selection. Close this window to stop.", Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(12, 8, 0, 0) };
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 500 };
        private Process _process;
        private bool _stopping, _closed;
        internal PreviewWindow(ManagerSnapshot snapshot)
        {
            UiTheme.Apply(this);
            Text = "CineSaver Preview"; ClientSize = new Size(800, 540); MinimumSize = new Size(400, 300);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 10);
            Controls.Add(_surface); Controls.Add(_status);
            Shown += (_, __) =>
            {
                try
                {
                    _process = Process.Start(new ProcessStartInfo(snapshot.SaverPath, PreviewSupport.Arguments(_surface.Handle, snapshot))
                    { UseShellExecute = false, WorkingDirectory = Path.GetTempPath(), CreateNoWindow = true });
                    _timer.Start();
                }
                catch (Exception ex) { _status.Text = "Preview could not start: " + ex.Message; }
            };
            _timer.Tick += (_, __) =>
            {
                if (_process != null && _process.HasExited)
                { _timer.Stop(); _status.Text = "Preview stopped. Check Diagnostics if the video did not play."; }
            };
            FormClosing += ClosePreview;
            FormClosed += (_, __) => _timer.Dispose();
        }
        private async void ClosePreview(object sender, FormClosingEventArgs e)
        {
            if (_closed) return;
            e.Cancel = true;
            if (_stopping) return;
            _stopping = true; _timer.Stop();
            IntPtr parent = _surface.Handle;
            try
            {
                await Task.Run(() =>
                {
                    if (_process == null || _process.HasExited) return;
                    EnumProc callback = (hwnd, _) =>
                    {
                        GetWindowThreadProcessId(hwnd, out uint pid);
                        if (pid == _process.Id) { PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero); return false; }
                        return true;
                    };
                    EnumChildWindows(parent, callback, IntPtr.Zero); GC.KeepAlive(callback);
                    if (!_process.WaitForExit(2500)) { _process.Kill(); _process.WaitForExit(3000); }
                });
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { _process?.Dispose(); _closed = true; Close(); }
        }
    }

    internal static class ManagerProgram
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
        [STAThread]
        private static void Main(string[] args)
        {
            Environment.CurrentDirectory = Path.GetTempPath();
            if (args.Length == 1 && args[0] == "/start")
            {
                // A short-lived launcher, before the manager mutex/UI or thumbnail decoder.
                try
                {
                    string saver = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), SaverSettings.SaverFile);
                    if (!File.Exists(saver)) throw new IOException("CineSaver is missing. Run Setup to repair the installation.");
                    Process.Start(new ProcessStartInfo(saver, "/s /shortcut") { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "CineSaver"); }
                return;
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using (var mutex = new Mutex(true, "Local\\ScreensaverApp.Manager", out bool created))
            {
                if (!created)
                {
                    IntPtr window = FindWindow(null, "CineSaver Manager");
                    if (window == IntPtr.Zero) window = FindWindow(null, "Screensaver Manager");
                    if (window != IntPtr.Zero) { ShowWindow(window, 9); SetForegroundWindow(window); }
                    return;
                }
                Application.Run(new ManagerWindow(new ManagerService()));
            }
        }
    }
}


