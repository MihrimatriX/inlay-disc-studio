import type {
  DiscErrorCode, DiscEventName, DiscPalette, DiscSdk, PlayerState, PlayOptions, SystemInfo,
} from "../../../sdk/disc-sdk";
import type { DiscManifest, MediaFile } from "../../../sdk/manifest";
import * as progress from "./progress";
import { htmlMedia, openPlayer } from "./player";

/** What the host (launcher, builder preview, or the static Menu.html fallback) hands the page before it loads. */
export interface Boot {
  manifest: DiscManifest;
  hosted: boolean;
}

/** Message channel to the host. Absent when the menu runs in a plain browser (hosted = false). */
export interface Transport {
  send(message: object): void;
  listen(handler: (message: any) => void): void;
}

export class DiscError extends Error {
  constructor(public readonly code: DiscErrorCode) {
    super(code);
  }
}

type Listener = (data: any) => void;
interface Playable { itemId: string; trackId?: string; media: MediaFile; title: string }

/** Disc-root relative path → URL the page can load. */
export function relUrl(path: string, hosted: boolean): string {
  const encoded = path.split("/").map(encodeURIComponent).join("/");
  return hosted ? `https://disc.local/${encoded}` : `./${encoded}`;
}

/** Item, album track or series episode by id (they share one id space); albums/series start at their first entry.
 *  A movie's trailer and extras have no id in the schema and are found by their disc path. */
export function findMedia(m: DiscManifest, id: string): Playable | null {
  for (const item of m.items) {
    if (item.type === "movie") {
      if (item.id === id) return { itemId: id, media: item.media, title: item.title };
      const extra = [item.trailer, ...(item.extras ?? [])].find((x) => x?.file === id);
      if (extra) return { itemId: item.id, trackId: extra.file, media: extra, title: item.title };
    }
    if (item.type === "album") {
      const t = item.id === id ? item.tracks[0] : item.tracks.find((t) => t.id === id);
      if (t) return { itemId: item.id, trackId: t.id, media: t.media, title: t.title };
    }
    if (item.type === "series") {
      for (const s of item.seasons) {
        const ep = item.id === id ? s.episodes[0] : s.episodes.find((e) => e.id === id);
        if (ep) return { itemId: item.id, trackId: ep.id, media: ep.media, title: `${item.title} · ${ep.title}` };
      }
    }
    if (item.type === "gallery") {
      const p = item.photos.find((p) => p.media?.file === id || p.id === id);
      if (p?.media) return { itemId: item.id, trackId: p.id, media: p.media, title: item.title };
    }
  }
  return null;
}

export function createSdk(boot: Boot, transport: Transport | null): DiscSdk {
  const { manifest, hosted } = boot;
  const listeners = new Map<DiscEventName, Set<Listener>>();
  const pending = new Map<number, { resolve: (d: any) => void; reject: (e: unknown) => void }>();
  let nextReq = 1;
  /** What is playing: in a page element (html engine) or in the launcher (libvlc/shell). */
  let current: { state: PlayerState; element?: HTMLMediaElement; queue?: Playable[]; index?: number } | null = null;
  let sharedAudio: HTMLAudioElement | null = null;
  let closeOverlay: (() => void) | null = null;

  const emit = (event: DiscEventName, data: any) => listeners.get(event)?.forEach((l) => l(data));

  transport?.listen((msg) => {
    if (msg?.v !== 1) return;
    if (typeof msg.res === "number") {
      const p = pending.get(msg.res);
      if (!p) return;
      pending.delete(msg.res);
      msg.ok ? p.resolve(msg.data) : p.reject(new DiscError(msg.error ?? "internal"));
    } else if (typeof msg.event === "string") {
      if (msg.event === "player" && current && !current.element) {
        // LibVLC playback in the launcher: remember the position like the html engine does.
        current.state = msg.data;
        const s: PlayerState = msg.data;
        if (s.durationSec && (s.status === "paused" || s.status === "ended" || Math.floor(s.positionSec) % 5 === 0))
          progress.save(s.trackId ?? s.itemId, s.status === "ended" ? s.durationSec : s.positionSec, s.durationSec);
      }
      emit(msg.event, msg.data);
    }
  });

  function request<T>(cmd: string, id?: string, opts?: object): Promise<T> {
    if (!transport) return Promise.reject(new DiscError("internal"));
    const req = nextReq++;
    return new Promise<T>((resolve, reject) => {
      pending.set(req, { resolve, reject });
      transport.send({ v: 1, req, cmd, id, opts });
    });
  }
  const fire = (cmd: string, opts?: object) => void request(cmd, undefined, opts).catch(() => {});

  function setState(patch: Partial<PlayerState>) {
    if (!current) return;
    current.state = { ...current.state, ...patch };
    emit("player", current.state);
  }

  function bindElement(el: HTMLMediaElement) {
    if (el.dataset.inlayBound) return;
    el.dataset.inlayBound = "1";
    const mine = () => current?.element === el;
    let saved = 0;
    const sync = () => {
      if (!mine()) return;
      setState({ positionSec: el.currentTime, durationSec: el.duration || 0, volume: el.volume });
      if (Math.abs(el.currentTime - saved) >= 5) {
        saved = el.currentTime;
        progress.save(current!.state.trackId ?? current!.state.itemId, el.currentTime, el.duration);
      }
    };
    el.addEventListener("playing", () => mine() && setState({ status: "playing" }));
    el.addEventListener("pause", () => mine() && !el.ended && setState({ status: "paused" }));
    el.addEventListener("error", () => mine() && setState({ status: "error" }));
    el.addEventListener("timeupdate", sync);
    el.addEventListener("loadedmetadata", sync);
    el.addEventListener("ended", () => {
      if (!mine()) return;
      progress.save(current!.state.trackId ?? current!.state.itemId, el.duration, el.duration);
      const q = current!;
      if (q.queue && q.index! + 1 < q.queue.length) void startInElement(q.queue, q.index! + 1, el);
      else setState({ status: "ended" });
    });
  }

  async function startInElement(queue: Playable[], index: number, el: HTMLMediaElement, opts: PlayOptions = {}) {
    const p = queue[index];
    if (current?.element && current.element !== el) current.element.pause();
    current = {
      element: el, queue, index,
      state: {
        itemId: p.itemId, trackId: p.trackId, engine: "html", status: "loading",
        positionSec: opts.startSec ?? 0, durationSec: p.media.durationSec ?? 0, volume: el.volume,
      },
    };
    bindElement(el);
    el.src = relUrl(p.media.file, hosted);
    el.currentTime = opts.startSec ?? 0;
    emit("player", current.state);
    try {
      await el.play();
    } catch {
      // §6: what WebView2 cannot decode may still play from the original through LibVLC.
      if (p.media.playback.fallback === "libvlc" && hosted) {
        closeOverlay?.();
        return startInLauncher("play", p.trackId ?? p.itemId, { startSec: opts.startSec, original: true });
      }
      setState({ status: "error" });
      throw new DiscError("unsupported-codec");
    }
    if (opts.fullscreen && el.requestFullscreen) await el.requestFullscreen().catch(() => {});
    return current.state;
  }

  async function startInLauncher(cmd: string, id: string, opts: object): Promise<PlayerState> {
    current?.element?.pause();
    const state = await request<PlayerState>(cmd, id, opts);
    current = { state };
    return state;
  }

  /** The page-wide audio element music keeps playing in while the viewer moves between pages (§7.0). */
  function audio(): HTMLAudioElement {
    if (!sharedAudio) {
      sharedAudio = document.createElement("audio");
      sharedAudio.hidden = true;
      document.body.append(sharedAudio);
    }
    return sharedAudio;
  }

  async function play(id: string, opts: PlayOptions = {}): Promise<PlayerState> {
    const p = findMedia(manifest, id);
    if (!p) throw new DiscError("not-found");
    const { element, ...wire } = opts;
    if (p.media.playback.engine !== "html") return startInLauncher("play", id, wire);
    if (!hosted && !element && !p.media.video) {
      location.href = relUrl(p.media.file, false);
      return { itemId: p.itemId, trackId: p.trackId, engine: "html", status: "loading", positionSec: 0, durationSec: 0, volume: 1 };
    }
    if (!element && p.media.video) {
      // §9.4: the shared full-screen player; resuming is the caller's choice (startSec).
      const video = document.createElement("video");
      video.playsInline = true;
      const names = new Intl.DisplayNames([document.documentElement.lang || "tr"], { type: "language" });
      for (const sub of p.media.subtitles ?? []) {
        if (!sub.file) continue; // picture subtitles are LibVLC-only
        const t = document.createElement("track");
        Object.assign(t, { kind: "subtitles", src: relUrl(sub.file, hosted), srclang: sub.lang, label: names.of(sub.lang) ?? sub.lang, default: !!sub.forced });
        video.append(t);
      }
      closeOverlay = openPlayer({
        title: p.title, media: htmlMedia(video),
        onClose: () => {
          closeOverlay = null;
          if (current?.element !== video) return;
          progress.save(p.trackId ?? p.itemId, video.currentTime, video.duration);
          setState({ status: "ended" });
        },
      });
      return startInElement([p], 0, video, { ...opts, fullscreen: false });
    }
    return startInElement([p], 0, element ?? audio(), opts);
  }

  async function playlist(albumId: string, startTrackId?: string, opts: { shuffle?: boolean } = {}): Promise<PlayerState> {
    const album = manifest.items.find((i) => i.id === albumId);
    if (album?.type !== "album") throw new DiscError("not-found");
    if (album.tracks.some((t) => t.media.playback.engine !== "html")) return startInLauncher("playlist", albumId, { startTrackId, shuffle: opts.shuffle });
    const queue: Playable[] = album.tracks.map((t) => ({ itemId: album.id, trackId: t.id, media: t.media, title: t.title }));
    if (opts.shuffle)
      for (let i = queue.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [queue[i], queue[j]] = [queue[j], queue[i]];
      }
    const start = Math.max(0, queue.findIndex((q) => q.trackId === startTrackId));
    return startInElement(queue, start, audio());
  }

  function step(delta: number) {
    const q = current;
    if (!q?.element || !q.queue) return fire(delta > 0 ? "player.next" : "player.prev");
    const i = q.index! + delta;
    if (i >= 0 && i < q.queue.length) void startInElement(q.queue, i, q.element).catch(() => {});
  }

  const sdk: DiscSdk = {
    version: "1",
    hosted,
    manifest,
    theme: {
      palette: manifest.theme.palette as DiscPalette,
      settings: (manifest.theme.settings ?? {}) as Record<string, string | number | boolean>,
    },

    async run(actionId) {
      const action = manifest.actions.find((a) => a.id === actionId);
      if (!action) throw new DiscError("unknown-action");
      switch (action.kind) {
        case "play": await play(action.target!); return {};
        case "playlist": await playlist(action.target!); return {};
        case "slideshow": await sdk.slideshow(action.target!); return {};
      }
      if (!hosted) {
        if (action.kind === "url") window.open(action.target, "_blank", "noopener");
        else location.href = relUrl(action.target!, false);
        return {};
      }
      return request<{ exitCode?: number }>("run", actionId);
    },

    installBundle: (appIds) => request("installBundle", undefined, { appIds }),
    play,
    playlist,

    /** The gallery template owns the viewer (t3: full screen, Ken Burns); the SDK only asks it to start. */
    async slideshow(galleryId, startPhotoId) {
      const gallery = manifest.items.find((i) => i.id === galleryId);
      if (gallery?.type !== "gallery") throw new DiscError("not-found");
      document.dispatchEvent(new CustomEvent("disc:slideshow", { detail: { galleryId, startPhotoId } }));
      const music = gallery.slideshow?.music;
      if (music) await play(music).catch(() => {});
    },

    player: {
      pause: () => (current?.element ? current.element.pause() : fire("player.pause")),
      resume: () => (current?.element ? void current.element.play().catch(() => {}) : fire("player.resume")),
      stop: () => {
        if (!current?.element) return fire("player.stop");
        current.element.pause();
        current.element.removeAttribute("src");
        current.element.load();
        setState({ status: "ended" });
      },
      seek: (sec) => (current?.element ? void (current.element.currentTime = sec) : fire("player.seek", { sec })),
      next: () => step(1),
      prev: () => step(-1),
      setVolume: (v) => {
        const volume = Math.min(1, Math.max(0, v));
        if (current?.element) current.element.volume = volume;
        else fire("player.setVolume", { volume });
      },
      state: () => current?.state ?? null,
    },

    progress: (id) => progress.get(id),
    progressAll: () => progress.all(),
    system: () => request<SystemInfo>("system"),
    setFullscreen: (on) => fire("setFullscreen", { on }),
    close: () => fire("close"),

    on(event, handler) {
      if (!listeners.has(event)) listeners.set(event, new Set());
      listeners.get(event)!.add(handler);
      return () => listeners.get(event)!.delete(handler);
    },
  };
  return sdk;
}
