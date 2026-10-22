# Changelog

## Media is not converted by default

- `NormalizationPlanner`: with "Maksimum uyumluluk" off (the default), audio and video go on the disc untouched; WebView2
  plays what it can and LibVLC the rest. The §6.1 remux/faststart/AAC/FLAC/transcode steps now run only with the option on.

## Installer and website

- `src/Inlay.Setup` → `InlaySetup.exe`: NativeAOT, task-dialog UI in Turkish, per-user install (no admin) with Start
  menu/desktop shortcuts, `.discproj` association, Settings › Apps entry and `Uninstall.exe`. An update unpacks next to the
  old version and swaps folders at the end, and the user data in `%LOCALAPPDATA%\Inlay` is kept unless the user asks.
  `scripts/package-installer.ps1` builds it (`package-builder.ps1 -LayoutOnly` lays out the builder).
- `site/` + `scripts/build-site.mjs`: the Turkish product page (screens, disc templates, how to try, FAQ), the builder's
  tokens and Geist, no external requests; axe-clean at desktop and phone width.
- `.github/workflows/pages.yml` (GitHub Pages) and `release.yml` (tag → InlaySetup.exe on a release, signed when the
  certificate secrets are set).

## Deliverables (§14)

- `README.md` (developer setup, keys, build, tests, sample, packaging), `docs/user-guide.md` (Turkish, with screenshots in
  `docs/img`), `docs/theme-authoring.md` (now with `disc.autoplay`/`disc.remote` and the preview).
- `Inlay.exe --iso out.iso [--title …] sources…|project.discproj`: the full pipeline without a window.
- `scripts/make-sample.mjs`: the film disc and the mixed collection of `samples/manifest.mixed.json` (film, album,
  photos, manual); both ISOs build headlessly (`artifacts/sample`, not committed).
- `scripts/package-builder.ps1`: MSIX (self-contained builder, UI, templates, launcher, LGPL FFmpeg, LibVLC, .discproj
  association); signs when a certificate is given, otherwise warns that it is unsigned. Ayarlar › Gelişmiş warns when
  the launcher that goes on discs is unsigned (§10).
- M8 (CD-Extra) stays out, as decided (ADR 0005).

## M7 — Burning and verification

- `Burner` (IMAPI2, ADR 0014): blank/size/medium checks with the real free space, exclusive access, recommended
  or slowest speed, progress and drive speed from `DDiscFormat2DataEvents`, cancel, closed media, sleep blocked,
  one write at a time; IMAPI2 errors mapped to no disc / not blank / too small / buffer underrun / busy /
  write-protected / unsupported.
- Verification: SHA-256 before writing, read back after an eject/reload, report with mismatched and missing files
  and a read-speed graph; ISO burns compare the data area sector by sector.
- Araçlar: ISO kalıbı yaz, Diski sil (quick erase of CD-RW/DVD-RW with confirmation), Disk kopyala (raw read of a
  data disc to an image, then burn; ADR 0004).
- Builder: "Diske yaz" / "Disk tak" (opens the tray) / "ISO olarak kaydet" per §3.5, "Takılı disk bu içerik için
  küçük", the burn dialog with "Bitince kontrol et ve diski çıkar" (changeable while writing), x speed, IMAPI2 time
  estimate, "Disk hazır" + "Bir kopya daha" (no second preparation), calm error texts; drive list refreshes on
  WM_DEVICECHANGE; Gelişmiş: recorder, speed, verification.
- `scripts/window-shot.ps1`: window-only captures through PrintWindow (popups and window regions included), used
  by the LibVLC probe, so nothing else on the screen can reach a capture.
- Tests: speed choice, error table, verification and disc copy against a mounted ISO, recorder choice and drive
  text; the physical burns are on `docs/burn-checklist.md` (no recorder on the development machine).

## M6 — Builder UI and editor

- `Inlay.Builder` (WPF + WebView2, `Inlay.exe`): native 44 px title bar with Snap Layouts on the maximize button, dark
  frame, Geist; the React 19 / Vite UI from `ui/builder`; dialogs, folder/file drop, preview origin, ISO export (ADR 0013).
- `Session` engine: `.discproj` (sources, removals, kind corrections, matches, renames, menu and template choices,
  embedded user template), staged rebuilds, undo/redo, autosave, relink of moved sources; TMDB/IGDB/Steam/SteamGridDB
  lookups with inline "Bu film hangisi?" cards; capacity suggestions; drive detection (`Recorders`, IMAPI2).
- Screens matching `design/screens/01–03` and the 04 dialog: Başlangıç (kinds, drop, drive capsule, recent projects),
  Disk içeriği (table, parts, notes, context-menu correction, F2 rename, Delete, ruler with CD/DVD/DVD DL marks and
  suggestions), Disk menüsü (live preview, template cards incl. variants, Disk takılınca, name, icon, remote control,
  generated settings form, sections editor), progress dialog, Ayarlar with Gelişmiş and API keys (DPAPI).
- Code mode: CodeMirror 6 (lazy chunk, `ui/editor`) with Liquid highlighting and variable/filter completion, the live
  render beside it, data tree that inserts `{{ items['id'].title }}`, lint panel; copy-and-edit of a standard
  template, `.disctheme` import/export; lint errors block the disc.
- Preview is the hosted render on `https://disc.local/` with the SDK on a stand-in launcher (`ui/disc-sdk/src/preview.ts`).
- Schema: `disc.autoplay`, `disc.remote` (standard runtime and launcher honour them). Gamepad input is no longer
  handled twice when the Gamepad API also sees the pad.
- i18n: all builder text under `builder.*` in both languages. `ui/tokens.css` (faint raised to 4.5:1).
- Tests: engine (rows, menu edits, sections, corrections, removal, rename, copy-and-edit + lint blocking, .disctheme
  round trip, .discproj reopen, preview origin, RPC, ISO export); UI helpers and i18n parity (Vitest); screens captured
  from the page over CDP with axe-core clean on all of them (`scripts/builder-shot.mjs`, `docs/builder-checklist.md`).
- `scripts/make-sample.mjs`: the sample film folder (synthesized media, design/img art). CI now builds the SDK and UI.

## M5 — LibVLC

- `Inlay.Launcher.Player`: LibVLCSharp 3.10.2 session (AOT-safe), LibVLC 3.0.23 loaded from the disc's
  `.disc/player`; play/pause/seek/volume/audio and subtitle tracks, external subtitle slaves.
- Launcher: libvlc engine in a video popup over the menu, the shared controls in a WebView2 overlay clipped to its
  panels (ADR 0012), `player.*` commands, 250 ms state events, media keys (WM_APPCOMMAND) and XInput for both
  engines, full screen, `--probe-media` headless check.
- `LibVlcPlugins`: per-disc plugin pruning by container, codec and subtitle format; `DiscOutput` ships the pruned
  player with the LGPL notice and text and raises `libvlc-missing` when the runtime is absent.
- SDK: overlay mode, html → LibVLC fallback on decode failure, progress for LibVLC playback, one player look for
  both engines (contract-changes.md).
- Tests: pruned plugin set plays each container/codec headless through the AOT launcher (TTA and Ogg Vorbis
  stay on the html engine, see PruningTests); HEVC + DTS film end to end — analysis picks libvlc, disc carries
  the pruned runtime, the launcher plays it with the overlay controls, closing stops playback. APE has no sample
  in the fixtures (FFmpeg cannot encode it), so it is covered by the plugin list only.

## M4 — Templates

- `Inlay.Theming`: theme packages from folders (with the shared layer) or `.disctheme` zips (schema-checked,
  zip-slip refused, 64 MB cap), settings resolution (auto colours from the palette), Fluid render with a data-only
  model and step/recursion limits, filters `duration`/`clock`/`filesize`/`relurl`/`t`/`json`, CSS variables and
  the CSP meta tag injected, unhosted `Menu.html` render with the SDK inline, §7.1.5 lint.
- Five standard templates (`themes/standard-*`) over shared views and `ds.js` (ADR 0010): Sinema (+ Sade), dizi,
  film koleksiyonu, Albüm with the music bar, Galeri with mosaic and Ken Burns slideshow, Oyun with the
  compatibility line, program paketi, belge, karışık ana menü, Hakkında. Outfit embedded (OFL).
- SDK: shared full-screen film player, watch progress (`disc.progress`), shuffle, trailer/extra playback by disc path
  (ADR 0011, contract-changes.md). Schema: `app.logo`. CSP: `style-src 'self' 'unsafe-inline'`.
- `DiscOutput`: manifest, rendered UI, Menu.html, disc icon, autorun.inf, licenses and Baslat.exe on top of the
  materialized content.
- Tests: filters, settings, packages, every standard theme × every sample without lint errors, escaping, lint rules,
  golden HTML (`tests/golden`), and the full chain — folder → analysis → ISO → mounted disc → launcher → menu
  (fonts from the disc, film plays in the shared player).
- Verified on the reference discs in the real launcher (`scripts/screenshot.mjs`): screenshots match
  `design/screens/t1–t4`, every control reachable by arrow keys, axe-core WCAG A/AA clean; Lighthouse
  accessibility 100 (`docs/template-checklist.md`). Theme authoring guide: `docs/theme-authoring.md`.

## M3 — Analysis + normalization

- `Inlay.Media`: ffprobe model (languages to BCP 47 incl. ISO 639-2/B, HDR10/HLG/Dolby Vision), the §6.1
  `NormalizationPlanner` (pure), FFmpeg arguments per plan (faststart, MKV→MP4 remux, default AAC stereo track,
  copies of AC3/E-AC3/DTS, OpenH264/NVENC/QSV/AMF transcode with HDR tone mapping, FLAC/Opus), WebVTT and frame
  grabs, `FfmpegQueue` (bounded parallelism, `-progress` reporting, cancel kills and deletes), MP4 `moov` check,
  image pipeline (≤ 2560 px display copy, AVIF for HEIC/RAW/TIFF/PSD, 400 px WebP thumbnails, width-limited
  artwork), BlurHash encoder.
- `Inlay.Theming`: OKLab k-means palette with WCAG guarantees (text/bg ≥ 7, accent/bg ≥ 3, accentText/accent ≥ 4.5).
- `Inlay.Analysis`: scanner (§5.1), name parsers (release names, S01E01/1x01/Sezon/Bölüm, game installer noise,
  ids, Joliet-safe segments), §5.2 classifiers with confidence and user corrections, §7.0 `OpeningView`,
  local metadata (TagLib, EXIF incl. offset and GPS, Kodi .nfo, PE/MSI version info, 256 px icons, Authenticode,
  prerequisites and their silent switches), online providers (TMDB movie/TV with Turkish-first logo and
  certification, IGDB with Twitch token cache and 4 req/s bucket, Steam store with Turkish/English requirement
  parsing, SteamGridDB, game chain with §5.3 hero priority and attribution), 30-day response cache, DPAPI key store,
  `Composer` → `DiscDraft` (ADR 0009), `Materializer`, capacity suggestions.
- Tests: every §6.1 row on synthesized media (cached in %TEMP%, none committed); bit-exact MKV→MP4 remux, lossless
  FLAC conversion, DTS/AC3 copies, HDR→SDR transcode, cancellation; every §7.0 row; classifier scenarios; providers
  against recorded responses; a mixed drop composed into a schema-valid manifest and materialized.
  Not synthesizable here and covered by probe JSON instead: APE, PGS, DSD, Musepack; HEIC/CR2 decoding is checked
  as a codec capability.
- ADRs 0008 (H.264 via OpenH264 under the LGPL build), 0009 (DiscDraft).

## M2 — Launcher

- `Inlay.Launcher` → `Baslat.exe`: NativeAOT, 8.7 MB single file, Win32 window (Per-Monitor-V2, dark title bar,
  Mica), single instance per disc, WebView2 profile under `%LOCALAPPDATA%\Inlay\Launcher\<disc>`, F11 / HTML
  fullscreen, disc icon as window icon, no-WebView2 task dialog (open Menu.html / download the Runtime).
- WebView2 via generated `[GeneratedComInterface]` bindings + statically linked loader (ADR 0006).
- `https://disc.local` virtual host, SDK injected before page scripts, navigation outside disc.local cancelled,
  new windows blocked, every non-disc request answered 403 (ADR 0007), manifest checked against the embedded schema.
- Bridge (`{v,req,cmd,id,opts}`): run/install/open/openFolder/url with allow-list, path-escape guard, confirm text,
  UAC (`runas`, MSI via msiexec), busy, action-started/exited, closeLauncher, exit 3010 → reboot-required;
  installBundle through one elevated `--bundle-worker` (named-pipe progress), prerequisites before "Kur";
  system (incl. `memoryBytes`, see contract-changes.md), setFullscreen, close. Shell-engine media opens in the
  default app; libvlc playback lands in M5.
- `ui/disc-sdk`: `window.disc` (5 KB), html playback in page elements, shared audio queue for playlists,
  slideshow hand-off to the template, unhosted (Menu.html) fallbacks.
- Fixed `RelPath.Resolve` at drive roots (`E:\`), found by the integration test.
- Tests: launcher units (fake shell), bridge protocol, schema-checked loading, SDK (Vitest), and the §12
  integration test: ISO → mount → Baslat.exe from the disc → Playwright over CDP (allow-list, html video plays).
  The elevated bundle worker needs a real UAC prompt and is checked manually.

## M1 — ISO + autorun

- `Inlay.Core`: `MediaCapacity` (§8.4 table, 1.5 % file system share), `DiscLabel` (Turkish-safe A–Z0–9_ labels),
  `DiscFileSystem` (UDF only when a file is ≥ 4 GiB, Joliet name check), `Autorun` (UTF-16 autorun.inf, AutoPlay text
  from `disc.home`), `RelPath` (schema-equal validation + root-escape guard), `Text` (shared `i18n/*.json`, tr-TR/en-US).
- `Inlay.Imaging`: `DiscIcon` — 16–256 px ICO, 256 px frame as PNG; user icon > rounded cover > default disc.
- `Inlay.Burn`: `DiscImage` — IMAPI2FS image (ISO9660+Joliet+UDF 2.01 or UDF only), hidden `.disc`, ISO export
  with progress and cancellation.
- Tests mount the ISO: label, hidden `.disc`, Explorer icon from autorun.inf; opt-in 4 GiB UDF-only test (`INLAY_SLOW_TESTS=1`).
- ADRs 0003 (late-bound IMAPI2), 0004 (disc copy in scope), 0005 (CD-Extra deferred).

## M0 — Skeleton

- Repo layout per `docs/app-prompt.md` §3: `schemas/`, `samples/`, `sdk/`, `design/` moved to the root.
- .NET 10 solution (`Inlay.slnx`), central package management, Microsoft.Testing.Platform runner.
- `Inlay.Core`: manifest and theme contract records + AOT-safe `InlayJson` source-generated context.
- Contract tests (xUnit): samples round-trip losslessly and validate (JsonSchema.Net); strict deserialization.
- Contract tests (Vitest + Ajv 2020): samples validate; path traversal, non-https url action and unknown fields are rejected.
- `sdk/manifest.ts` is regenerated by `npm run gen:types` (json-schema-to-typescript 16); `home` is now fully typed. A test fails if it is stale.
- Typecheck covers `sdk/`, `tests/` and `scripts/` (root `tsconfig.json`, bundler resolution like Vitest).
- CI: GitHub Actions on windows-latest.
- ADRs 0001 (hand-written models), 0002 (FluentAssertions 7.2.x).
