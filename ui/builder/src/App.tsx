import { useCallback, useEffect, useRef, useState } from "react";
import { call, on, useView } from "./host";
import { setLocale, t } from "./i18n";
import { open, pickFolder, save, saveIsoPath } from "./actions";
import { Start, type StartKind } from "./Start";
import { Content } from "./Content";
import { Menu } from "./Menu";
import { Editor } from "./Editor";
import { JobDialog, type JobKind, type JobState } from "./JobDialog";
import { Settings } from "./Settings";
import type { BurnResult, View } from "./types";
import css from "./App.module.css";

export type Screen = "start" | "content" | "menu" | "editor";

/** The four screens of §11 flow into each other: no wizard, no step indicator. */
export function App() {
  const view = useView();
  const [screen, setScreen] = useState<Screen>("start");
  const [kind, setKind] = useState<StartKind>("movie");
  const [settings, setSettings] = useState(false);
  const [job, setJob] = useState<JobState | null>(null);
  const last = useRef<View | null>(null);

  useEffect(() => {
    if (!view) return;
    setLocale(view.prefs.locale);
    document.documentElement.dataset.accent = view.prefs.accent;
    const prev = last.current;
    // A drop on Başlangıç: the kind is recognised and the menu selection follows; the content opens (§3.1).
    if (view.kind && view.kind !== prev?.kind) setKind(view.kind);
    if (prev && prev.phase === "empty" && view.phase !== "empty") setScreen((s) => (s === "start" ? "content" : s));
    if (view.phase === "empty" && screen !== "start") setScreen("start");
    last.current = view;
  }, [view, screen]);

  useEffect(() => on("job", (j) => setJob((s) => (s ? { ...s, progress: j } : s))), []);

  const retry = useRef<() => void>(() => {});

  /** Runs one long host job behind the progress dialog; resolves with its result (null when it failed or was cancelled). */
  const runJob = useCallback(async <T,>(kind: JobKind, method: string, params: object = {}, path?: string): Promise<T | null> => {
    retry.current = () => void runJob(kind, method, params, path);
    setJob({ kind, path, started: Date.now(), progress: null, done: false, error: null });
    try {
      const result = await call<T>(method, { ...params, check: view?.prefs.verify ?? true });
      setJob((s) => (s ? { ...s, done: true, result: kind === "burn" || kind === "isoburn" ? (result as BurnResult) : null } : s));
      return result;
    } catch (e) {
      const msg = (e as Error).message;
      setJob((s) => (s ? (msg === "cancelled" ? null : { ...s, error: msg }) : s));
      return null;
    }
  }, [view?.prefs.verify]);

  const exportIso = useCallback(async () => {
    if (!view) return;
    const path = await saveIsoPath(view);
    if (path) await runJob("iso", "iso.export", { path }, path);
  }, [view, runJob]);

  const burn = useCallback(() => void runJob("burn", "burn.start"), [runJob]);
  const actions = { burn, iso: exportIso, tray: () => void call("disc.tray") };

  useEffect(() => void call("window.modal", { on: !!job || settings }).catch(() => {}), [job, settings]);

  useEffect(() => {
    const keys = async (e: KeyboardEvent) => {
      if (!view || job || settings) return;
      const k = e.key.toLowerCase();
      if (e.ctrlKey && k === "o") { e.preventDefault(); await pickFolder(); }
      else if (e.ctrlKey && k === "s") { e.preventDefault(); if (view.phase !== "empty") await save(view); }
      else if (e.ctrlKey && k === "b") { e.preventDefault(); if (view.capacity?.fits && !view.editor?.blocking) view.drives.some((d) => d.ready) ? burn() : await exportIso(); }
      else if (e.ctrlKey && k === "z" && !isTyping(e)) { e.preventDefault(); await call("undo"); }
      else if (e.ctrlKey && k === "y" && !isTyping(e)) { e.preventDefault(); await call("redo"); }
    };
    addEventListener("keydown", keys);
    return () => removeEventListener("keydown", keys);
  }, [view, job, settings, exportIso, burn]);

  if (!view) return null;

  const body = (() => {
    switch (screen) {
      case "content": return <Content view={view} go={setScreen} actions={actions} />;
      case "menu": return <Menu view={view} go={setScreen} actions={actions} />;
      case "editor": return <Editor view={view} go={setScreen} />;
      default: return <Start view={view} kind={kind} setKind={setKind} openSettings={() => setSettings(true)} openProject={async () => { if (await open()) setScreen("content"); }} resume={() => setScreen("content")} runJob={runJob} />;
    }
  })();

  return (
    <>
      <div className={screen === "start" ? css.appGlow : css.app} inert={!!job || settings}>
        {body}
        {view.error && <div role="alert" className={css.error}>{t("builder.error")}: {view.error}</div>}
      </div>
      {settings && <Settings view={view} close={() => setSettings(false)} />}
      {job && <JobDialog view={view} job={job} close={() => setJob(null)} again={() => retry.current()} />}
    </>
  );
}

const isTyping = (e: KeyboardEvent) => e.target instanceof HTMLElement && (/input|textarea|select/i.test(e.target.tagName) || e.target.isContentEditable);
