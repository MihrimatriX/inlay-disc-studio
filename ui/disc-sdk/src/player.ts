// The shared full-screen film player (§9.4). The same controls drive an html <video> or, through the launcher,
// LibVLC behind the launcher's clipped overlay window — the viewer must not see which engine plays.

export interface Track { label: string; active: boolean; select(): void }

/** What the controls need from an engine. */
export interface MediaLike {
  readonly element?: HTMLVideoElement;
  readonly paused: boolean;
  readonly currentTime: number;
  readonly duration: number;
  volume: number;
  muted: boolean;
  toggle(): void;
  seek(sec: number): void;
  close(): void;
  subtitles(): Track[];
  subtitlesOff(): void;
  audio(): Track[];
  /** Notifies on time, play/pause, metadata, end. */
  onChange(fn: (ended: boolean) => void): void;
}

export interface PlayerOpen {
  title: string;
  media: MediaLike;
  onClose: () => void;
  /** Visible panels in CSS px, whenever they change (the LibVLC overlay clips its window to them). */
  onPanels?: (rects: { x: number; y: number; w: number; h: number; r: number }[]) => void;
}

const CSS = `
.ip{position:fixed;inset:0;z-index:2147483000;background:#000;color:#f6f2ec;font:400 16px/1.4 Outfit,"Segoe UI Variable Text","Segoe UI",sans-serif;cursor:none}
.ip.remote{background:transparent}.ip.ui{cursor:default}
.ip video{position:absolute;inset:0;width:100%;height:100%;object-fit:contain;background:#000}
.ip video::cue{font:500 clamp(18px,2.4vw,42px)/1.3 Outfit,"Segoe UI",sans-serif;color:#fff;background:rgba(0,0,0,.55);text-shadow:0 1px 3px #000}
.ip .bar{position:absolute;left:4vw;right:4vw;bottom:3.5vh;padding:18px 22px 16px;border-radius:18px;background:rgba(20,20,22,.88);opacity:0;visibility:hidden;transition:opacity .25s}
.ip .top{position:absolute;left:4vw;top:3.5vh;max-width:60vw;padding:10px 18px;border-radius:14px;background:rgba(20,20,22,.88);opacity:0;visibility:hidden;transition:opacity .25s;font-size:clamp(16px,1.4vw,30px);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.ip.ui .bar,.ip.ui .top{opacity:1;visibility:visible}
.ip.remote .bar,.ip.remote .top,.ip.remote .menu{background:#141416;transition:none}
.ip .seek{position:relative;height:6px;border-radius:9px;background:rgba(255,255,255,.2);margin:6px 0 16px;cursor:pointer}
.ip .seek i{position:absolute;left:0;top:0;bottom:0;border-radius:inherit;background:#f6f2ec}
.ip .seek b{position:absolute;top:50%;width:16px;height:16px;margin:-8px 0 0 -8px;border-radius:50%;background:#f6f2ec}
.ip .row{display:flex;align-items:center;gap:12px}.ip .grow{flex:1}.ip .time{font-variant-numeric:tabular-nums;color:rgba(246,242,236,.8)}
.ip button{height:48px;min-width:48px;padding:0 14px;border:0;border-radius:999px;background:rgba(255,255,255,.14);color:inherit;font:inherit;display:inline-flex;align-items:center;justify-content:center;gap:8px;cursor:pointer}
.ip button:focus-visible{outline:3px solid #f6f2ec;outline-offset:3px}.ip button.main{background:#f6f2ec;color:#121314}
.ip svg{width:20px;height:20px}
.ip .menu{position:absolute;right:5vw;bottom:15vh;min-width:220px;padding:8px;border-radius:14px;background:rgba(30,30,32,.94);display:flex;flex-direction:column;gap:4px}
.ip .menu button{justify-content:flex-start;background:transparent}.ip .menu button[aria-checked=true]{background:rgba(255,255,255,.14)}
@media (prefers-reduced-motion:reduce){.ip .bar,.ip .top{transition:none}}`;

const ICON = {
  play: '<path fill="currentColor" d="M7 4.5v15l12-7.5z"/>',
  pause: '<path fill="currentColor" d="M6 4.5h4v15H6zM14 4.5h4v15h-4z"/>',
  back10: '<path fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" d="M4 12a8 8 0 1 0 2.4-5.7M4 4v4h4"/><text x="12" y="15.5" font-size="7" text-anchor="middle" fill="currentColor">10</text>',
  fwd10: '<path fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" d="M20 12a8 8 0 1 1-2.4-5.7M20 4v4h-4"/><text x="12" y="15.5" font-size="7" text-anchor="middle" fill="currentColor">10</text>',
  subs: '<rect x="3" y="5" width="18" height="14" rx="2" fill="none" stroke="currentColor" stroke-width="2"/><path fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" d="M7 13h4M13 13h4M7 16h10"/>',
  audio: '<path fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" d="M4 9v6h4l5 4V5L8 9zM16 9a4 4 0 0 1 0 6M18.5 6.5a8 8 0 0 1 0 11"/>',
  close: '<path fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" d="M6 6l12 12M18 6 6 18"/>',
};
const svg = (name: keyof typeof ICON) => `<svg viewBox="0 0 24 24" aria-hidden="true">${ICON[name]}</svg>`;

export const clock = (s: number) => {
  if (!isFinite(s)) s = 0;
  const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = Math.floor(s % 60);
  return h ? `${h}:${String(m).padStart(2, "0")}:${String(sec).padStart(2, "0")}` : `${m}:${String(sec).padStart(2, "0")}`;
};

/** An html <video> as MediaLike: native text tracks; audio tracks where WebView2 exposes them. */
export function htmlMedia(video: HTMLVideoElement): MediaLike {
  const at = () => (video as any).audioTracks as { length: number; [i: number]: { enabled: boolean; language: string; label: string } } | undefined;
  const names = new Intl.DisplayNames([document.documentElement.lang || "tr"], { type: "language" });
  return {
    element: video,
    get paused() { return video.paused; },
    get currentTime() { return video.currentTime; },
    get duration() { return video.duration; },
    get volume() { return video.volume; },
    set volume(v) { video.volume = v; },
    get muted() { return video.muted; },
    set muted(v) { video.muted = v; },
    toggle: () => void (video.paused ? video.play().catch(() => {}) : video.pause()),
    seek: (sec) => void (video.currentTime = Math.max(0, Math.min(video.duration || Infinity, sec))),
    close: () => video.pause(),
    subtitles: () => [...video.textTracks].map((t) => ({
      label: t.label, active: t.mode === "showing",
      select: () => [...video.textTracks].forEach((x) => (x.mode = x === t ? "showing" : "disabled")),
    })),
    subtitlesOff: () => [...video.textTracks].forEach((t) => (t.mode = "disabled")),
    audio: () => {
      const a = at();
      if (!a || a.length < 2) return [];
      return Array.from({ length: a.length }, (_, i) => ({
        label: a[i].label || names.of(a[i].language) || `${i + 1}`, active: a[i].enabled,
        select: () => { for (let j = 0; j < a.length; j++) a[j].enabled = j === i; },
      }));
    },
    onChange: (fn) => {
      ["timeupdate", "play", "pause", "loadedmetadata"].forEach((e) => video.addEventListener(e, () => fn(false)));
      video.addEventListener("ended", () => fn(true));
    },
  };
}

export function openPlayer(o: PlayerOpen): () => void {
  if (!document.getElementById("ip-style")) {
    const style = document.createElement("style");
    style.id = "ip-style";
    style.textContent = CSS;
    document.head.append(style);
  }
  document.querySelector(".ip")?.remove();
  const lang = document.documentElement.lang || "tr";
  const tr = lang.startsWith("tr");
  const m = o.media;

  const el = document.createElement("div");
  el.className = "ip ui" + (m.element ? "" : " remote");
  el.setAttribute("role", "dialog");
  el.setAttribute("aria-label", o.title);
  el.innerHTML = `<div class="top"></div>
    <div class="bar">
      <div class="seek" role="slider" aria-label="${tr ? "Konum" : "Position"}" tabindex="-1"><i></i><b></b></div>
      <div class="row">
        <button class="main" data-k="toggle" aria-label="${tr ? "Oynat / duraklat" : "Play / pause"}">${svg("pause")}</button>
        <button data-k="back" aria-label="−10 sn">${svg("back10")}</button>
        <button data-k="fwd" aria-label="+10 sn">${svg("fwd10")}</button>
        <span class="time"></span><span class="grow"></span>
        <button data-k="audio" aria-label="${tr ? "Ses" : "Audio"}" hidden>${svg("audio")}</button>
        <button data-k="subs" aria-label="${tr ? "Altyazı" : "Subtitles"}" hidden>${svg("subs")}</button>
        <button data-k="close" aria-label="${tr ? "Kapat" : "Close"}">${svg("close")}</button>
      </div>
    </div>`;
  (el.querySelector(".top") as HTMLElement).textContent = o.title;
  if (m.element) el.prepend(m.element);
  document.body.append(el);

  const seek = el.querySelector(".seek") as HTMLElement;
  const fill = seek.querySelector("i") as HTMLElement, knob = seek.querySelector("b") as HTMLElement;
  const time = el.querySelector(".time") as HTMLElement;
  const toggle = el.querySelector("[data-k=toggle]") as HTMLButtonElement;
  const subsBtn = el.querySelector("[data-k=subs]") as HTMLButtonElement;
  const audioBtn = el.querySelector("[data-k=audio]") as HTMLButtonElement;

  let idle: number | undefined;
  let lastPanels = "";
  const panels = () => {
    if (!o.onPanels) return;
    const show = el.classList.contains("ui");
    const rects = [...el.querySelectorAll<HTMLElement>(show ? ".top, .bar, .menu" : ".menu")].map((p) => {
      const r = p.getBoundingClientRect();
      return { x: Math.floor(r.left), y: Math.floor(r.top), w: Math.ceil(r.width), h: Math.ceil(r.height), r: parseFloat(getComputedStyle(p).borderTopLeftRadius) || 0 };
    });
    const key = JSON.stringify(rects);
    if (key !== lastPanels) o.onPanels((lastPanels = key, rects));
  };
  const wake = () => {
    el.classList.add("ui");
    clearTimeout(idle);
    idle = window.setTimeout(() => { if (!m.paused && !el.querySelector(".menu")) el.classList.remove("ui"); panels(); }, 3000);
    panels(); // not rAF: a fully clipped overlay counts as occluded and gets no frames
  };
  addEventListener("resize", panels);
  let wasPaused: boolean | null = null;
  const paint = () => {
    const p = m.duration ? (100 * m.currentTime) / m.duration : 0;
    fill.style.width = p + "%";
    knob.style.left = p + "%";
    time.textContent = `${clock(m.currentTime)} / ${clock(m.duration)}`;
    if (wasPaused !== m.paused) toggle.innerHTML = svg(m.paused ? "play" : "pause");
    wasPaused = m.paused;
    subsBtn.hidden = m.subtitles().length === 0;
    audioBtn.hidden = m.audio().length < 2;
  };
  m.onChange((ended) => (ended ? close() : paint()));

  function menu(kind: "subs" | "audio") {
    el.querySelector(".menu")?.remove();
    const box = document.createElement("div");
    box.className = "menu";
    box.setAttribute("role", "menu");
    const add = (label: string, checked: boolean, pick: () => void) => {
      const b = document.createElement("button");
      b.setAttribute("role", "menuitemradio");
      b.setAttribute("aria-checked", String(checked));
      b.textContent = label;
      b.onclick = () => { pick(); box.remove(); wake(); panels(); };
      box.append(b);
    };
    const tracks = kind === "subs" ? m.subtitles() : m.audio();
    if (kind === "subs") add(tr ? "Kapalı" : "Off", tracks.every((t) => !t.active), () => m.subtitlesOff());
    tracks.forEach((t) => add(t.label, t.active, t.select));
    el.append(box);
    panels();
    ((box.querySelector("[aria-checked=true]") as HTMLElement | null) ?? (box.querySelector("button") as HTMLElement | null))?.focus();
  }

  const act: Record<string, () => void> = {
    toggle: () => m.toggle(),
    back: () => m.seek(m.currentTime - 10),
    fwd: () => m.seek(m.currentTime + 10),
    subs: () => menu("subs"),
    audio: () => menu("audio"),
    close: () => close(),
  };
  el.addEventListener("click", (e) => {
    const k = (e.target as HTMLElement).closest("button")?.dataset.k;
    if (k) act[k]();
    else if (e.target === m.element || e.target === el) act.toggle();
    wake();
  });
  seek.addEventListener("pointerdown", (e) => {
    const r = seek.getBoundingClientRect();
    if (m.duration) m.seek(((e.clientX - r.left) / r.width) * m.duration);
  });
  el.addEventListener("pointermove", wake);

  // Keys belong to the player while it is open; the template's own navigation must not see them.
  const keys = (e: KeyboardEvent) => {
    if (!document.body.contains(el)) return;
    const menuOpen = el.querySelector(".menu");
    const handled = (() => {
      switch (e.key) {
        case " ": case "k": act.toggle(); return true;
        case "Enter": if (document.activeElement === document.body || document.activeElement === m.element) { act.toggle(); return true; } return false;
        case "ArrowLeft": if (menuOpen) return false; act.back(); return true;
        case "ArrowRight": if (menuOpen) return false; act.fwd(); return true;
        case "ArrowUp": if (menuOpen) return false; m.volume = Math.min(1, m.volume + 0.1); return true;
        case "ArrowDown": if (menuOpen) return false; m.volume = Math.max(0, m.volume - 0.1); return true;
        case "m": m.muted = !m.muted; return true;
        case "c": if (!subsBtn.hidden) menu("subs"); return true;
        case "Escape": case "Backspace": if (menuOpen) { menuOpen.remove(); panels(); } else close(); return true;
      }
      return false;
    })();
    if (handled) { e.preventDefault(); e.stopImmediatePropagation(); }
    wake();
  };
  window.addEventListener("keydown", keys, true);

  // Gamepad: A toggles, B closes, D-pad left/right seeks (both engines; the overlay is a page too).
  const held = new Set<string>();
  let raf = 0;
  const poll = () => {
    for (const g of navigator.getGamepads?.() ?? []) {
      if (!g) continue;
      for (const [i, k] of [[0, "toggle"], [1, "close"], [14, "back"], [15, "fwd"]] as const) {
        const id = `${g.index}:${i}`;
        if (g.buttons[i]?.pressed) { if (!held.has(id)) { held.add(id); act[k](); wake(); } }
        else held.delete(id);
      }
    }
    raf = requestAnimationFrame(poll);
  };
  raf = requestAnimationFrame(poll);

  let closed = false;
  function close() {
    if (closed) return;
    closed = true;
    cancelAnimationFrame(raf);
    window.removeEventListener("keydown", keys, true);
    m.close();
    el.remove();
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
    o.onClose();
  }
  toggle.focus();
  paint();
  wake();
  return close;
}
