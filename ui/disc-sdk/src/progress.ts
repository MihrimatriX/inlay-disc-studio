// Watch positions for "Kaldığın yerden devam et" (§7.0). The launcher gives every disc its own WebView2 profile under
// %LOCALAPPDATA%\Inlay\Launcher\<disc>, so localStorage is per disc and lives where §9.4 wants it.
export interface Progress {
  positionSec: number;
  durationSec: number;
  watched: boolean;
  updatedAt: number;
}

const KEY = "inlay.progress.v1";
const WATCHED = 0.95;

function load(): Record<string, Progress> {
  try {
    return JSON.parse(localStorage.getItem(KEY) ?? "{}");
  } catch {
    return {};
  }
}

export function get(id: string): Progress | null {
  return load()[id] ?? null;
}

export function all(): Record<string, Progress> {
  return load();
}

/** Positions under 30 s or in the last 5 % are not worth resuming from. */
export function save(id: string, positionSec: number, durationSec: number) {
  if (!durationSec || !isFinite(durationSec)) return;
  const map = load();
  const watched = positionSec / durationSec >= WATCHED || map[id]?.watched === true;
  map[id] = { positionSec: watched || positionSec < 30 ? 0 : positionSec, durationSec, watched, updatedAt: Date.now() };
  try {
    localStorage.setItem(KEY, JSON.stringify(map));
  } catch {
    /* private mode or quota: resuming is a nicety */
  }
}
