# ScreensaverApp — build & install

A minimal, dependency-free video screensaver. Targets .NET Framework 4.8
(built into every Windows 10/11 install), so the compiled `.scr` needs
nothing installed on the target machine and stays a few hundred KB.

## 1. Build

1. Open `ScreensaverApp.csproj` in Visual Studio 2022 (or `dotnet build` from
   the CLI — the SDK-style project works with either).
2. Set configuration to **Release**, platform **x64** (this project pins
   `PlatformTarget` to x64 in the `.csproj` — see the note below on why).
3. First build will restore NuGet packages (needs internet access once) — it
   pulls in LibVLCSharp and the bundled native libvlc binaries.
4. Build. Output lands in `bin\Release\net48\ScreensaverApp.exe`, alongside
   a `libvlc` folder containing the native playback engine — **that whole
   folder needs to travel with the exe**, both when testing and when you
   install it into System32 later.

`UseWPF` and `UseWindowsForms` in the `.csproj` cover the UI shell; the
`LibVLCSharp` / `LibVLCSharp.WPF` / `VideoLAN.LibVLC.Windows` packages
supply video playback.

### Why LibVLC instead of WPF's built-in MediaElement

WPF's `MediaElement` renders through Windows' Media Foundation pipeline,
which has a long-standing bug: it doesn't correctly expand limited-range
YCbCr color to full-range RGB on many H.264 files, so video looks visibly
washed out and flat compared to VLC or MPV playing the identical file.
LibVLC's decode and color-conversion pipeline gets this right, at the cost
of a heavier output folder (~40–50 MB of native libvlc binaries instead of
using what's already built into Windows). For a screensaver that's still a
perfectly reasonable trade — it's not shipped to end users at scale, it's
copied once into System32 on machines you control.

## 2. Add your video

Drop your video next to the built `.exe` as `screensaver.mp4`
(`bin\Release\net48\screensaver.mp4`). Use H.264/AAC in an MP4 container —
that combination decodes through Media Foundation on every Windows version
back to 7, so you won't hit missing-codec failures on other people's
machines. Avoid exotic codecs (VP9, AV1, HEVC) unless you know the target
machine can decode them.

## 3. Package as a .scr and install

```
copy bin\Release\net48\ScreensaverApp.exe  ScreensaverApp.scr
copy bin\Release\net48\screensaver.mp4      screensaver.mp4
```

Then, as admin, copy the `.scr`, the video, **and the entire `libvlc`
folder** from `bin\Release\net48\` into `C:\Windows\System32`:

```
copy ScreensaverApp.scr        C:\Windows\System32\
copy screensaver.mp4           C:\Windows\System32\
xcopy /E /I libvlc              C:\Windows\System32\libvlc\
```

Don't also copy these into `SysWOW64` — this project is pinned to a 64-bit
build specifically so it never needs to. (A 32-bit build placed in
`System32` runs into a nasty trap: Windows silently redirects any file
path containing the literal text "System32" to `SysWOW64` instead for
32-bit processes, so a 32-bit `.scr` would never actually find its own
`libvlc` folder once installed, even though it works fine everywhere else.)

Missing the `libvlc` folder is the most common failure mode after this
change — the screensaver will show a black screen with no video and no
error, because LibVLC's native binaries simply aren't there to load.

It will then appear in **Settings → Personalization → Lock screen →
Screen saver settings** dropdown, named after the file (`ScreensaverApp`).

## 4. Test without installing

You don't need to install it to test the three modes Windows will actually
invoke — just run the exe directly with the same arguments Windows uses:

```
ScreensaverApp.exe /s          -> fullscreen run, across all monitors
ScreensaverApp.exe /c          -> config dialog
ScreensaverApp.exe /p 12345    -> embeds into window handle 12345 (preview)
```

For `/p`, `12345` needs to be a real HWND to be visible — easiest way to
sanity-check preview mode is to just add the .scr via the Settings dropdown
and look at the live thumbnail Windows renders there.

## Design notes / why it's built this way

- **One window per monitor** (`Screen.AllScreens`) rather than one giant
  virtual-desktop window — this keeps each screen's video correctly
  positioned even with mixed DPI or non-aligned monitor arrangements, and
  avoids the stretching artifacts you get from spanning a single window
  across monitors with different scale factors.
- **PerMonitorV2 DPI awareness** (`app.manifest`) so the video renders sharp
  on high-DPI displays instead of being upscaled blurry by Windows' DPI
  virtualization.
- **Mouse-jitter tolerance**: exits only after the cursor moves more than 8px
  *after* a short arming delay, not on any `MouseMove` event. Without this,
  screensavers built from naive `MouseMove -> Exit` code quit almost
  instantly on trackpads or optical mice that report tiny phantom deltas.
- **LibVLC for playback, not WPF's `MediaElement`** — see the build section
  above; MediaElement's Media Foundation backend washes out colors on many
  H.264 files, LibVLC's pipeline doesn't.
- **Crop-to-fill instead of letterbox-to-fit**: once playback starts, the
  code compares the video's native aspect ratio to the window's and sets
  LibVLC's crop geometry to fill the window completely (cropping overflow)
  rather than showing bars — the same idea as CSS `object-fit: cover`.
- **Pinned to x64** (`PlatformTarget` in the `.csproj`) specifically to
  avoid the WOW64 System32→SysWOW64 path-redirection trap that would
  otherwise break the app the moment it's installed into `System32`, even
  though it runs fine from anywhere else.
- **Looping is set two ways**: an `input-repeat` option on the media itself,
  plus a manual restart on `EndReached` as a fallback, since some libvlc
  builds ignore `input-repeat` on certain container formats.
- **Video shipped as a plain file, not an embedded resource** — playback
  needs a real seekable file/URI; embedding it as a resource would just
  require extracting it to a temp file at startup anyway, so shipping the
  file directly is simpler and just as clean.
