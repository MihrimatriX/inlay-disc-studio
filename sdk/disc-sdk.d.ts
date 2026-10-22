/**
 * Disc Studio — Page SDK (v1)
 * Launcher, sayfa yüklenmeden önce `window.disc` nesnesini enjekte eder
 * (WebView2 AddScriptToExecuteOnDocumentCreated). Sayfa ile launcher arasındaki
 * TEK köprü budur; sayfa asla dosya yolu göndermez, yalnızca manifest id'leri.
 *
 * Mesaj protokolü (postMessage JSON):
 *   sayfa → launcher : { v:1, req:<n>, cmd:<Command>, id?:string, opts?:object }
 *   launcher → sayfa : { v:1, res:<n>, ok:boolean, data?:any, error?:DiscErrorCode }
 *   launcher → sayfa : { v:1, event:<DiscEventName>, data:any }   (istek dışı olaylar)
 */

export type DiscErrorCode =
  | "unknown-action"      // id manifest.actions içinde yok
  | "not-found"           // hedef dosya diskte yok
  | "cancelled"           // kullanıcı onay/UAC penceresini iptal etti
  | "unsupported-codec"   // html motoru oynatamadı ve fallback yok
  | "no-webview-feature"  // eski WebView2 runtime
  | "busy"                // aynı action zaten çalışıyor
  | "reboot-required"     // kurulum yeniden başlatma istedi (exit 3010)
  | "internal";

export interface DiscPalette {
  bg: string; surface: string; accent: string; accentText?: string; text: string; muted: string;
}

export type PlaybackEngine = "html" | "libvlc" | "shell";

export interface PlayOptions {
  startSec?: number;
  audioTrack?: number;
  subtitle?: string | null;   // lang kodu veya null = kapalı
  fullscreen?: boolean;
  /** html motoru için: oynatmayı sayfa içindeki bir <video>/<audio> elemanında yap. */
  element?: HTMLMediaElement;
}

export interface PlayerState {
  itemId: string;
  trackId?: string;
  engine: PlaybackEngine;
  status: "loading" | "playing" | "paused" | "ended" | "error";
  positionSec: number;
  durationSec: number;
  volume: number; // 0..1
}

export interface WatchProgress {
  positionSec: number;   // 0: baştan (başlanmadı ya da izlendi)
  durationSec: number;
  watched: boolean;      // %95 izlendi
  updatedAt: number;     // ms epoch
}

export interface SystemInfo {
  launcherVersion: string;
  webviewVersion: string;
  os: string;               // "Windows 11 23H2"
  freeSpaceBytes: Record<string, number>; // { "C:": 123... }
  /** Toplam fiziksel bellek; oyun şablonunun uyumluluk satırı (Steam RAM gereksinimi) için. */
  memoryBytes: number;
  hasLibVlc: boolean;
  hasHevcExtension: boolean;
  isElevated: boolean;
  locale: string;
}

export type DiscEventName =
  | "player"          // PlayerState
  | "action-started"  // { id }
  | "action-exited"   // { id, exitCode }
  | "media-key"       // { key: "play-pause" | "next" | "prev" | "stop" }
  | "gamepad"         // { button: string } (launcher XInput köprüsü, Gamepad API yoksa)
  | "bundle-progress" // { id, index, total, percent, state: "waiting" | "installing" | "done" | "failed" }
  | "visibility";     // { focused: boolean }

export interface DiscSdk {
  readonly version: "1";
  /** Launcher içinde mi? Tarayıcı fallback'inde false; butonlar "Diski Gözat" moduna düşer. */
  readonly hosted: boolean;
  /** Yakma anında render edilen manifest (aynı içerik .disc/manifest.json'da da var). */
  readonly manifest: import("./manifest").DiscManifest;
  readonly theme: { palette: DiscPalette; settings: Record<string, string | number | boolean> };

  /** manifest.actions[].id çalıştırır (install/run/open/openFolder/url). */
  run(actionId: string): Promise<{ exitCode?: number }>;

  /**
   * Program paketi: seçilen app id'lerini sırayla kurar (önce bundle.required olanlar, sonra bundle.order).
   * Launcher kendini bir kez yönetici olarak başlatır (tek UAC), kurulumları o süreç yürütür.
   * İlerleme "bundle-progress" olayıyla gelir.
   */
  installBundle(appIds: string[]): Promise<{ installed: string[]; failed: { id: string; exitCode: number }[] }>;

  /** Bir item/track/bölüm oynatır (fragman ve ekstralar disk yoluyla); motoru manifest'teki playback alanı belirler.
   *  html video element verilmeden çağrılırsa SDK'nın paylaşılan tam ekran oynatıcısı açılır (§9.4). */
  play(itemOrTrackId: string, opts?: PlayOptions): Promise<PlayerState>;
  playlist(albumId: string, startTrackId?: string, opts?: { shuffle?: boolean }): Promise<PlayerState>;
  slideshow(galleryId: string, startPhotoId?: string): Promise<void>;
  player: {
    pause(): void; resume(): void; stop(): void;
    seek(sec: number): void; next(): void; prev(): void;
    setVolume(v: number): void;
    state(): PlayerState | null;
  };

  /** Kaldığın yerden devam et (§7.0): item/bölüm/parça id'sine göre son konum; disk başına saklanır. */
  progress(itemOrTrackId: string): WatchProgress | null;
  progressAll(): Record<string, WatchProgress>;

  system(): Promise<SystemInfo>;
  setFullscreen(on: boolean): void;
  close(): void;

  on(event: DiscEventName, handler: (data: any) => void): () => void;
}

declare global {
  interface Window { disc: DiscSdk; }
}
