// Entry injected by the launcher (AddScriptToExecuteOnDocumentCreated) right after `window.__inlayBoot = {...}`, or by
// the builder's preview with `preview: true` (a stand-in launcher, see preview.ts).
import { createSdk, type Boot, type Transport } from "./sdk";
import { startOverlay } from "./overlay";
import { previewTransport } from "./preview";

declare global {
  interface Window {
    __inlayBoot?: Boot & { overlay?: { title: string }; preview?: boolean };
    chrome?: { webview?: { postMessage(m: unknown): void; addEventListener(t: "message", h: (e: { data: unknown }) => void): void } };
  }
}

const boot = window.__inlayBoot;
const wv = window.chrome?.webview;
const transport: Transport | null = boot?.preview ? previewTransport(boot.manifest)
  : wv ? { send: (m) => wv.postMessage(m), listen: (h) => wv.addEventListener("message", (e) => h(e.data)) }
  : null;
if (boot?.overlay && transport) {
  // The launcher's LibVLC overlay window: controls only (§9.4).
  addEventListener("DOMContentLoaded", () => startOverlay(boot.overlay!.title, boot.manifest.disc.locale ?? "tr-TR", transport));
} else if (boot) {
  const sdk = createSdk(boot, transport);
  Object.defineProperty(window, "disc", { value: sdk, writable: false, configurable: false });

  let fullscreen = false;
  addEventListener("keydown", (e) => {
    if (e.key === "F11") {
      e.preventDefault();
      sdk.setFullscreen((fullscreen = !fullscreen));
    }
  });
}
