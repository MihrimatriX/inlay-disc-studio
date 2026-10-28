// The builder's live preview stands in for the launcher (§7.3 "SDK taklidi"): the page is the same hosted render the
// disc gets, and this transport answers the bridge the way Baslat.exe would — except nothing is ever started.
import type { DiscManifest } from "../../../sdk/manifest";
import type { Transport } from "./sdk";

export function previewTransport(manifest: DiscManifest): Transport {
  let handler: ((m: unknown) => void) | null = null;
  const reply = (res: number, data?: unknown, error?: string) =>
    setTimeout(() => handler?.(error ? { v: 1, res, ok: false, error } : { v: 1, res, ok: true, data }), 250);

  return {
    listen: (h) => void (handler = h),
    send: (m) => {
      const { req, cmd, opts } = m as { req: number; cmd: string; opts?: { appIds?: string[] } };
      switch (cmd) {
        case "system":
          return reply(req, {
            launcherVersion: "preview",
            webviewVersion: /Edg\/([\d.]+)/.exec(navigator.userAgent)?.[1] ?? "",
            os: "Windows",
            freeSpaceBytes: { "C:": 200e9 },
            memoryBytes: ((navigator as { deviceMemory?: number }).deviceMemory ?? 8) * 2 ** 30,
            hasLibVlc: true,
            hasHevcExtension: false,
            isElevated: false,
            locale: manifest.disc.locale ?? "tr-TR",
          });
        case "run":
        case "runInstalled":
          return reply(req, { exitCode: 0 });
        case "installBundle":
          return reply(req, { installed: opts?.appIds ?? [], failed: [] });
        // Original files LibVLC would play are not available to the preview.
        case "play":
        case "playlist":
          return reply(req, undefined, "unsupported-codec");
        default:
          return reply(req);
      }
    },
  };
}
