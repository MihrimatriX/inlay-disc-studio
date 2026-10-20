# Contract changes

Changes to `schemas/`, `sdk/disc-sdk.d.ts` or the page ↔ launcher protocol. Schema `$id`/`version` stay at v1 while
every change is additive and optional for consumers.

## M2

- `SystemInfo.memoryBytes` (SDK, additive): total physical memory. The game template's compatibility line compares it
  with Steam's RAM requirement (§7.2); without it the line could only ever show disk space.

## M4

- **CSP** gains `style-src 'self' 'unsafe-inline' https://disc.local`. The §10 policy has no style-src, so
  default-src would block the `:root{--ds-…}` block §7.1.4 injects, every inline style of the design references and
  the SDK player's styles. Network isolation is unchanged: no external origin is allowed, and the launcher answers
  every off-disc request with 403 (ADR 0007).
- **SDK** `play(id)` also accepts the disc path of a movie's trailer or extra (they have no id in the schema);
  `playlist(albumId, startTrackId?, { shuffle? })` gains an options argument. Both additive.
- **SDK** html video played without an element opens the shared full-screen player (§9.4) instead of being
  rejected.
- **Schema** `app.logo` (relPath, optional): the transparent SteamGridDB logo §5.3 says the template shows
  instead of the title. Movies and series already had `logo`; apps did not.

## M5

- **Bridge** `play` accepts `opts.original: true`: play the item's original file through LibVLC. The SDK sends it when
  WebView2 cannot decode an html-engine file whose `playback.fallback` is `libvlc` (§6).
- **Bridge** `player.audio` / `player.subtitle` take `{ id }` (LibVLC track id, `-1` turns subtitles off). The
  launcher's `player` events for LibVLC playback also carry `audio` and `subtitles` track lists. Both are additive;
  pages that ignore them are unaffected.
- **Overlay protocol** (launcher ↔ its own overlay page only, not exposed to themes): `overlay.region`
  `{ rects: {x,y,w,h,r}[], scale }` and the launcher event `wake` (ADR 0012).
- **SDK** LibVLC playback saves watch progress from the `player` events like the html engine (`disc.progress`).

## M6

- **Schema** `disc.autoplay` (bool, default false): "Disk takılınca › Hemen oynat". The standard runtime presses the
  featured item's primary action at start; custom templates may read it the same way.
- **Schema** `disc.remote` (bool, default true): "Kumanda ile gezinme". When false the standard runtime ignores pads
  and the launcher stops forwarding XInput `gamepad` events.
- **SDK boot** `__inlayBoot.preview: true` (builder only): the SDK talks to a stand-in launcher (ADR 0013) instead of
  `chrome.webview`. Discs never carry it.

## After M7

- **SDK/Bridge** `run(id)` of an install action may answer `canRunInstalled: true`: the installer succeeded and the
  launcher found the app's installed copy in the Uninstall keys (HKLM/HKCU, 64- and 32-bit views; DisplayName must
  contain the app title, a differing Publisher rules an entry out; the exe is DisplayIcon or the best exe in
  InstallLocation). New command `runInstalled` / `disc.runInstalled(appId)` starts that exe. The page passes only the
  app id; the path never leaves the launcher. The standard runtime uses it so "Oyna" works on installer-only game
  discs (§7.0). Both additive; the manifest schema is unchanged.
