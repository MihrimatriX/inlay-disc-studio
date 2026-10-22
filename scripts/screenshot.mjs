// @ts-nocheck — half of this file runs inside the page (page.evaluate); Node-side typing would be noise.
// Opens artifacts/preview/<disc> in the real launcher (Baslat.exe: disc.local virtual host, SDK injection, 403 for the
// network) over CDP, then: screenshot at 1440×810, axe-core accessibility audit, keyboard reachability of every control.
// Usage: node scripts/screenshot.mjs [disc ...]   (the Inlay.Theming.Tests golden test writes the preview discs)
import { chromium } from "playwright-core";
import { spawn } from "node:child_process";
import { readFileSync, existsSync, readdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";

const repo = path.resolve(import.meta.dirname, "..");
const previews = path.join(repo, "artifacts", "preview");
const launcher = [path.join(repo, "artifacts", "launcher", "Baslat.exe"), path.join(repo, "src", "Inlay.Launcher", "bin", "Debug", "net10.0-windows", "win-x64", "Baslat.exe")].find(existsSync);
if (!launcher) throw new Error("build or publish src/Inlay.Launcher first");
const axe = readFileSync(createRequire(import.meta.url).resolve("axe-core/axe.min.js"), "utf8");
const names = process.argv.slice(2).length ? process.argv.slice(2) : readdirSync(previews).filter((n) => existsSync(path.join(previews, n, ".disc")));

async function open(root) {
  const port = 9400 + Math.floor(Math.random() * 400);
  const child = spawn(launcher, ["--root", root], { env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port} --force-prefers-reduced-motion` }, stdio: "ignore" });
  for (let i = 0; i < 80; i++) {
    try {
      const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
      for (let j = 0; j < 40; j++) {
        const page = browser.contexts().flatMap((c) => c.pages()).find((p) => p.url().startsWith("https://disc.local/"));
        if (page) return { browser, page, child };
        await new Promise((r) => setTimeout(r, 250));
      }
      throw new Error("menu page did not open");
    } catch (e) {
      if (e.message === "menu page did not open") { child.kill(); throw e; }
      await new Promise((r) => setTimeout(r, 250));
    }
  }
  child.kill();
  throw new Error("launcher did not expose CDP");
}

const report = {};
let failed = false;
for (const name of names) {
  const { browser, page, child } = await open(path.join(previews, name));
  const errors = [];
  page.on("pageerror", (e) => errors.push(e.message));
  try {
    await page.setViewportSize({ width: 1440, height: 810 });
    await page.reload();
    await page.waitForLoadState("networkidle");
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(400);
    await page.screenshot({ path: path.join(previews, `${name}.png`) });

    // Every control on the opening page is reachable with the arrow keys alone (§7.2.6).
    const reach = await page.evaluate(async () => {
      const view = [...document.querySelectorAll(".view")].find((v) => !v.hidden);
      const wanted = [...view.querySelectorAll("button[data-action], button[data-sheet], button[data-play], .card, .track, .tile, .pkg, .shot, #bundle-install")].filter((b) => b.offsetParent);
      const seen = new Set([document.activeElement]);
      for (const dir of ["ArrowRight", "ArrowDown", "ArrowLeft", "ArrowUp", "ArrowRight", "ArrowDown", "ArrowUp"])
        for (let i = 0; i < 30; i++) {
          document.activeElement.dispatchEvent(new KeyboardEvent("keydown", { key: dir, bubbles: true }));
          seen.add(document.activeElement);
        }
      return { total: wanted.length, missed: wanted.filter((b) => !seen.has(b)).map((b) => b.outerHTML.slice(0, 90)) };
    });

    await page.addScriptTag({ content: axe });
    const a11y = await page.evaluate(async () => {
      const r = await axe.run(document, { runOnly: ["wcag2a", "wcag2aa"], resultTypes: ["violations"] });
      return r.violations.map((v) => ({ id: v.id, impact: v.impact, nodes: v.nodes.length, sample: v.nodes[0]?.html.slice(0, 140) }));
    });
    report[name] = { errors, reach, a11y };
    const bad = errors.length > 0 || reach.missed.length > 0 || a11y.some((v) => v.impact === "critical" || v.impact === "serious");
    failed ||= bad;
    console.log(`${bad ? "✗" : "✓"} ${name}: ${reach.total} controls, ${reach.missed.length} unreachable; axe: ${a11y.length ? a11y.map((v) => `${v.id}/${v.impact}×${v.nodes}`).join(", ") : "clean"}${errors.length ? "; errors: " + errors.join(" | ") : ""}`);
  } finally {
    await browser.close().catch(() => {});
    // The next disc may share this one's single-instance mutex: wait until the process is really gone.
    const gone = new Promise((r) => child.once("exit", r));
    child.kill();
    await gone;
  }
}
writeFileSync(path.join(previews, "report.json"), JSON.stringify(report, null, 2));
process.exit(failed ? 1 : 0);
