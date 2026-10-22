# 0012 — LibVLC playback: video popup plus a region-clipped WebView2 overlay

**Status:** accepted (M5)

§9.4 wants the libvlc engine to show the same controls as the html player, drawn by the disc UI over LibVLC's
picture. The launcher (`LauncherWindow.Playback.cs`) creates two owned popups over the menu's client area: one that
LibVLC renders into (`libdrawable`), and above it a second WebView2 that boots the SDK in overlay mode
(`ui/disc-sdk/src/overlay.ts`) and runs the shared `openPlayer` against a remote media object, which sends bridge
commands and is updated by the launcher's 250 ms `player` events.

A transparent WebView2 over another window does not composite. With `WS_EX_NOREDIRECTIONBITMAP` and an alpha-0
background the overlay is invisible but so is everything behind it. With a `LWA_COLORKEY` layered window the key
colour turns black instead of showing the video. Region clipping does work. The page reports the rectangles of
its visible panels (`overlay.region`, CSS px plus `devicePixelRatio`), and the launcher clips the overlay window to
their rounded union with `SetWindowRgn`. While the controls are hidden the region is empty and the whole picture
shows. Both engines therefore use the floating-panel look, and the libvlc engine paints the panels opaque
(`.remote`).

Since the clipped overlay cannot see the mouse over the picture, the launcher polls the cursor on its player timer
and posts `{"event":"wake"}`. The SDK reports panels synchronously and not from `requestAnimationFrame`, because
Chromium treats a fully clipped window as occluded and gives it no frames. Clicking the picture hands focus back
to the overlay so the keyboard keeps working, and media keys and XInput go through the same commands.

The disc carries only the LibVLC plugins its media needs (`LibVlcPlugins`: base set plus per container, codec and
subtitle format). They are verified per format by an AOT launcher probe (`--probe-media`,
`tests/Inlay.Launcher.Player.Tests`).
