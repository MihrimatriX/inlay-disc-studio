// @ts-nocheck — half of this file runs inside the page (page.evaluate); Node-side typing would be noise.
// Starts Baslat.exe against a disc folder with CDP enabled and runs a check in the menu page (§12 integration).
// Usage: node scripts/cdp-probe.mjs <Baslat.exe> <discRoot | -> "<js expression>"   ("-": the exe folder is the disc root)
import { chromium } from "playwright-core";
import { spawn } from "node:child_process";

const [exe, root, expr] = process.argv.slice(2);
const port = 9300 + Math.floor(Math.random() * 500);
const child = spawn(exe, root === "-" ? [] : ["--root", root], {
  env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port}` },
  stdio: "ignore",
});

async function connect() {
  for (let i = 0; i < 60; i++) {
    try { return await chromium.connectOverCDP(`http://127.0.0.1:${port}`); }
    catch { await new Promise((r) => setTimeout(r, 250)); }
  }
  throw new Error("launcher did not expose CDP");
}

try {
  const browser = await connect();
  let page;
  for (let i = 0; i < 40 && !page; i++) {
    page = browser.contexts().flatMap((c) => c.pages()).find((p) => p.url().startsWith("https://disc.local/"));
    if (!page) await new Promise((r) => setTimeout(r, 250));
  }
  if (!page) throw new Error("menu page did not open: " + browser.contexts().flatMap((c) => c.pages()).map((p) => p.url()));
  await page.waitForLoadState("load");
  const result = await page.evaluate(`(async () => (${expr}))()`);
  console.log(JSON.stringify(result, null, 2));
  await browser.close();
} finally {
  child.kill();
}
