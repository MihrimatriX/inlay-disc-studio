# Builds InlaySetup-<version>.exe: a NativeAOT installer (src/Inlay.Setup) carrying the zipped builder layout: the
# builder (self-contained .NET 10), its React UI and templates, the NativeAOT launcher that goes on every disc, the LGPL
# FFmpeg build and LibVLC. It installs per user (no administrator rights) to %LOCALAPPDATA%\Programs\Inlay Disc Studio,
# adds Start menu and desktop shortcuts, the .discproj association and an entry in Settings › Apps; Uninstall.exe (the
# same program without the payload) removes it again.
#
# Unsigned, Windows SmartScreen asks once ("More info" → "Run anyway"). With INLAY_SIGN_PFX / INLAY_SIGN_PASSWORD
# set, the setup, the uninstaller, Inlay.exe and Baslat.exe are signed with a timestamp (signtool, Windows SDK).
#
# Prerequisites: npm run build, tools/ffmpeg (CI step), the launcher published to artifacts/launcher, dotnet restore
# (LibVLC comes from the NuGet cache). Usage: powershell -File scripts/package-installer.ps1 [-Version 0.3.0]
# (default: <Version> in Directory.Build.props)
param([string] $Version = ([xml](Get-Content (Join-Path $PSScriptRoot "..\Directory.Build.props"))).Project.PropertyGroup.Version)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$layout = Join-Path $repo "artifacts\builder"
$work = Join-Path $repo "artifacts\setup"
$setup = Join-Path $repo "artifacts\InlaySetup-$Version.exe"
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer" # NativeAOT finds the MSVC linker through vswhere

function Sign([string[]] $files) {
    if (-not $env:INLAY_SIGN_PFX) { return }
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName -Descending | Select-Object -First 1
    & $signtool.FullName sign /fd SHA256 /f $env:INLAY_SIGN_PFX /p $env:INLAY_SIGN_PASSWORD /tr http://timestamp.digicert.com /td SHA256 @files
    if ($LASTEXITCODE -ne 0) { throw "signing failed" }
}

foreach ($need in @("ui\builder\dist\index.html", "artifacts\launcher\Baslat.exe", "tools\ffmpeg\bin\ffmpeg.exe", "tools\ffmpeg\LICENSE.txt")) {
    if (-not (Test-Path (Join-Path $repo $need))) { throw "$need is missing (see the prerequisites above)" }
}

# 1. The layout: what the builder looks for next to Inlay.exe is launcher\Baslat.exe, ffmpeg\ and libvlc\.
if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }
dotnet publish (Join-Path $repo "src\Inlay.Builder") -c Release -r win-x64 --self-contained -p:Version=$Version -o $layout
if ($LASTEXITCODE -ne 0) { throw "publish (builder) failed" }
New-Item -ItemType Directory -Force "$layout\launcher", "$layout\ffmpeg", "$layout\libvlc" | Out-Null
Copy-Item "$repo\artifacts\launcher\Baslat.exe" "$layout\launcher\"
Copy-Item "$repo\tools\ffmpeg\bin\*.exe", "$repo\tools\ffmpeg\bin\*.dll" "$layout\ffmpeg\" -Exclude ffplay.exe
Copy-Item "$repo\tools\ffmpeg\LICENSE.txt" "$layout\ffmpeg\"
$vlc = Get-ChildItem "$env:USERPROFILE\.nuget\packages\videolan.libvlc.windows\*\build\x64" -Directory | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $vlc) { throw "LibVLC not found in the NuGet cache (dotnet restore)" }
Copy-Item "$($vlc.FullName)\*" "$layout\libvlc\" -Recurse
Sign @("$layout\Inlay.exe", "$layout\launcher\Baslat.exe")

if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null

# 2. The uninstaller: the setup program without a payload, placed in the layout.
dotnet publish (Join-Path $repo "src\Inlay.Setup") -c Release -r win-x64 -p:Version=$Version -o "$work\uninstall"
if ($LASTEXITCODE -ne 0) { throw "publish (uninstaller) failed" }
Copy-Item "$work\uninstall\InlaySetup.exe" "$layout\Uninstall.exe"
Sign @("$layout\Uninstall.exe")

# 3. The payload.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $work "payload.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($layout, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

# 4. The setup with the payload embedded.
dotnet publish (Join-Path $repo "src\Inlay.Setup") -c Release -r win-x64 -p:Version=$Version -p:InlayPayload=$zip -o "$work\setup"
if ($LASTEXITCODE -ne 0) { throw "publish (setup) failed" }
Copy-Item "$work\setup\InlaySetup.exe" $setup -Force
Sign @($setup)

$mb = [math]::Round((Get-Item $setup).Length / 1MB)
if ($env:INLAY_SIGN_PFX) { Write-Host "Signed: $setup ($mb MB)" }
else { Write-Warning "UNSIGNED: $setup ($mb MB). Windows SmartScreen will ask before running it (More info, Run anyway)." }
