// The host's View (src/Inlay.Builder/Session.View.cs), as JSON: camelCase names, enums as camelCase strings.

export type DiscMedia = "cd700" | "dvd5" | "dvd9";
export type ContentKind = "movie" | "series" | "album" | "gallery" | "app" | "document";

export interface View {
  project: { name: string; path?: string; named: boolean; missing: string[]; canUndo: boolean; canRedo: boolean; recent: { path: string; name: string }[] };
  phase: "empty" | "analyzing" | "ready";
  progress?: number;
  error?: string;
  kind?: "movie" | "music" | "photo" | "app" | "data";
  rows: Row[];
  capacity?: Capacity;
  menu?: Menu;
  editor?: Editor;
  drives: Drive[];
  prefs: Prefs;
}

export interface Candidate { provider: string; id: string; title: string; year?: number; detail?: string; thumbUrl?: string }

export interface Row {
  id: string;
  parent?: string;
  title: string;
  info?: string;
  thumb?: string;
  badge?: string;
  kind: string;
  kindLabel: string;
  accent: boolean;
  bytes: number;
  note?: string;
  match?: { question: "movie" | "series" | "game"; candidates: Candidate[]; notFound: boolean };
}

export interface Capacity { used: number; media: DiscMedia; usable: number; fits: boolean; over: number; needs?: DiscMedia; suggestions: { index: number; text: string }[] }

export interface Menu {
  templates: { id: string; name: string; thumb?: string; selected: boolean; plain: boolean }[];
  autoplay: boolean;
  autoplayLabel: string;
  title: string;
  label: string;
  icon?: string;
  iconFromPoster: boolean;
  remote: boolean;
  preview: string;
  glow?: string;
}

export type SettingType = "color" | "bool" | "font" | "select" | "range" | "image" | "text";

export interface Setting {
  key: string;
  type: SettingType;
  label: string;
  help?: string;
  group?: string;
  options?: { value: string; label: string }[];
  min?: number;
  max?: number;
  step?: number;
  unit?: string;
  value?: unknown;
}

export type Layout = "row" | "grid" | "hero" | "list";
export interface Section { id: string; kind: string; title: string; layout?: Layout; hidden: boolean; count: number }
export interface Issue { severity: "warning" | "error"; rule: string; message: string }

export interface Editor { user: boolean; themeName: string; files: string[]; settings: Setting[]; sections: Section[]; issues: Issue[]; blocking: boolean }

export interface Drive { id: string; letter: string; name: string; status: string; ready: boolean; readable: boolean; erasable: boolean; free: number; fits?: DiscMedia }

export interface Prefs {
  accent: "orange" | "blue" | "purple";
  locale: string;
  keepOriginals: boolean;
  maxCompatibility: boolean;
  encoder: "software" | "nvenc" | "qsv" | "amf";
  udfRevision: number;
  speed: number;
  verify: boolean;
  recorder?: string;
  keys: Record<string, boolean>;
  launcherSigned: boolean;
}

export type Stage = "prepare" | "hash" | "write" | "finalize" | "verify" | "read" | "erase" | "done";
export interface Job { stage: Stage; value: number; doneBytes: number; totalBytes: number; speedX?: number; remainingSec?: number }
export interface BurnResult { checked: boolean; files: number; mismatched: string[]; missing: string[]; readMBps: number[] }
