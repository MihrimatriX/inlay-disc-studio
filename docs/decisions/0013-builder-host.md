# 0013 — Builder: WPF title bar, one WebView2, preview on the launcher's own origin

**Status:** accepted (M6)

**Window.** §11 asks for a 44 px custom title bar with Snap Layouts. A title bar drawn in HTML cannot offer
Snap Layouts. Windows shows the flyout only when the top-level window answers `WM_NCHITTEST` with `HTMAXBUTTON`,
and the WebView2 child (a window of another process) receives the mouse instead. So `MainWindow` draws the bar
natively in WPF: the disc icon, "Inlay / project", and caption buttons in Geist (embedded TTF, OFL). It answers
`HTMAXBUTTON` over the maximize button, and the React UI fills the rest. The page paints the same `bg` radial
light, offset by 44 px, so the two halves meet without a seam. That is also why the window keeps the `bg`
gradient instead of Mica: an opaque WebView2 covers the backdrop, and Mica would only show in the title strip.

**Protocol.** The page sends `{id, method, params}` and gets `{id, ok, result|error}` plus `{event, data}`
pushes (`state`, `job`). `Rpc.Call` maps methods onto `Session`, which can be tested without a window. The
host owns dialogs, dropped file paths (WebView2 additional objects), `iso.export` and the modal dimming of the
native bar. `Session` keeps the user's choices in `DiscProject` (`.discproj`, §7.4) and rebuilds only as far
back as a change needs: an edit renders again, a match composes again, a content change analyses again. Every
change pushes a fresh `View`, autosaves and is undoable.

**Preview.** §7.3: "the same render and an SDK stand-in, no fake browser preview". The builder's WebView2 does not
map `https://disc.local/`, so the host answers that origin through `WebResourceRequested`. It serves the
hosted `index.html` the launcher shows, with `/~preview/sdk.js` added first in `<head>` (the CSP's `'self'`
allows it). That script is the boot object with `preview: true` plus the same SDK, and the SDK then talks to
`previewTransport`, a stand-in launcher that answers `system`/`run`/`installBundle` and starts nothing. Disc
files come straight from their sources. Files a job would convert (frames, WebVTT, resized images) are made on
first request into `%LOCALAPPDATA%\Inlay\preview\<project>`. Thumbnails for the builder's own screens use
`https://preview.inlay/`. Video seeking works through byte ranges.

**Text and tokens.** Builder strings live in `i18n/*.json` under `builder.*`, the same tables the disc uses.
`ui/tokens.css` follows design-prompt §2.1 with one change: `faint` is `#84868f` rather than `#6d6f78`, because
the latter is 3.5:1 on the panels and §1.6 requires 4.5:1.

**Burning.** M6 ends at "ISO olarak kaydet", where the full chain runs (prepare, lay out, image). Writing to
a disc, verification and the Araçlar tools follow in M7.
