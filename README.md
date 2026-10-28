# Inlay Disc Studio

*Film, müzik, fotoğraf, oyun ve program klasörlerinden kendi menüsüyle açılan CD/DVD'ler hazırlayan Windows uygulaması.
Kullanıcı rehberi: [docs/user-guide.md](docs/user-guide.md).*

Inlay turns folders into discs that open with their own full-screen menu. The **builder** (`Inlay.exe`, WPF +
WebView2 + React) analyses the content, finds metadata, prepares media so it plays everywhere, renders a Liquid
template and burns or images the disc. Every disc carries **`Baslat.exe`**, a ~9 MB NativeAOT launcher that shows
the menu in WebView2, plays video in the page or through LibVLC, and runs only the actions in the disc's manifest.

The product spec is [docs/app-prompt.md](docs/app-prompt.md), the visual spec is [docs/design-prompt.md](docs/design-prompt.md),
decisions are in [docs/decisions/](docs/decisions), and the history is in [CHANGELOG.md](CHANGELOG.md).

## Layout

```
schemas/  samples/  sdk/          contract: manifest + theme schemas, sample manifests, page SDK types
src/Inlay.Core                    manifest/theme models, paths, labels, autorun, i18n
src/Inlay.Analysis                scan → classify → metadata (nfo, TMDB, OMDb, fanart.tv, IGDB, Steam, SteamGridDB) → DiscDraft → files
src/Inlay.Media                   ffprobe/FFmpeg, normalization planner, images, LibVLC plugin pruning
src/Inlay.Theming                 theme packages, Fluid render, lint, palette
src/Inlay.Imaging                 disc/app icons
src/Inlay.Burn                    IMAPI2: ISO images, burning, verification, erase, raw disc read
src/Inlay.Builder                 WPF host, Session engine, .discproj, RPC
src/Inlay.Launcher(.Player)       Baslat.exe (NativeAOT, WebView2, LibVLC)
ui/builder  ui/editor  ui/disc-sdk  builder UI (React 19/Vite), CodeMirror editor, window.disc
themes/                           the five standard templates over themes/_shared
i18n/                             tr-TR / en-US text for discs and the builder
tests/                            xUnit v3 per project + Vitest
scripts/                          generators, screenshots, probes, packaging
```

## Prerequisites

| What | Version / where | Notes |
|---|---|---|
| Windows | 10 22H2 or 11 | IMAPI2 and WebView2 are Windows-only |
| .NET SDK | 10.0.402 (`global.json`) | |
| Node.js | 22 or newer | `npm ci` installs the exact versions in `package-lock.json` |
| Visual Studio Build Tools | "Desktop development with C++" | NativeAOT links `Baslat.exe` with the MSVC linker; `vswhere.exe` must be on `PATH` (see below) |
| WebView2 Runtime | evergreen | preinstalled on Windows 11 |
| FFmpeg | BtbN `ffmpeg-n8.1-latest-win64-lgpl-shared-8.1` in `tools/ffmpeg` | or set `INLAY_FFMPEG_DIR` to a folder with `ffmpeg.exe`/`ffprobe.exe`. **LGPL build only** (ADR 0008) |
| LibVLC | NuGet `VideoLAN.LibVLC.Windows` 3.0.23 | restored to `%USERPROFILE%\.nuget\packages` by `dotnet restore`; or set `INLAY_LIBVLC_DIR` |
| Windows SDK | 10.0.26100 | only for signing the installer (`signtool`) |

FFmpeg, as CI fetches it:

```bash
gh release download latest --repo BtbN/FFmpeg-Builds -p 'ffmpeg-n8.1-latest-win64-lgpl-shared-8.1.zip' -p checksums.sha256 -D tools/dl
```

Then check the zip against `checksums.sha256` and unzip it so that `tools/ffmpeg/bin/ffmpeg.exe` exists.

### Metadata keys (optional)

Entered in the builder under **Ayarlar › Çevrimiçi bilgiler** and stored with DPAPI in `%LOCALAPPDATA%\Inlay\keys.dat`.
Without them Inlay still works from `.nfo` files, the Steam store and what the user types.

- **TMDB** API key (v3): films and series. The TMDB attribution goes on every disc that uses it.
- **OMDb** API key (omdbapi.com): IMDb, Rotten Tomatoes and Metacritic scores for a TMDB match, by its IMDb id. OMDb's
  data is CC BY-NC 4.0, so its credit goes on the disc.
- **fanart.tv** API key: the logo, backdrop or poster TMDB has none of (movies by TMDB id, shows by TheTVDB id).
- **IGDB**: a Twitch application's **Client ID** and **Client Secret** (dev.twitch.tv/console).
- **SteamGridDB** API key: game logos and hero images.

Each service has a **Test et** button that sends one uncached request with the saved key (`keys.test`): a 400/401/403
answer reads as a refused key, anything else as an unreachable service.

## Build and run

```bash
npm ci
```

```bash
npm run build
```

This builds the page SDK (embedded in the launcher and the builder) and the builder UI. Then publish the
launcher from PowerShell, adding vswhere for the NativeAOT linker:

```powershell
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"; dotnet publish src/Inlay.Launcher -c Release -r win-x64 -o artifacts/launcher
```

```bash
dotnet run --project src/Inlay.Builder
```

`INLAY_DEVTOOLS=1` enables the builder's WebView2 DevTools. `INLAY_ROOT` moves its data folder (prefs, cache,
work, autosaves) away from `%LOCALAPPDATA%\Inlay`.

Headless (scripts, CI, the sample ISO):

```bash
dotnet run --project src/Inlay.Builder -- --iso out.iso --title "Disk adı" path/to/folder
```

## Tests

```bash
dotnet test
```

```bash
npm test
```

Some tests open real windows (launcher, LibVLC, builder over CDP) or mount ISOs. They skip themselves on CI or
when the launcher, FFmpeg or LibVLC are missing, and should be run in a desktop session. Media fixtures are
synthesized with FFmpeg into `%TEMP%\inlay-media-fixtures`; nothing binary is committed.

Visual checks: `node scripts/builder-shot.mjs` (builder screens, axe-core), `node scripts/screenshot.mjs`
(disc templates in the real launcher), and `INLAY_KEEP_SHOT=<png>` for the LibVLC overlay test. Captures come
from the page over CDP or from the app's own windows (`scripts/window-shot.ps1`, PrintWindow), never from the screen.

## Sample project

```bash
node scripts/make-sample.mjs artifacts/sample/src
```

This writes two source folders: a film disc, and the mixed collection of `samples/manifest.mixed.json`
(film, album, photos and manual; its two programs need real installers and are left out). Their ISOs:

```powershell
dotnet run --project src/Inlay.Builder -- --iso artifacts/sample/Koleksiyon.iso --title "Side project GAMES — Koleksiyon" (Get-ChildItem artifacts/sample/src/Koleksiyon).FullName
```

## Installer

```powershell
powershell -ExecutionPolicy Bypass -File scripts/package-installer.ps1
```

The version comes from `Directory.Build.props` (or `-Version`). The result is `artifacts/InlaySetup-<version>.exe`
(~200 MB), a NativeAOT installer (`src/Inlay.Setup`) with the self-contained builder, its UI and templates, the launcher,
LGPL FFmpeg and LibVLC inside. Set `INLAY_SIGN_PFX` / `INLAY_SIGN_PASSWORD` to sign the setup, `Inlay.exe` and
`Baslat.exe` with a timestamp. **Without a certificate everything is unsigned:** SmartScreen asks before the setup
runs, and discs made with an unsigned `Baslat.exe` show a SmartScreen warning (the builder says so under Ayarlar ›
Gelişmiş). It installs per user, with no administrator rights, to `%LOCALAPPDATA%\Programs\Inlay Disc Studio`. It adds Start menu and
desktop shortcuts, the `.discproj` association and an entry in Settings › Apps (`Uninstall.exe`). An update keeps the old
version until the new one is fully unpacked. `--quiet [--dir X] [--no-register]` installs without dialogs, and `--uninstall [--quiet]`
removes. A tag `vX.Y.Z` runs `.github/workflows/release.yml`, which builds it and attaches it to a GitHub release as `InlaySetup.exe`.

## Website

`site/` is the product page. `node scripts/build-site.mjs --repo owner/name` builds it into `artifacts/site`, and
`.github/workflows/pages.yml` publishes it to GitHub Pages on every push to `main` that touches it. Its download button
points at the latest release's `InlaySetup.exe`.

## Licences

Inlay's own code is not yet under a published licence. Third-party components it ships:

- FFmpeg (BtbN LGPL build; its LICENSE.txt ships beside the binaries)
- LibVLC (LGPL-2.1-or-later, dynamically linked, notice and text on each disc that uses it)
- Outfit and Geist fonts (SIL OFL 1.1)
- WebView2 (Microsoft)

Test-only dependencies: FluentAssertions 7 (Apache-2.0; v8 is commercial, ADR 0002) and xUnit.
