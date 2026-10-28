// The page side of the WPF host protocol (src/Inlay.Builder/Rpc.cs): calls are {id, method, params} answered by
// {id, ok, result|error}; pushes are {event, data}.
import { useEffect, useState } from "react";
import type { Job, View } from "./types";

interface WebView {
  postMessage(m: unknown): void;
  postMessageWithAdditionalObjects(m: unknown, objects: ArrayLike<unknown>): void;
  addEventListener(type: "message", fn: (e: MessageEvent) => void): void;
}

const wv: WebView | undefined = (window as unknown as { chrome?: { webview?: WebView } }).chrome?.webview;
const pending = new Map<string, { resolve: (v: unknown) => void; reject: (e: Error) => void }>();
const listeners = new Map<string, Set<(data: never) => void>>();
let seq = 0;

wv?.addEventListener("message", (e) => {
  const m = e.data;
  if (m?.id !== undefined && pending.has(String(m.id))) {
    const p = pending.get(String(m.id))!;
    pending.delete(String(m.id));
    m.ok ? p.resolve(m.result) : p.reject(new Error(String(m.error)));
  } else if (typeof m?.event === "string") listeners.get(m.event)?.forEach((fn) => fn(m.data as never));
});

function send<T>(message: (id: string) => void): Promise<T> {
  const id = String(++seq);
  return new Promise<T>((resolve, reject) => {
    if (!wv) return reject(new Error("no host"));
    pending.set(id, { resolve: resolve as (v: unknown) => void, reject });
    message(id);
  });
}

export const call = <T = void>(method: string, params?: object) => send<T>((id) => wv!.postMessage({ id, method, params }));

/** Dropped files and folders: only the host can read their paths. */
export const drop = (files: FileList) => send<string[]>((id) => wv!.postMessageWithAdditionalObjects({ id, method: "content.drop" }, files));

export function on(event: "state", fn: (v: View) => void): () => void;
export function on(event: "job", fn: (j: Job) => void): () => void;
export function on(event: string, fn: (data: never) => void) {
  if (!listeners.has(event)) listeners.set(event, new Set());
  listeners.get(event)!.add(fn);
  return () => void listeners.get(event)!.delete(fn);
}

export function useView(): View | null {
  const [view, setView] = useState<View | null>(null);
  useEffect(() => {
    const off = on("state", setView);
    call<View>("state").then(setView, () => {});
    return off;
  }, []);
  return view;
}
