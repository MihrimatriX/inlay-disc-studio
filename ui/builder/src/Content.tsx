import { useEffect, useRef, useState, type KeyboardEvent, type MouseEvent } from "react";
import { call } from "./host";
import { t } from "./i18n";
import { pickFiles } from "./actions";
import { size } from "./format";
import { Icon } from "./icons";
import { useDrop } from "./drop";
import { Footer } from "./Footer";
import type { Actions } from "./BurnActions";
import { MatchCard } from "./MatchCard";
import type { Screen } from "./App";
import type { ContentKind, Row, View } from "./types";
import ui from "./ui.module.css";
import css from "./Content.module.css";

const KINDS: ContentKind[] = ["movie", "series", "album", "gallery", "app", "document"];

/** Disk içeriği (design/screens/02): what goes on the disc, corrections, matches, the capacity ruler. */
export function Content({ view, go, actions }: { view: View; go: (s: Screen) => void; actions: Actions }) {
  const [selected, setSelected] = useState<string[]>([]);
  const [menu, setMenu] = useState<{ id: string; x: number; y: number } | null>(null);
  const [renaming, setRenaming] = useState<string | null>(null);
  const drop = useDrop();
  const table = useRef<HTMLTableElement>(null);

  useEffect(() => setSelected((s) => s.filter((id) => view.rows.some((r) => r.id === id))), [view.rows]);

  const remove = async () => {
    if (selected.length === 0) return;
    await call("content.remove", { ids: selected });
    setSelected([]);
  };

  const onKey = async (e: KeyboardEvent) => {
    if (renaming) return;
    if (e.key === "Delete") { e.preventDefault(); await remove(); }
    else if (e.key === "F2" && selected.length === 1 && !view.rows.find((r) => r.id === selected[0])?.parent) { e.preventDefault(); setRenaming(selected[0]); }
  };

  const select = (r: Row, e: MouseEvent) =>
    setSelected((s) => (e.ctrlKey ? (s.includes(r.id) ? s.filter((x) => x !== r.id) : [...s, r.id]) : [r.id]));

  return (
    <>
      <div className={ui.toolbar}>
        <button className={ui.tool} onClick={() => go("start")}><Icon name="back" size={15} stroke={2} />{t("builder.back")}</button>
        <span className={ui.divider} />
        <button className={ui.toolFilled} onClick={pickFiles}><Icon name="add" size={15} stroke={2} />{t("builder.content.add")}</button>
        <button className={ui.tool} onClick={remove} disabled={selected.length === 0}><Icon name="remove" size={15} stroke={2} />{t("builder.content.remove")}</button>
        <div className={ui.spacer} />
        {view.phase === "analyzing" && <span className={ui.note} role="status">{t("builder.content.analyzing")}</span>}
        <button className={ui.toolLined} onClick={() => go("menu")} disabled={!view.menu}><Icon name="screen" size={15} stroke={1.8} />{t("builder.menu.title")}</button>
      </div>

      <main className={drop.over ? css.mainOver : css.main} {...drop.props}>
        {view.project.missing.length > 0 && <Missing view={view} />}
        <div className={css.scroll}>
          <table ref={table} className={css.table} onKeyDown={onKey}>
            <thead>
              <tr>
                <th>{t("builder.content.name")}</th>
                <th className={css.kindCol}>{t("builder.content.kind")}</th>
                <th className={css.sizeCol}>{t("builder.content.size")}</th>
              </tr>
            </thead>
            <tbody>
              {view.rows.map((r) => (
                <RowView key={r.id} row={r} selected={selected.includes(r.id)} renaming={renaming === r.id}
                  onSelect={(e) => select(r, e)}
                  onMenu={(e) => { if (r.parent) return; e.preventDefault(); setSelected([r.id]); setMenu({ id: r.id, x: e.clientX, y: e.clientY }); }}
                  onRename={async (title) => { setRenaming(null); if (title !== null) await call("content.rename", { id: r.id, title }); }} />
              ))}
            </tbody>
          </table>
          {view.rows.length === 0 && view.phase === "analyzing" && <p className={css.empty}>{t("builder.content.analyzing")}</p>}
        </div>
        <div className={css.drop}>{t("builder.content.drop")}</div>
      </main>

      <Footer view={view} actions={actions} />

      {menu && <KindMenu x={menu.x} y={menu.y} close={() => setMenu(null)}
        pick={async (kind) => { setMenu(null); await call("content.kind", { id: menu.id, kind }); }} />}
    </>
  );
}

function RowView({ row, selected, renaming, onSelect, onMenu, onRename }: {
  row: Row; selected: boolean; renaming: boolean; onSelect: (e: MouseEvent) => void; onMenu: (e: MouseEvent) => void; onRename: (t: string | null) => void;
}) {
  const cls = [row.accent || selected ? css.rowAccent : css.row, row.parent ? css.part : ""].join(" ");
  return (
    <>
      <tr className={cls} aria-selected={selected} tabIndex={0} onClick={onSelect} onContextMenu={onMenu}>
        <td>
          <span className={css.name}>
            {row.thumb ? <img src={row.thumb} alt="" className={row.accent ? css.thumbMain : css.thumb} loading="lazy" />
              : <span className={css.thumbBox} aria-hidden="true">{row.badge}</span>}
            <span className={css.titleCell}>
              {renaming ? <Rename initial={row.title} done={onRename} />
                : row.accent ? <b className={css.title}>{row.title}</b> : <span>{row.title}</span>}
              {row.info && <span className={css.info}>{row.info}</span>}
              {row.note && <span className={css.noteLine}>{row.note}</span>}
            </span>
          </span>
        </td>
        <td><span className={row.accent ? ui.pillAccent : ui.pill}>{row.kindLabel}</span></td>
        <td className={`num ${row.accent ? css.size : css.sizeMuted}`}>{size(row.bytes)}</td>
      </tr>
      {row.match && (
        <tr className={css.matchRow}><td colSpan={3}><MatchCard row={row} /></td></tr>
      )}
    </>
  );
}

function Rename({ initial, done }: { initial: string; done: (t: string | null) => void }) {
  const [value, setValue] = useState(initial);
  return (
    <input className={`${ui.input} ${css.rename}`} autoFocus value={value} aria-label={t("builder.content.rename")}
      onChange={(e) => setValue(e.target.value)} onBlur={() => done(value.trim() || null)} onClick={(e) => e.stopPropagation()}
      onKeyDown={(e) => { e.stopPropagation(); if (e.key === "Enter") done(value.trim() || null); if (e.key === "Escape") done(null); }} />
  );
}

/** Tür düzeltme (§3.2): the row's context menu. */
function KindMenu({ x, y, pick, close }: { x: number; y: number; pick: (k: ContentKind) => void; close: () => void }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    ref.current?.querySelector("button")?.focus();
    const away = (e: PointerEvent) => { if (!ref.current?.contains(e.target as Node)) close(); };
    addEventListener("pointerdown", away);
    return () => removeEventListener("pointerdown", away);
  }, [close]);
  return (
    <div ref={ref} role="menu" className={css.menu} style={{ left: x, top: y }}
      onKeyDown={(e) => {
        if (e.key === "Escape") close();
        if (e.key === "ArrowDown" || e.key === "ArrowUp") {
          const items = [...ref.current!.querySelectorAll("button")];
          const i = items.indexOf(document.activeElement as HTMLButtonElement);
          items[(i + (e.key === "ArrowDown" ? 1 : items.length - 1)) % items.length]?.focus();
          e.preventDefault();
        }
      }}>
      <span className={css.menuLabel}>{t("builder.content.kindIs")}</span>
      {KINDS.map((k) => <button key={k} role="menuitem" onClick={() => pick(k)}>{t(`builder.correct.${k}`)}</button>)}
    </div>
  );
}

/** "Kaynağı yeniden bağla" (§7.4): a source folder moved since the project was saved. */
function Missing({ view }: { view: View }) {
  return (
    <div className={css.missing} role="alert">
      {view.project.missing.map((m) => (
        <span key={m} className={css.missingLine}>
          <span>{t("builder.relink.missing", m)}</span>
          <button className={ui.toolLined} onClick={async () => {
            const [now] = await call<string[]>("dialog.folder");
            if (now) await call("project.relink", { old: m, now });
          }}>{t("builder.relink.action")}</button>
        </span>
      ))}
    </div>
  );
}
