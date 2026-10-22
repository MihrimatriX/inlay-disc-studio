# Packs Inlay Disc Studio as an MSIX (§14): the builder (self-contained .NET 10), its React UI and templates, the
# NativeAOT launcher that goes on every disc, the LGPL FFmpeg build and LibVLC.
#
# Signing (§10): set INLAY_SIGN_PFX (and INLAY_SIGN_PASSWORD) to a code-signing certificate and the package and
# Baslat.exe are signed with a timestamp. Without one the MSIX is left unsigned and the script says so: Windows only
# installs MSIX packages signed by a certificate it trusts, and unsigned Baslat.exe files on discs get SmartScreen
# warnings. The publisher name in the manifest must match the certificate subject (INLAY_PUBLISHER).
#
# Prerequisites: npm run build, tools/ffmpeg (CI step), the launcher published to artifacts/launcher, the Windows SDK
# (makeappx/signtool). Usage: powershell -File scripts/package-builder.ps1 [-Version 0.1.0.0] [-LayoutOnly]
# -LayoutOnly stops once artifacts\builder is laid out (scripts/package-installer.ps1 zips it into InlaySetup.exe).
param([string] $Version = "0.1.0.0", [switch] $LayoutOnly)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$out = Join-Path $repo "artifacts\builder"
$msix = Join-Path $repo "artifacts\Inlay-$Version-x64.msix"
$publisher = if ($env:INLAY_PUBLISHER) { $env:INLAY_PUBLISHER } else { "CN=Inlay Disc Studio" }

$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $sdk -and -not $LayoutOnly) { throw "makeappx.exe not found: install the Windows 10/11 SDK" }
$signtool = if ($sdk) { Join-Path $sdk.DirectoryName "signtool.exe" } else { $null }

foreach ($need in @("ui\builder\dist\index.html", "artifacts\launcher\Baslat.exe", "tools\ffmpeg\bin\ffmpeg.exe", "tools\ffmpeg\LICENSE.txt")) {
    if (-not (Test-Path (Join-Path $repo $need))) { throw "$need is missing (see the prerequisites above)" }
}

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish (Join-Path $repo "src\Inlay.Builder") -c Release -r win-x64 --self-contained -o $out
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# What the builder looks for next to Inlay.exe: launcher\Baslat.exe, ffmpeg\, libvlc\.
New-Item -ItemType Directory -Force "$out\launcher", "$out\ffmpeg", "$out\libvlc", "$out\Assets" | Out-Null
Copy-Item "$repo\artifacts\launcher\Baslat.exe" "$out\launcher\"
Copy-Item "$repo\tools\ffmpeg\bin\*.exe", "$repo\tools\ffmpeg\bin\*.dll" "$out\ffmpeg\" -Exclude ffplay.exe
Copy-Item "$repo\tools\ffmpeg\LICENSE.txt" "$out\ffmpeg\"
$vlc = Get-ChildItem "$env:USERPROFILE\.nuget\packages\videolan.libvlc.windows\*\build\x64" -Directory | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $vlc) { throw "LibVLC not found in the NuGet cache (dotnet restore)" }
Copy-Item "$($vlc.FullName)\*" "$out\libvlc\" -Recurse
if ($LayoutOnly) { Remove-Item "$out\Assets"; Write-Host "Laid out: $out"; return }

foreach ($a in @(@{ n = "Square44x44Logo.png"; s = 44 }, @{ n = "Square150x150Logo.png"; s = 150 }, @{ n = "StoreLogo.png"; s = 50 })) {
    dotnet run (Join-Path $repo "scripts\gen-app-icon.cs") -- --png $a.s (Join-Path "$out\Assets" $a.n)
}

@"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap rescap">
  <Identity Name="InlayDiscStudio" Publisher="$publisher" Version="$Version" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>Inlay Disc Studio</DisplayName>
    <PublisherDisplayName>Inlay Disc Studio</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19045.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Resources>
    <Resource Language="tr-TR" />
    <Resource Language="en-US" />
  </Resources>
  <Applications>
    <Application Id="Inlay" Executable="Inlay.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="Inlay Disc Studio" Description="Kendi menüsüyle açılan diskler hazırla ve yaz"
        BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png" />
      <Extensions>
        <uap:Extension Category="windows.fileTypeAssociation">
          <uap:FileTypeAssociation Name="discproj">
            <uap:DisplayName>Inlay projesi</uap:DisplayName>
            <uap:SupportedFileTypes><uap:FileType>.discproj</uap:FileType></uap:SupportedFileTypes>
          </uap:FileTypeAssociation>
        </uap:Extension>
      </Extensions>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@ | Set-Content -Encoding UTF8 "$out\AppxManifest.xml"

if ($env:INLAY_SIGN_PFX) {
    & $signtool sign /fd SHA256 /f $env:INLAY_SIGN_PFX /p $env:INLAY_SIGN_PASSWORD /tr http://timestamp.digicert.com /td SHA256 "$out\launcher\Baslat.exe" "$out\Inlay.exe"
    if ($LASTEXITCODE -ne 0) { throw "signing the executables failed" }
}
if (Test-Path $msix) { Remove-Item $msix }
& $sdk.FullName pack /o /d $out /p $msix
if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }

if ($env:INLAY_SIGN_PFX) {
    & $signtool sign /fd SHA256 /f $env:INLAY_SIGN_PFX /p $env:INLAY_SIGN_PASSWORD /tr http://timestamp.digicert.com /td SHA256 $msix
    if ($LASTEXITCODE -ne 0) { throw "signing the package failed" }
    Write-Host "Signed: $msix"
} else {
    Write-Warning "UNSIGNED: $msix. Windows will not install it until it is signed with a trusted certificate (set INLAY_SIGN_PFX). Baslat.exe is unsigned too, so discs made with this build show a SmartScreen warning."
}
