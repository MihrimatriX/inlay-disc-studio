// Builds the GitHub Pages site: copies site/ to artifacts/site and fills in the repository, version and download link.
// The repository comes from GITHUB_REPOSITORY (set in Actions), --repo owner/name, or the git remote "origin".
// Usage: node scripts/build-site.mjs [--repo owner/name] [outDir]
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

const repoRoot = path.resolve(import.meta.dirname, "..");
const args = process.argv.slice(2);
const flag = args.indexOf("--repo");
const out = path.resolve(args.filter((_, i) => i !== flag && i !== flag + 1)[0] ?? path.join(repoRoot, "artifacts", "site"));

function originRepo() {
  try {
    const url = execFileSync("git", ["remote", "get-url", "origin"], { cwd: repoRoot, encoding: "utf8" }).trim();
    return /github\.com[:/](.+?\/.+?)(\.git)?$/.exec(url)?.[1];
  } catch { return undefined; }
}
const repo = (flag >= 0 ? args[flag + 1] : undefined) ?? process.env.GITHUB_REPOSITORY ?? originRepo();
if (!repo || !/^[\w.-]+\/[\w.-]+$/.test(repo)) throw new Error("repository unknown: pass --repo owner/name");

const csproj = fs.readFileSync(path.join(repoRoot, "src", "Inlay.Setup", "Inlay.Setup.csproj"), "utf8");
const version = /<Version>([^<]+)<\/Version>/.exec(csproj)?.[1] ?? "0.0.0";
// A fixed asset name, so "latest" always points at the newest setup (.github/workflows/release.yml uploads it).
const download = `https://github.com/${repo}/releases/latest/download/InlaySetup.exe`;

fs.rmSync(out, { recursive: true, force: true });
fs.cpSync(path.join(repoRoot, "site"), out, { recursive: true });
fs.copyFileSync(path.join(repoRoot, "src", "Inlay.Builder", "Inlay.ico"), path.join(out, "favicon.ico"));
for (const file of ["index.html"]) {
  const p = path.join(out, file);
  fs.writeFileSync(p, fs.readFileSync(p, "utf8").replaceAll("%REPO%", repo).replaceAll("%VERSION%", version).replaceAll("%DOWNLOAD%", download));
}
fs.writeFileSync(path.join(out, ".nojekyll"), "");
console.log(`${out} (${repo}, ${version})`);
