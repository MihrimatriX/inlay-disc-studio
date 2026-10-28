// @ts-nocheck — drives the builder over CDP; half of it runs inside the page.
// Opens Inlay.exe on a scratch data folder, adds a source folder and captures the four screens of design/screens
// (01 Başlangıç, 02 Disk içeriği, 03 Disk menüsü, code mode) from the WebView2 page itself — never the desktop.
// Prints a JSON report (axe-core violations per screen, keyboard reach). Usage:
//   node scripts/builder-shot.mjs <Inlay.exe> <sourceFolder> <outDir>
import { chromium } from "playwright-core";
import { spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const [exe, source, outDir] = process.argv.slice(2);
fs.mkdirSync(outDir, { recursive: true });
const root = fs.mkdtempSync(path.join(os.tmpdir(), "inlay-builder-"));
const port = 9600 + Math.floor(Math.random() * 150);
const child = spawn(exe, [], { env: { ...process.env, INLAY_ROOT: root, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port}` }, stdio: "ignore" });
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const axe = fs.readFileSync(path.join(import.meta.dirname, "..", "node_modules", "axe-core", "axe.min.js"), "utf8");

const report = {};
try {
  let browser;
  for (let i = 0; i < 120 && !browser; i++) { try { browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`); } catch { await wait(250); } }
  let page;
  for (let i = 0; i < 400 && !page; i++) { page = browser.contexts().flatMap((c) => c.pages()).find((p) => p.url().startsWith("https://app.inlay/")); if (!page) await wait(250); }
  await page.waitForSelector("nav");
  await page.evaluate(() => document.fonts.ready);
  await wait(600);

  const audit = async (name) => {
    await page.evaluate(axe);
    const r = await page.evaluate(async () => (await axe.run(document, { runOnly: ["wcag2a", "wcag2aa"] })).violations.map((v) => ({ id: v.id, nodes: v.nodes.length, target: v.nodes[0]?.target })));
    report[name] = { axe: r };
    await page.screenshot({ path: path.join(outDir, `${name}.png`) });
  };

  await audit("01-baslangic");
  await page.getByRole("button", { name: "Ayarlar" }).click();
  await page.waitForSelector("[role=dialog]");
  await wait(400);
  await audit("06-ayarlar");
  await page.keyboard.press("Escape");
  await page.getByRole("button", { name: "Disk kopyala" }).click();
  await wait(300);
  await audit("07-disk-kopyala");
  await page.getByRole("button", { name: "Film ya da dizi" }).click();

  await page.evaluate((p) => window.chrome.webview.postMessage({ id: "shot-1", method: "content.add", params: { paths: [p] } }), source);
  await page.waitForSelector("table tbody tr", { timeout: 120000 });
  await page.waitForFunction(() => !document.body.innerText.includes("inceleniyor"), null, { timeout: 120000 });
  await wait(1500);
  await audit("02-disk-icerigi");
  report["02-disk-icerigi"].matchCard = await page.locator("tr td[colspan] form, tr td[colspan] button").count() > 0;

  await page.getByRole("button", { name: /Disk menüsü/ }).click();
  await page.waitForSelector("iframe");
  await wait(2500);
  await audit("03-disk-menusu");

  await page.getByRole("button", { name: /HTML olarak düzenle/ }).click();
  await page.waitForSelector(".cm-editor", { timeout: 20000 });
  await wait(1500);
  await audit("05-kod");

  console.log(JSON.stringify(report, null, 2));
  await browser.close();
} finally {
  child.kill();
  await wait(500);
  fs.rmSync(root, { recursive: true, force: true, maxRetries: 20, retryDelay: 250 });
}
