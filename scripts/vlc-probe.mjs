// @ts-nocheck — drives pages over CDP; half of it runs inside them.
// Plays a LibVLC item on a disc folder through the real launcher and inspects both WebView2 pages: the menu (state
// from the launcher) and the overlay (the shared controls). Saves a screen capture of the window.
// Usage: node scripts/vlc-probe.mjs <Baslat.exe> <discRoot> <itemId> <screenshot.png> <window title>
import { chromium } from "playwright-core";
import { spawn, execFileSync } from "node:child_process";
import path from "node:path";

const [exe, root, itemId, shot, title] = process.argv.slice(2);
const port = 9800 + Math.floor(Math.random() * 150);
const child = spawn(exe, ["--root", root], { env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port}` }, stdio: "ignore" });
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const pages = (b) => b.contexts().flatMap((c) => c.pages());

try {
  let browser;
  for (let i = 0; i < 80 && !browser; i++) { try { browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`); } catch { await wait(250); } }
  let menu;
  for (let i = 0; i < 40 && !menu; i++) { menu = pages(browser).find((p) => p.url().startsWith("https://disc.local/")); if (!menu) await wait(250); }
  await menu.waitForLoadState("load");
  const started = await menu.evaluate((id) => disc.play(id).then((s) => s, (e) => ({ error: e.code })), itemId);
  await wait(2500);
  const state = await menu.evaluate(() => disc.player.state());
  let overlay;
  for (let i = 0; i < 20 && !overlay; i++) { overlay = pages(browser).find((p) => p !== menu); if (!overlay) await wait(250); }
  const controls = overlay ? await overlay.evaluate(() => ({
    open: !!document.querySelector(".ip"), remote: document.querySelector(".ip")?.classList.contains("remote"),
    time: document.querySelector(".ip .time")?.textContent, title: document.querySelector(".ip .top")?.textContent,
    buttons: [...document.querySelectorAll(".ip button")].filter((b) => !b.hidden).map((b) => b.dataset.k),
  })) : null;
  // Controls hide after 3 s idle and PowerShell takes about that long to start: keep waking them like a mouse move
  // would. window-shot.ps1 renders the launcher's own windows, so nothing else on the screen can end up in the picture.
  if (overlay) await overlay.evaluate(() => { const w = () => document.querySelector(".ip")?.dispatchEvent(new PointerEvent("pointermove", { bubbles: true })); w(); setInterval(w, 500); });
  await wait(300);
  execFileSync("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path.join(import.meta.dirname, "window-shot.ps1"), "-ProcessId", String(child.pid), "-Out", shot]);
  if (overlay) await overlay.evaluate(() => document.querySelector("[data-k=close]").click());
  await wait(800);
  const after = await menu.evaluate(() => ({ state: disc.player.state(), progress: disc.progress(Object.keys(disc.progressAll())[0] ?? "") }));
  console.log(JSON.stringify({ started, state, controls, after }, null, 2));
  await browser.close();
} finally {
  child.kill();
}
