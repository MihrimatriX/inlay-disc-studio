import { useEffect, useMemo, useRef, useState } from "react";
import { call } from "./host";
import { t } from "./i18n";
import { save } from "./actions";
import { Icon } from "./icons";
import { Preview } from "./Preview";
import type { CodeEditor } from "../../editor/src/editor";
import type { Screen } from "./App";
import type { View } from "./types";
import ui from "./ui.module.css";
import css from "./Editor.module.css";

/** "HTML olarak düzenle" (§3.6, §7.3 level 3): code left, the same live render right, data and warnings below. */
export function Editor({ view, go }: { view: View; go: (s: Screen) => void }) {
  const ed = view.editor;
  const host = useRef<HTMLDivElement>(null);
  const cm = useRef<CodeEditor | null>(null);
  const [file, setFile] = useState("index.liquid");
  const [data, setData] = useState<unknown>(null);
  const vars = useRef<string[]>([]);
  const timer = useRef<number>(undefined);
  const user = !!ed?.user;
  const fileRef = useRef(file);
  fileRef.current = file;

  const write = () => cm.current && user && call("theme.write", { path: fileRef.current, text: cm.current.text() });

  useEffect(() => {
    let alive = true;
    import("../../editor/src/editor").then(({ createEditor }) => {
      if (!alive || !host.current) return;
      cm.current = createEditor(host.current, {
        label: t("builder.editor.code"),
        vars: () => vars.current,
        onSave: () => { clearTimeout(timer.current); void write(); void save(view); },
        // Typing re-renders the preview shortly after the last key (auto-save into the project, §7.3).
        onChange: () => { clearTimeout(timer.current); timer.current = window.setTimeout(() => void write(), 700); },
      });
      setFile((f) => (ed?.files.includes(f) ? f : ed?.files[0] ?? f));
      void load();
    });
    return () => { alive = false; clearTimeout(timer.current); cm.current?.destroy(); cm.current = null; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const load = async () => {
    if (!cm.current || !ed) return;
    const path = ed.files.includes(fileRef.current) ? fileRef.current : ed.files[0];
    cm.current.open(path, await call<string>("theme.read", { path }), !ed.user);
  };
  useEffect(() => void load(), [file, user]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    call<string>("editor.data").then((json) => {
      const d = JSON.parse(json);
      setData(d);
      vars.current = variables(d);
    });
  }, [view.menu?.preview]);

  if (!ed || !view.menu) return null;
  const errors = ed.issues.filter((i) => i.severity === "error").length;

  return (
    <>
      <div className={ui.toolbar}>
        <button className={ui.tool} onClick={() => { void write(); go("menu"); }}><Icon name="back" size={15} stroke={2} />{t("builder.menu.title")}</button>
        <span className={css.here}>{ed.themeName}</span>
        <select className={`${ui.select} ${css.file}`} aria-label={t("builder.editor.file")} value={file}
          onChange={(e) => { void write(); setFile(e.target.value); }}>
          {ed.files.map((f) => <option key={f} value={f}>{f}</option>)}
        </select>
        <div className={ui.spacer} />
        <button className={ui.tool} onClick={async () => {
          const [p] = await call<string[]>("dialog.files", { filter: t("builder.filter.theme") });
          if (p) await call("theme.import", { path: p });
        }}>{t("builder.editor.import")}</button>
        <button className={ui.tool} onClick={async () => {
          const p = await call<string>("dialog.save", { filter: t("builder.filter.theme"), name: ed.themeName });
          if (p) await call("theme.export", { path: p });
        }}>{t("builder.editor.export")}</button>
        {user
          ? <button className={ui.primary} onClick={async () => { clearTimeout(timer.current); await write(); await save(view); }}>{t("builder.editor.save")}</button>
          : <button className={ui.primary} onClick={() => call("theme.copy")}>{t("builder.editor.copy")}</button>}
      </div>

      {!user && <p className={css.banner}>{t("builder.editor.standard")}</p>}

      <div className={css.layout}>
        <div className={`${ui.panel} ${css.code}`} ref={host} />
        <Preview src={view.menu.preview} max={1280} />
        <section className={`${ui.panel} ${css.data}`} aria-label={t("builder.editor.data")}>
          <span className={ui.label}>{t("builder.editor.data")}</span>
          <div className={css.tree}>{data !== null && <Tree data={data} insert={(v) => cm.current?.insert(`{{ ${v} }}`)} />}</div>
        </section>
        <section className={`${ui.panel} ${css.lint}`} aria-label={t("builder.editor.issues")} aria-live="polite">
          <span className={ui.label}>{t("builder.editor.issues")}{ed.issues.length > 0 && <span className="num"> · {ed.issues.length}</span>}</span>
          {ed.issues.length === 0 && <span className={css.clean}><Icon name="check" size={16} stroke={2} />{t("builder.editor.clean")}</span>}
          <ul className={css.issues}>
            {ed.issues.map((i, n) => (
              <li key={n} className={i.severity === "error" ? css.error : css.warning}><b>{t(`builder.lint.${i.severity}`)}</b> {i.message}</li>
            ))}
          </ul>
          {errors > 0 && <span className={ui.warn}>{t("builder.editor.blocking")}</span>}
        </section>
      </div>
    </>
  );
}

/** Liquid paths the template can read for each JSON node (§7.1.2 model): items and actions by id. */
function pathOf(trail: (string | number)[], root: Record<string, unknown>): string {
  const [head, ...rest] = trail;
  let out: string;
  let node: unknown = root[head as string];
  if ((head === "items" || head === "actions") && typeof rest[0] === "number") {
    const id = (node as { id: string }[])[rest[0]].id;
    out = `${head}['${id}']`;
    node = (node as unknown[])[rest[0]];
    rest.shift();
  } else out = String(head);
  for (const k of rest) out += typeof k === "number" ? `[${k}]` : `.${k}`;
  return out;
}

function variables(d: Record<string, unknown>): string[] {
  const out = new Set<string>(["settings", "hosted", "theme.palette", "text"]);
  const walk = (v: unknown, trail: (string | number)[], depth: number) => {
    if (depth > 4) return;
    if (trail.length > 0) out.add(pathOf(trail, d));
    if (Array.isArray(v)) v.slice(0, 3).forEach((x, i) => walk(x, [...trail, i], depth + 1));
    else if (v && typeof v === "object") for (const [k, x] of Object.entries(v)) walk(x, [...trail, k], depth + 1);
  };
  for (const k of ["disc", "items", "sections", "actions", "credits"]) walk(d[k], [k], 0);
  for (const k of Object.keys((d.theme as { palette?: object })?.palette ?? {})) out.add(`theme.palette.${k}`);
  return [...out];
}

function Tree({ data, insert }: { data: unknown; insert: (path: string) => void }) {
  const root = data as Record<string, unknown>;
  const top = useMemo(() => ["disc", "items", "sections", "actions"].filter((k) => k in root), [root]);
  return <ul className={css.treeList}>{top.map((k) => <Node key={k} name={k} value={root[k]} trail={[k]} root={root} insert={insert} open />)}</ul>;
}

function Node({ name, value, trail, root, insert, open = false }: {
  name: string; value: unknown; trail: (string | number)[]; root: Record<string, unknown>; insert: (p: string) => void; open?: boolean;
}) {
  const [expanded, setExpanded] = useState(open);
  const leaf = value === null || typeof value !== "object";
  if (leaf) {
    return (
      <li><button className={css.leaf} title={pathOf(trail, root)} onClick={() => insert(pathOf(trail, root))}>
        <span className={css.key}>{name}</span> <span className={css.val}>{String(value).slice(0, 60)}</span></button></li>
    );
  }
  const entries: [string, unknown, string | number][] = Array.isArray(value)
    ? value.map((v, i) => [String((v as { id?: string })?.id ?? i), v, i])
    : Object.entries(value as object).map(([k, v]) => [k, v, k]);
  return (
    <li>
      <button className={css.branch} aria-expanded={expanded} onClick={() => setExpanded(!expanded)}>
        <span className={css.caret}>{expanded ? "▾" : "▸"}</span><span className={css.key}>{name}</span>
        <span className={css.val}>{Array.isArray(value) ? `[${value.length}]` : ""}</span>
      </button>
      {expanded && <ul className={css.treeList}>{entries.map(([label, v, k]) => <Node key={String(k)} name={label} value={v} trail={[...trail, k]} root={root} insert={insert} />)}</ul>}
    </li>
  );
}
