import { call } from "./host";
import { t } from "./i18n";
import { Icon } from "./icons";
import { Preview } from "./Preview";
import { SettingsForm } from "./SettingsForm";
import { SectionsEditor } from "./SectionsEditor";
import { BurnActions, type Actions } from "./BurnActions";
import type { Screen } from "./App";
import type { View } from "./types";
import ui from "./ui.module.css";
import css from "./Menu.module.css";

/** Disk menüsü (design/screens/03): live preview, template, what happens when the disc goes in, name and icon. */
export function Menu({ view, go, actions }: { view: View; go: (s: Screen) => void; actions: Actions }) {
  const m = view.menu;
  if (!m) return null;
  const blocked = !view.capacity?.fits || view.editor?.blocking || view.phase !== "ready";
  return (
    <>
      <div className={ui.toolbar}>
        <button className={ui.tool} onClick={() => go("content")}><Icon name="back" size={15} stroke={2} />{t("builder.content.title")}</button>
        <span className={css.here}>{t("builder.menu.title")}</span>
        <div className={ui.spacer} />
        <button className={ui.toolLined} onClick={() => go("editor")}>{t("builder.menu.editHtml")}</button>
      </div>

      <div className={css.layout}>
        <Preview src={m.preview} glow={m.glow} />
        <aside aria-label={t("builder.menu.settings")} className={`${ui.panel} ${css.aside}`}>
          <section className={css.group}>
            <span className={ui.label} id="tpl">{t("builder.menu.template")}</span>
            <div role="radiogroup" aria-labelledby="tpl" className={css.cards}
              onKeyDown={(e) => {
                if (!["ArrowRight", "ArrowLeft", "ArrowDown", "ArrowUp"].includes(e.key)) return;
                const items = [...e.currentTarget.querySelectorAll<HTMLButtonElement>("[role=radio]")];
                const i = items.indexOf(document.activeElement as HTMLButtonElement);
                const next = items[(i + (e.key === "ArrowRight" || e.key === "ArrowDown" ? 1 : items.length - 1)) % items.length];
                next.focus(); next.click(); e.preventDefault();
              }}>
              {m.templates.map((tpl) => (
                <button key={tpl.id} role="radio" aria-checked={tpl.selected} tabIndex={tpl.selected ? 0 : -1}
                  className={tpl.selected ? css.cardOn : css.card} onClick={() => !tpl.selected && call("menu.template", { id: tpl.id })}>
                  {tpl.thumb ? <img src={tpl.thumb} alt="" className={tpl.plain ? css.thumbPlain : css.thumb} /> : <span className={css.thumb} />}
                  {tpl.name}
                </button>
              ))}
            </div>
          </section>

          <label className={ui.field}>
            <span className={ui.label}>{t("builder.menu.onInsert")}</span>
            <select className={ui.select} value={m.autoplay ? "play" : "menu"} onChange={(e) => call("menu.set", { autoplay: e.target.value === "play" })}>
              <option value="menu">{t("builder.home.menu")}</option>
              <option value="play">{m.autoplayLabel}</option>
            </select>
          </label>

          <label className={ui.field}>
            <span className={ui.label}>{t("builder.menu.name")}</span>
            <input className={ui.input} defaultValue={m.title} key={m.title} maxLength={120}
              onBlur={(e) => e.target.value.trim() !== m.title && call("menu.set", { title: e.target.value })}
              onKeyDown={(e) => e.key === "Enter" && (e.target as HTMLInputElement).blur()} />
          </label>

          <div className={css.icon}>
            {m.icon ? <img src={m.icon} alt={t("builder.menu.icon")} className={css.iconImg} /> : <span className={css.iconImg} />}
            <span className={css.iconText}>
              {m.iconFromPoster ? t("builder.menu.iconFromPoster") : t("builder.menu.iconChosen")}
              <span className={css.iconLinks}>
                <button className={css.link} onClick={async () => {
                  const [p] = await call<string[]>("dialog.files", { filter: t("builder.filter.icons") });
                  if (p) await call("menu.set", { icon: p });
                }}>{t("builder.menu.change")}</button>
                {!m.iconFromPoster && <button className={css.link} onClick={() => call("menu.set", { icon: "" })}>{t("builder.menu.iconReset")}</button>}
              </span>
            </span>
          </div>

          <label className={css.toggle}>
            <span>{t("builder.menu.remote")}</span>
            <input type="checkbox" className={ui.check} checked={m.remote} onChange={(e) => call("menu.set", { remote: e.target.checked })} />
          </label>

          {view.editor && view.editor.settings.length > 0 && <SettingsForm settings={view.editor.settings} />}
          {view.editor && view.editor.sections.length > 1 && <SectionsEditor sections={view.editor.sections} />}
        </aside>
      </div>

      <footer className={css.footer}>
        <button className={ui.secondary} onClick={() => go("content")}>{t("builder.menu.done")}</button>
        <BurnActions view={view} actions={actions} blocked={!!blocked} />
      </footer>
    </>
  );
}
