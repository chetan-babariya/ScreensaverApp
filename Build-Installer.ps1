[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$VideoPath,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $PSScriptRoot 'dist' }
$video = (Resolve-Path -LiteralPath $VideoPath).Path
if ([IO.Path]::GetExtension($video) -ne '.mp4') { throw 'Choose an MP4 video.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
# An isolated build prevents stale DLLs, .src files, and x86 binaries entering the installer.
$stage = Join-Path $PSScriptRoot ('obj\package-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $stage 'app'
dotnet build (Join-Path $PSScriptRoot 'ScreensaverApp.csproj') -c Release -o $app --nologo
if ($LASTEXITCODE -ne 0) { throw 'Screensaver build failed.' }
$managerOutput = Join-Path $stage 'manager'
dotnet build (Join-Path $PSScriptRoot 'manager\Manager.csproj') -c Release -o $managerOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'Manager build failed.' }
Copy-Item -LiteralPath (Join-Path $managerOutput 'CineSaver.Manager.exe') -Destination $app
Copy-Item -LiteralPath (Join-Path $managerOutput 'CineSaver.Manager.exe.config') -Destination $app
Copy-Item -LiteralPath $video -Destination (Join-Path $app 'screensaver.mp4')
$config = Join-Path $app 'CineSaver.scr.config'
if (!(Test-Path -LiteralPath $config)) { throw 'The screensaver configuration file is missing.' }
# Retain only runtime files. Package only the x64 native folder selected by the project.
$runtimeFiles = @('CineSaver.scr', 'CineSaver.scr.config', 'screensaver.mp4', 'CineSaver.Manager.exe', 'CineSaver.Manager.exe.config')
Get-ChildItem -LiteralPath $app -File | Where-Object { $_.Name -notin $runtimeFiles -and $_.Extension -ne '.dll' } | Remove-Item -Force
$payload = Join-Path $stage 'payload.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($app, $payload, [IO.Compression.CompressionLevel]::Optimal, $false)
$setupOutput = Join-Path $stage 'setup'
dotnet build (Join-Path $PSScriptRoot 'installer\Setup.csproj') -c Release -o $setupOutput "-p:PayloadPath=$payload" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
$setup = Join-Path $output 'CineSaver-Setup.exe'
Copy-Item -LiteralPath (Join-Path $setupOutput 'CineSaver-Setup.exe') -Destination $setup -Force
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
Set-Content -LiteralPath (Join-Path $output 'CineSaver-Setup.sha256') -Value "$hash  CineSaver-Setup.exe" -Encoding ASCII
$size = [math]::Round((Get-Item -LiteralPath $setup).Length / 1MB, 1)
Write-Output "Created: $setup ($size MB). Double-click it and choose Install / Update."
