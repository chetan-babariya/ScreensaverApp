# Customize and rebuild

Release output is now named `CineSaver-Setup.exe`. The source project remains
`ScreensaverApp.csproj`; keep that filename in the commands below. Fresh installs
use `%LOCALAPPDATA%\CineSaver`. Upgrades keep an existing ScreensaverApp data folder
so videos, settings, and the original uninstall backup remain available.

## Change the application icon

Replace `assets\App.ico` with your own real Windows `.ico` file, then run the
installer build command below. Renaming a PNG/JPG to `.ico` does not convert it.
Include transparent 16, 24, 32, 48, 64, 128, and 256-pixel images in the icon for
clear results at different display sizes.

That single asset supplies the installer executable, manager executable, `.scr`,
window title bars/taskbar icons, Start menu shortcuts, and Windows Apps entry.
The shortcuts explicitly use icon 0 from their target executable. There are no
separate icon files to copy after installation.

Your supplied artwork is saved in `assets\App-source.png`. To convert another PNG
into the shared `.ico`, run this before building Setup:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\assets\Create-AppIcon.ps1 -InputPath 'C:\Images\YourIcon.png'
```

The converter keeps transparency, fits the whole image without stretching, and
creates 16-256 pixel icon sizes. `assets\App-preview.png` is only a preview and is
not used by the application. Running the converter with no input path uses
`assets\App-source.png` again. Replace that source file too if you want to retain
your new artwork in the repository.

The shared colors and styled controls for both windows are in `shared\UiTheme.cs`.
Changing that palette and rebuilding Setup updates Manager and Setup together.

Clock designs are drawn in `shared\ClockRenderer.cs`; the manager controls are in
`manager\ManagerCustomization.cs`. `shared\ClockOptions.cs` defines the saved
clock options. They use built-in .NET drawing and Windows fonts, so no clock
library, license key, or additional package is needed. `ClockOverlay.cs` places
the drawings above native video. `shared\QuickLaunch.cs` creates the Start menu
shortcut/hotkey, and the manager's `/start` mode briefly launches the player and
exits. These changes require rebuilding the full installer so both programs match.

## Change the developer information and rebuild

1. Open `manager\DeveloperInfo.cs`. The comment **CUSTOMIZE DEVELOPER DETAILS HERE** marks the values to replace:
   - `Name`: used in the Developer tab and the bottom “Developed by” credit.
   - `Role`, `Bio`, `Email`, `Website`: used in the Developer tab. Use a full `https://` website address.
   - `Version`: the version label shown in the manager.
2. Save the file. Keep the double quotes around each C# string. Use `\"` for a quote inside your text and `\\` for a backslash.
3. Close CineSaver Manager before updating. Open PowerShell in this project folder.
4. Build the complete installer (a .NET SDK and the .NET Framework 4.8 targeting pack are required; restore may download dependencies on the first build):

```powershell
Set-Location 'C:\Users\HP\DEV\ScreensaverApp'
.\Build-Installer.ps1 -VideoPath 'C:\Windows\System32\screensaver.mp4'
```

You can substitute any MP4 path to change the **included default video**. Choosing a different video in Manager does not require rebuilding.

If PowerShell blocks local scripts, run this one command instead; it applies the execution-policy override only to this PowerShell process:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-Installer.ps1 -VideoPath 'C:\Windows\System32\screensaver.mp4'
```

5. The output is `dist\CineSaver-Setup.exe` and its SHA256 file. Run the new Setup and click **Install / Update**. This installs the rebuilt manager and player together; saved video and preferences are retained.
6. Open **Start → CineSaver → CineSaver Manager** to see your changes. Share only the Setup executable with another Windows x64 PC; that PC needs .NET Framework 4.8, but no SDK or VLC installation.

For a local compile check without packaging:

```powershell
dotnet build manager\Manager.csproj -c Release
dotnet build ScreensaverApp.csproj -c Release
```

The standalone manager uses the installed application's state. Use the full installer build to test changes to preview/player behavior together. To publish a new product version, update `AssemblyInfo.cs`, `manager\Manager.csproj`, `installer\Setup.csproj`, version strings in `installer\Setup.cs` and `App.xaml.cs`, and `DeveloperInfo.Version` consistently.
