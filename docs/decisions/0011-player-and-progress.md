# 0011 — Shared film player in the SDK; watch progress in the disc's WebView2 profile

**Status:** accepted (M4)

§9.4 makes the full-screen film player a shared SDK component. `disc.play(id)` on an html-engine video without an
element opens it (`ui/disc-sdk/src/player.ts`): seek bar, ±10 s, audio and subtitle menus, keyboard (Space, ←/→,
↑/↓, M, C, Esc), idle-hiding controls. M5's LibVLC overlay draws the same controls.

Resume positions ("Kaldığın yerden devam et") live in `localStorage`. The launcher already gives every disc its own
WebView2 user data folder under `%LOCALAPPDATA%\Inlay\Launcher\<disc>`, so this is per disc and where §7.0/§9.4
want it, with no extra bridge command. Positions under 30 s or past 95 % are not resumed; 95 % marks "İzlendi".
