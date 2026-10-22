# Builds InlaySetup-<version>.exe: a NativeAOT installer (src/Inlay.Setup) carrying the zipped builder layout.
# It installs per user (no administrator rights) to %LOCALAPPDATA%\Programs\Inlay Disc Studio, adds Start menu and
# desktop shortcuts, the .discproj association and an entry in Settings › Apps; Uninstall.exe (the same program
# without the payload) removes it again.
#
# Unsigned, Windows SmartScreen asks once ("More info" → "Run anyway"). With INLAY_SIGN_PFX / INLAY_SIGN_PASSWORD
# set, the setup, the uninstaller, Inlay.exe and Baslat.exe are signed with a timestamp.
#
# Prerequisites as for scripts/package-builder.ps1. Usage: powershell -File scripts/package-installer.ps1 [-Version 0.1.0]
param([string] $Version = "0.1.0")
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

& (Join-Path $PSScriptRoot "package-builder.ps1") -Version "$Version.0" -LayoutOnly
Sign @("$layout\Inlay.exe", "$layout\launcher\Baslat.exe")

if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null

# 1. The uninstaller: the setup program without a payload, placed in the layout.
dotnet publish (Join-Path $repo "src\Inlay.Setup") -c Release -r win-x64 -p:Version=$Version -o "$work\uninstall"
if ($LASTEXITCODE -ne 0) { throw "publish (uninstaller) failed" }
Copy-Item "$work\uninstall\InlaySetup.exe" "$layout\Uninstall.exe"
Sign @("$layout\Uninstall.exe")

# 2. The payload.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = Join-Path $work "payload.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($layout, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

# 3. The setup with the payload embedded.
dotnet publish (Join-Path $repo "src\Inlay.Setup") -c Release -r win-x64 -p:Version=$Version -p:InlayPayload=$zip -o "$work\setup"
if ($LASTEXITCODE -ne 0) { throw "publish (setup) failed" }
Copy-Item "$work\setup\InlaySetup.exe" $setup -Force
Sign @($setup)

$mb = [math]::Round((Get-Item $setup).Length / 1MB)
if ($env:INLAY_SIGN_PFX) { Write-Host "Signed: $setup ($mb MB)" }
else { Write-Warning "UNSIGNED: $setup ($mb MB). Windows SmartScreen will ask before running it (More info, Run anyway)." }
