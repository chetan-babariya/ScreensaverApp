# Verification — 20 October 2026

## Clocks and manual launch shortcut (1.6.0)

The final package passed 108 customization checks and seven existing safety checks.
Coverage includes 12/24-hour formatting (noon/midnight), seconds, invalid settings,
all four rendered designs at five positions and three viewport sizes, persisted
preferences, upgrade preservation, and clearing/removing shortcut hotkeys.
The actual Customization Apply button saves drafts through its background service;
Discard restores the saved clock. Controls fit the minimum manager window size.

Native playback was verified with Minimal, Neon, and Analog in Compatibility mode
and Flip in Automatic mode. Each clock remains a child of its saver window,
resizes within the preview, and closes with playback or a destroyed host. GDI
resource counts did not accumulate during ticking (34 handles in Compatibility,
33 in Automatic). Manager previews and native playback screenshots were visually
checked under the final staging folder's `customization-results` directory.

A full-screen run with a clock survived two video loops on an isolated desktop
and exited via Escape in 180 ms. Measured on this PC in that test: 0.39% of total
CPU, peak working set 174 MiB, peak private memory 301 MiB. These figures include
video decoding and do not measure clock-only overhead or predict other machines.
An isolated-desktop `/start` launch confirmed the manager launcher exits, starts
the saver even with idle activation disabled, and permits Escape exit (218 ms).
Start menu `.lnk` targets, arguments, hotkey values, update, disable, and uninstall
were verified in an isolated Programs folder. The user's actual Explorer hotkey
was not registered or invoked by the tests.

Clock rendering uses built-in .NET/Windows drawing with no new NuGet dependency.
The small native layered child is updated once per second and passes input
through. Microsoft documents [layered child windows and UpdateLayeredWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow)
and [Shell link hotkeys](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishelllinkw-sethotkey).
The initial launch chord is ignored until released (five-second ceiling), while
Escape always dismisses. Ordinary startup retains immediate keyboard dismissal.

`Build-Installer.ps1` was also corrected for Windows PowerShell 5.1: the default
output path is now assigned after `param`, where `$PSScriptRoot` is available.
The final Release installer is version 1.6.0; debug symbols and tests are excluded.
Regression suites also passed: manager 34, installer 12, legacy migration eight,
uninstall locks five, native icons seven, and installer layout/readability three.
Extraction from the final Setup executable matched the staged player/manager
SHA256 hashes and contained no PDB files or test executables. Final Setup SHA256:
`64D3A22DE2B9D7C6199E0D43881EBBDDAAD17C3101ABC8E78E2B9B0B0A6A3596`.

## Uninstall directory locks (1.5.1)

The reported failure involved empty 1.2/1.3 release directories held as working
directories by old System32 preview processes. The uninstall helper also inherited
its launcher's working directory. Copying an EXE into Temp alone did not release
those directory locks.

Manager/Setup entry points, Start menu shortcuts, helper launchers, and preview
processes now use Temp as their working directory. Uninstall moves its own working
directory outside the installation, stops installed Setup windows as well as
owned player/manager processes, retries short-lived locks, and keeps the marker
and registry backup until file removal succeeds. A failed removal can be retried.

Five regression checks reproduce self-held directory locks, an external process
temporarily pinning an empty release, and a persistent file lock followed by a
successful retry. The 12 installer integration checks passed. The actual interrupted
1.4 installation was removed after backing up settings/video/registry data and
stopping the two identified orphaned previews. Its directory, shortcuts, Windows
Apps entry, and installation state were confirmed removed.

Final manager integration passed 34 checks, including native video playback from
Temp and the shortcut working-directory assertion. Final installer extraction
matched the staged player/manager hashes. Live recovery backups are kept locally
under the ignored `recovery-backups` folder; test-preview logs were archived there.

## CineSaver rename (1.5.0)

Public UI, version metadata, player/manager/installer filenames, Start menu entries,
Windows Apps display name, uninstall messages, embedded resource names, and build
documentation now use CineSaver. New installations use `%LOCALAPPDATA%\CineSaver`.
Existing managed installations keep their original data folder, marker, registry
identity, and cross-version mutexes so updates preserve videos, preferences, and
the original Windows-settings backup. Old owned shortcuts are replaced; unrelated
files in the old shortcut folder are retained.

Installer integration passed 12 checks, upgrade/root selection passed eight,
and native icon verification passed seven. The 45-check player/watchdog suite
passed with the renamed player. Setup's three layout/readability checks passed.
All 33 manager integration checks passed with the final CineSaver package, including
unsaved-video preview and readable disabled text. The CineSaver title/sidebar and
installer were visually checked. Final packaged player/manager hashes matched the staged release. Thumbnail capture
now waits for the actual media timestamp, with a bounded short-clip fallback, so
slow native startup does not capture a black opening frame prematurely.

## Supplied logo and coordinated theme (1.4.0)

The supplied transparent neon infinity monitor PNG is preserved in
`assets/App-source.png`. The converter creates transparent 16-256 pixel icon
representations without changing the artwork. Manager and Setup use the same
navy surfaces, cyan text accents, violet primary actions, static logo, and dark
Windows title bars. Shared controls retain readable text while disabled.

The manager's 33 integration checks passed against the redesigned controls,
including actual preview playback, saved-state protection, Apply, and renderer
cleanup. Setup layout checks cover clipping of the selected filename, control
fit, and the disabled install button. Both interfaces were captured and visually
checked. These are static WinForms surfaces; no extra background process or
continuous UI animation was introduced.

The final 1.4 package passed all 33 manager checks, seven icon checks matching the
supplied artwork in each executable, and three installer layout/readability checks.
Final embedded player/manager hashes matched staging after Setup extraction.

## Button colors and branding (1.3.1)

Apply disables the manager while saving. Native WinForms disabled-button painting
ignores the configured white ForeColor, which produced dark labels on blue and
navy backgrounds. Manager buttons and labels now preserve their configured foreground and
background when disabled; enabled buttons retain normal Windows interaction.

A shared multi-size `assets/App.ico` is embedded in the player, manager, and
installer as both a native executable icon and a managed window icon. Start menu
shortcuts explicitly select icon 0 from their target. Windows Apps already points
to the manager executable. The provided neon monitor artwork is retained as
`assets/App-source.png`; the icon converter and replacement procedure
are included in `assets/Create-AppIcon.ps1` and `BUILDING.md`.

The manager integration suite passed 33 checks, including rendered white glyphs
on both Apply and the sidebar heading while disabled, readability after reenable,
and the saved shortcut's explicit icon location. Seven branding checks extracted
and inspected the native icons from all three release executables and loaded the
embedded window icon. The disabled-manager capture was visually checked, and the
final Setup's extracted player and manager hashes matched the staged release.

## Manager redesign (1.3)

Built the player, redesigned manager, and installer with zero compiler warnings or
errors. Manager remains a separate roughly 44 KiB executable without a VLC
dependency. The sidebar, cards, video thumbnail, Developer page, and footer were
captured and visually checked at normal and minimum window sizes on this display.

The manager integration suite passed 29 checks with a real Windows Forms message
loop. It covers automatic thumbnail generation with nonblack decoded pixels,
pending video and playback-mode previews, the actual Preview selection button,
unchanged saved settings/activation after preview, cancellation during rapid
selections, Apply, manager close, and renderer cleanup. Developer tab and footer
both use the clearly marked sample values in `manager/DeveloperInfo.cs`.

Thumbnail playback uses an offscreen child process. It captures one still and
exits; cancellation/timeout kills only that owned child and removes its temporary
image. A thumbnail failure displays a fallback hint and keeps live preview
available. Thumbnails are not a persistent background animation.

The existing player/watchdog suite passed all 45 checks against the 1.3 player;
installer integration passed 10 checks and legacy migration passed 3. Tests use
isolated installation folders and registry trees. The packaged Setup was extracted
without installing it, and the embedded player/manager hashes matched staging.
The build/customization procedure is in `BUILDING.md`.

## Screensaver Manager (1.2)

The new manager is a separate .NET Framework executable of approximately 30 KiB.
It has no VLC dependency and no startup/tray/service component. The installer
bundles it, creates a per-user Start menu manager shortcut plus an uninstall
shortcut, and connects the screensaver's `/c` Settings action to the manager.

Manager verification covers:

- Loading the installed default video and Windows activation state.
- Start menu shortcut creation and its exact executable target.
- Importing a video into managed storage, including a read-only source, and
  keeping playback independent of the original file's location.
- Saving enable/disable, idle time, and playback mode through the actual Apply
  button without running Setup. Another selected saver is preserved when disabled.
- Preserving the chosen video, playback mode, and disabled state across an app
  update; preserving another selected Windows saver; restoring the original bundled
  video through **Use included video**.
- Launching a real saved-video preview while the automatic saver is disabled,
  resizing it, closing it, and verifying that its renderer process has exited.
- Preserving saved settings after a failed import, rejecting unsafe stored video
  paths, falling back safely, and bounding the diagnostics read.
- Removing the manager, preferences, copied videos, and owned shortcuts on uninstall.
- Importing an earlier 1.1 installation's video while retaining its original
  pre-install Windows-settings backup through upgrade and uninstall.

The manager/preview UI was captured and visually checked. Test settings, installs,
and shortcut folders are isolated from the real Windows configuration and Start
menu. Extraction path-length checks prompted shorter release-folder names and
long-path awareness in the application manifests.

The existing watchdog/playback regression suite also passed with the 1.2 package:
45 checks including two loops, resizing, teardown, and missing/corrupt media.

## Playback and installer baseline

Built the app and the single-file installer with zero compiler warnings/errors.
The final embedded screensaver was extracted from the installer and its SHA256
matched the staged release. The package contains no x86 VLC or LibVLCSharp.WPF.

Verified on Windows 10 22H2, i5-8400 (six cores), GT 710, one 1440×900 display,
with the original 1920×1080 H.264, 30 fps, 15.77-second video. Compatibility mode
was used; no driver update or live screensaver-settings change was made.

Passed checks:

- Independent safety monitor terminates blocked shutdown around 2.1 seconds and
  a process with no UI heartbeat around 8.1 seconds. Native raw input registers.
- Contained on-screen preview survives two video loops and viewport resizing.
  Captures before/after looping show video with black backgrounds and no white bars.
- Preview close completes around 216 ms; destroyed preview parent exits around 534 ms.
- Fullscreen on an isolated desktop survives two loops; injected Escape exits
  normally around 184 ms. The test desktop is never made visible to the user.
- Invalid preview handle and missing video exit immediately. Invalid media exits
  around 15.4 seconds through the startup/playback progress timeout.
- Installer extraction rejects paths outside its destination. Managed deletion
  rejects paths outside its parent directory.
- Fresh install, repeated update, custom video copying, compatibility/hardware
  selection, uninstall, and restoration of original settings pass using an isolated
  test registry tree and filesystem directory. Security/resume settings are preserved.
  Uninstall also preserves another saver selected after this app.
- The setup UI fits without scrolling and was visually checked.

On-screen preview measurement: approximately 0.54–0.68% total CPU, 153 MiB peak
working set, and 285–286 MiB peak private memory. These are short test measurements,
not guaranteed fullscreen/multi-monitor resource limits. The offscreen fullscreen
test does not represent visible fullscreen rendering performance.

Setup is approximately 52.9 MiB; extracted runtime including video is 112.8 MiB.
Installation also retains the setup executable for later changes/uninstall.

The original rare system-wide NVIDIA/DWM black screen was not reproduced.
Physical keyboard/mouse recovery during a real driver failure, long-duration use,
and multiple monitors/mixed-DPI transitions remain hardware validation work.
