# CineSaver 1.6.0

A Windows 10/11 x64 video screensaver using .NET Framework 4.8 and LibVLC.

## Install or update

1. Double-click `dist\CineSaver-Setup.exe`.
2. Keep **Compatibility mode** selected on older or unstable graphics drivers.
3. Optionally choose another video and change the idle interval.
4. Click **Install / Update**. A first installation selects the screensaver;
   later updates preserve whether it is enabled or another saver is selected.

The one setup file contains the video and all required x64 DLLs. Copy just this
setup file to another PC; do not copy files into System32. No administrator rights,
VLC installation, or developer tools are required on the target PC.

Installation is per Windows account, under
`%LOCALAPPDATA%\CineSaver\Releases`. Setup adds **CineSaver Manager** and
an uninstall shortcut to the current user's Start menu. An installed copy of Setup
is kept at `%LOCALAPPDATA%\CineSaver\CineSaver-Setup.exe` for repairs and uninstall.
An upgrade from ScreensaverApp reuses its original AppData folder and stable
registration identifiers to preserve saved videos and the original Windows-settings
backup. Its Windows Apps entry and Start menu shortcuts are renamed to CineSaver;
new installations use the CineSaver folder. Source project/namespace identifiers
remain unchanged for compatibility with the existing development setup.
New releases are extracted before selection; obsolete managed releases are removed
afterward when their files are not locked. The old manually copied System32 files
are left alone and are no longer selected after installation.

Remove **CineSaver** from Windows **Settings → Apps** to uninstall. If this
app is still selected, uninstall restores the screensaver/idle settings saved at
the first installation. It preserves a different saver selected afterward and
does not change the Windows “On resume, display logon screen” preference.

Uninstall runs from Temp, retries short-lived locks, and preserves its marker and
registration if cleanup is blocked so removal can be retried. Manager, Setup,
shortcuts, and preview launchers use a working directory outside the installation
to avoid keeping release folders locked through external Windows processes.

## CineSaver Manager

After installing once, open **Start → CineSaver → CineSaver Manager**.
Windows' Screen Saver Settings **Settings** button also opens the manager.
There is no need to rerun Setup to change everyday preferences.

Setup and Manager share a dark navy theme with cyan/violet accents drawn from
the supplied neon infinity monitor logo. The logo is also the Windows application
icon. Buttons and labels keep their intended text colors while saving/installing.

- Choose a video, or restore **Use included video** to return to the video bundled
  with Setup. MP4/H.264 is recommended; the picker also accepts M4V, MKV, MOV, AVI,
  WebM, and WMV. Codec support and resource use depend on the chosen video.
- Set the idle interval, enable/disable this screensaver, and select compatibility
  or automatic/hardware playback. Click **Apply changes** to save.
- **Preview selection** plays the video you just chose before applying it, in a
  separate resizable window. It also uses your pending playback mode and clock. Closing the
  preview stops its renderer, including when playback is unresponsive.
- The current video thumbnail loads automatically. Choosing a video or restoring
  the included video updates the thumbnail. **Discard changes** returns to the
  saved selection. A thumbnail failure leaves the Preview action available.
- The **Developer** page and bottom credit use clearly marked sample information
  in `manager/DeveloperInfo.cs`. See [BUILDING.md](BUILDING.md) to customize and rebuild.
- All application and shortcut icons come from `assets/App.ico`. Replace that
  file with your own multi-size Windows icon and rebuild Setup to customize it.
- Read bounded playback logs in **Diagnostics**, or open Windows settings and
  uninstall controls from the manager.

The manager runs only when opened. It adds no tray process, service, or startup
task, and does not load VLC into the manager process. Thumbnail generation briefly
starts a separate player, captures one still, and stops it (12-second limit with
a bounded cleanup wait). Thumbnails remain in memory; temporary images are removed.
A live preview runs until closed. The manager can be closed after saving.

Selected videos are copied into `%LOCALAPPDATA%\CineSaver\Videos`; the original
source file can then be moved or deleted. Preferences are atomically saved in
`%LOCALAPPDATA%\CineSaver\settings.xml`. Video selection and preferences survive
app updates, while the original bundled video stays available as a default.
Changes apply to the next screensaver/preview launch. Editing preferences while a
different Windows screensaver is selected leaves that other saver enabled unless
you explicitly enable this one. Uninstall removes the manager, owned shortcuts,
managed videos, and preferences along with the player.

## Clocks and keyboard shortcut

Open **Customization** in CineSaver Manager:

- Choose **Minimal digital**, **Neon digital**, **Analog**, or **Flip cards**.
  Select **Off · video only** to hide the clock (the default).
- Choose a corner or the center, Small/Medium/Large size, and cyan, white, violet,
  or amber accents. Toggle 24-hour time, seconds, and the date independently.
- The live clock preview and video thumbnail reflect these choices immediately.
  **Preview selection** opens the selected video with the draft clock; **Apply
  changes** saves both. **Discard changes** restores the saved preferences.
- Under **Start screensaver shortcut**, select an unused **Ctrl + Alt + letter**
  combination, for example **Ctrl + Alt + S**, then apply. Disabled is the default.
  The Start menu's **CineSaver → Start CineSaver** shortcut also starts it directly.

The hotkey belongs to that Windows Start menu shortcut. It works with the manager
closed, using the signed-in Windows desktop's Explorer shell, and adds no service,
tray app, startup task, or resident hotkey listener. Other software can reserve the
same combination; choose another letter if it conflicts. Windows may take a moment
to recognize a changed shortcut. Release the launch keys; Escape or other input
dismisses the saver. Starting it manually does not lock Windows; use Win+L to lock.
The idle timer and the manual shortcut are independent, so a shortcut can still
start CineSaver when automatic idle activation is disabled.

Clocks read **DateTime.Now** from the local PC on every update, including changes
to the system clock, time zone, daylight saving, and date. They use Windows fonts
and built-in .NET drawing, with no added clock library, downloads, or subscriptions.
A native transparent child window keeps the clock above the VLC video in both
playback modes, clips it to previews, and passes input through. Each monitor gets
its own clock. Clock drawings refresh at most once per second and only occupy the
clock's small rectangle; video decoding remains the main resource cost.

Clock and shortcut preferences survive Install / Update. Uninstall removes the
Start CineSaver shortcut and its hotkey along with the other application files.

## Playback modes and resource use

**Compatibility** (default): software video decoding, Windows GDI video output,
and software rendering for the WPF shell. This avoids VLC's Direct3D video path
and reduces reliance on older graphics drivers. Desktop composition still belongs
to Windows; the app cannot repair a system-wide driver or hardware failure.

Select automatic/hardware playback in the manager to opt into VLC's normal
hardware-capable output. This may reduce CPU work on stable graphics drivers.
Switch back to Compatibility if it causes problems. Audio, on-screen titles,
and video OSD are disabled.

The package includes only x64 VLC, avoiding approximately 94.5 MiB of unused x86
binaries from the previous output. This remains a continuously playing video
application, rather than a static screensaver. CPU/memory depend on the video,
screen size, playback mode, and monitor count. The original video is preserved;
no silent transcoding or quality reduction is performed.

## Reliability changes

- An independent message thread receives raw keyboard/mouse-button/wheel input,
  so exit does not depend on WPF focus over the native video. Cursor movement
  uses physical screen coordinates, with an 8-pixel tolerance and 600 ms arming
  period. Only fullscreen mode exits on user input; the Settings thumbnail does not.
- Exit first hides the windows. Native playback calls, including synchronous VLC
  Stop/Dispose, run on dedicated playback workers, never on the UI thread.
- An independent watchdog terminates only this process after 2 seconds of blocked
  shutdown, or 8 seconds without a UI heartbeat. This works while Windows can
  still schedule the watchdog; it is not a recovery tool for an OS/GPU-wide hang.
- Missing media and playback errors exit safely. No playback progress for 15
  seconds, including a preview that never loads, also requests exit.
- Native video windows are created black, use Windows-owned brushes, and are
  updated after video-output creation. Cropping is refreshed when the viewport
  changes. There is no transparent WPF video-wrapper overlay.
- Playback callbacks cannot synchronously wait for the UI. Pending restarts are
  discarded on shutdown; one shared VLC engine serves all monitor players.
- Fullscreen placement uses physical HWND coordinates for DPI correctness. A
  display-configuration change ends the current saver so the next launch can
  enumerate the new monitors. A second fullscreen instance is rejected.
- Logs are asynchronous, queue-limited, and rotate around 1 MiB into one previous
  file at `%LOCALAPPDATA%\CineSaver\Logs\saver.log`. No input content is logged.

## Build a new setup file

Install a .NET SDK capable of building .NET Framework 4.8 desktop projects. NuGet
restore needs network access the first time; playback and installation work offline.

```powershell
.\Build-Installer.ps1 -VideoPath 'C:\Videos\screensaver.mp4'
```

This builds into a fresh staging folder, creates a real `.scr` and its matching
`.scr.config`, bundles x64 dependencies and the supplied video, and writes:

```text
dist\CineSaver-Setup.exe
dist\CineSaver-Setup.sha256
```

For development only:

```powershell
dotnet build ScreensaverApp.csproj -c Release
# /c: open the bundled manager; /p <HWND>: preview; /s: actual fullscreen
```

Build-Installer also builds the separate `manager/Manager.csproj` and bundles its
executable; a player-only development build shows a hint if no manager is next to it.
No-argument/config mode does not load native VLC. Unknown arguments and invalid preview
handles exit without opening a fullscreen window.

## Verification

The smoke-test executable contains isolated registry/filesystem installer tests,
real native watchdog deadline tests, and actual playback integration tests.
Fullscreen integration uses a separate desktop that is never switched to, so it
does not cover the user's desktop. Native key messages exercise fullscreen exit;
raw-device registration is checked separately.

```powershell
dotnet build tests\SmokeTests.csproj -c Release
$fixture = Join-Path $PWD 'obj\installer-smoke'
Start-Process .\dist\CineSaver-Setup.exe -ArgumentList @('/extract', ('"' + $fixture + '"')) -Wait -WindowStyle Hidden
.\tests\bin\Release\net48\ScreensaverApp.SmokeTests.exe "$fixture\CineSaver.scr"
.\tests\bin\Release\net48\ScreensaverApp.SmokeTests.exe --fullscreen "$fixture\CineSaver.scr"
# Use the payload.zip path printed/created under obj\package-* by the build:
.\tests\bin\Release\net48\ScreensaverApp.SmokeTests.exe --installer 'C:\path\to\payload.zip'
.\tests\bin\Release\net48\ScreensaverApp.SmokeTests.exe --manager 'C:\path\to\payload.zip'
.\tests\bin\Release\net48\ScreensaverApp.SmokeTests.exe --migration 'C:\path\to\payload.zip'
```

Playback tests include two loops of the supplied 15.77-second video, viewport
resizing, normal close, destroyed preview parent, invalid handle, missing video,
and corrupt video. Longer/custom clips require increasing the loop-test duration.
The installer/manager tests use isolated registry and file locations, including
their shortcut folders; they never change the real desktop's screensaver settings
or Start menu. They cover Apply, custom-video import, update persistence, default
restore, preview cleanup, bounded diagnostics, and migration from the earlier setup.

The original rare driver black screen has not been reproduced, and multiple
physical monitors/mixed-DPI transitions need testing on suitable hardware.
