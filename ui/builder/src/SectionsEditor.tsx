import { useState } from "react";
import { call } from "./host";
import { t } from "./i18n";
import type { Layout, Section } from "./types";
import ui from "./ui.module.css";
import css from "./SectionsEditor.module.css";

const LAYOUTS: Layout[] = ["row", "grid", "hero", "list"];

/** §7.3 level 2: order (drag or Alt+↑/↓), title, layout and hiding of the menu's sections. */
export function SectionsEditor({ sections }: { sections: Section[] }) {
  const [dragging, setDragging] = useState<string | null>(null);
  const save = (list: Section[]) =>
    call("theme.sections", { sections: list.map((s) => ({ id: s.id, title: s.title, layout: s.layout ?? null, hidden: s.hidden })) });
  const move = (from: number, to: number) => {
    if (to < 0 || to >= sections.length || from === to) return;
    const list = [...sections];
    const [s] = list.splice(from, 1);
    list.splice(to, 0, s);
    return save(list);
  };
  const patch = (i: number, p: Partial<Section>) => save(sections.map((s, j) => (j === i ? { ...s, ...p } : s)));

  return (
    <section className={css.box}>
      <span className={ui.label}>{t("builder.sections.title")}</span>
      <ol className={css.list}>
        {sections.map((s, i) => (
          <li key={s.id} className={dragging === s.id ? css.itemDragging : css.item} draggable
            onDragStart={(e) => { setDragging(s.id); e.dataTransfer.effectAllowed = "move"; }}
            onDragEnd={() => setDragging(null)}
            onDragOver={(e) => { if (dragging) e.preventDefault(); }}
            onDrop={(e) => { e.preventDefault(); const from = sections.findIndex((x) => x.id === dragging); setDragging(null); move(from, i); }}
            onKeyDown={(e) => { if (e.altKey && (e.key === "ArrowUp" || e.key === "ArrowDown")) { e.preventDefault(); move(i, i + (e.key === "ArrowUp" ? -1 : 1)); } }}>
            <span className={css.handle} aria-hidden="true">⋮⋮</span>
            <span className={css.fields}>
              <input className={ui.input} aria-label={t("builder.sections.name")} defaultValue={s.title} key={s.title}
                onBlur={(e) => e.target.value.trim() && e.target.value !== s.title && patch(i, { title: e.target.value.trim() })} />
              <span className={css.line}>
                <select className={ui.select} aria-label={t("builder.sections.layout")} value={s.layout ?? "row"} onChange={(e) => patch(i, { layout: e.target.value as Layout })}>
                  {LAYOUTS.map((l) => <option key={l} value={l}>{t(`builder.layout.${l}`)}</option>)}
                </select>
                <label className={css.hide}><input type="checkbox" className={ui.check} checked={s.hidden} onChange={(e) => patch(i, { hidden: e.target.checked })} />{t("builder.sections.hide")}</label>
              </span>
              <span className={css.count}>{t("builder.sections.count", s.count)}</span>
            </span>
          </li>
        ))}
      </ol>
    </section>
  );
}
