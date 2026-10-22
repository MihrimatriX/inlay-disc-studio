// The controls page in the launcher's overlay window above LibVLC's video, clipped to its panels (§9.4, ADR 0012). Same openPlayer as the
// html engine; the media is LibVLC, driven by bridge commands and painted from the launcher's "player" events.
import { openPlayer, type MediaLike, type Track } from "./player";
import type { Transport } from "./sdk";

interface RemoteTrack { id: number; label: string; active: boolean }
interface RemoteState {
  status: string;
  positionSec: number;
  durationSec: number;
  volume: number;
  audio?: RemoteTrack[];
  subtitles?: RemoteTrack[];
}

export function startOverlay(title: string, locale: string, transport: Transport) {
  document.documentElement.lang = locale.slice(0, 2);
  let st: RemoteState = { status: "loading", positionSec: 0, durationSec: 0, volume: 1 };
  const listeners: ((ended: boolean) => void)[] = [];
  let req = 1;
  let volumeBeforeMute = 1;
  const send = (cmd: string, opts?: object) => transport.send({ v: 1, req: req++, cmd, opts });

  transport.listen((m) => {
    if (m?.v !== 1) return;
    // The launcher sees the mouse move over the video (the clipped overlay does not) and wakes the controls.
    if (m.event === "wake") return void document.querySelector(".ip")?.dispatchEvent(new PointerEvent("pointermove", { bubbles: true }));
    if (m.event !== "player") return;
    st = m.data;
    listeners.forEach((f) => f(st.status === "ended"));
  });

  const tracks = (list: RemoteTrack[] | undefined, cmd: string): Track[] =>
    (list ?? []).map((t) => ({ label: t.label, active: t.active, select: () => send(cmd, { id: t.id }) }));

  const media: MediaLike = {
    get paused() { return st.status !== "playing" && st.status !== "loading"; },
    get currentTime() { return st.positionSec; },
    get duration() { return st.durationSec; },
    get volume() { return st.volume; },
    set volume(v) { send("player.setVolume", { volume: v }); },
    get muted() { return st.volume === 0; },
    set muted(on) {
      if (on) volumeBeforeMute = st.volume || 1;
      send("player.setVolume", { volume: on ? 0 : volumeBeforeMute });
    },
    toggle: () => send(media.paused ? "player.resume" : "player.pause"),
    seek: (sec) => send("player.seek", { sec: Math.max(0, sec) }),
    close: () => send("player.stop"),
    subtitles: () => tracks(st.subtitles, "player.subtitle"),
    subtitlesOff: () => send("player.subtitle", { id: -1 }),
    audio: () => tracks(st.audio, "player.audio"),
    onChange: (fn) => listeners.push(fn),
  };
  openPlayer({
    title, media, onClose: () => {},
    onPanels: (rects) => send("overlay.region", { rects, scale: devicePixelRatio }),
  });
}
