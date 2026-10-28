import { call } from "./host";
import { t } from "./i18n";
import type { Setting } from "./types";
import ui from "./ui.module.css";
import css from "./SettingsForm.module.css";

/** §7.3 level 1: a form generated from theme.json → settings, grouped by `group`. */
export function SettingsForm({ settings }: { settings: Setting[] }) {
  const groups = [...new Set(settings.map((s) => s.group ?? ""))];
  const set = (key: string, value: unknown) => call("theme.setting", { key, value });
  return (
    <div className={css.form}>
      {groups.map((g) => (
        <section key={g} className={css.group}>
          <span className={ui.label}>{g || t("builder.settings.theme")}</span>
          {settings.filter((s) => (s.group ?? "") === g).map((s) => <Field key={s.key} s={s} set={(v) => set(s.key, v)} />)}
        </section>
      ))}
    </div>
  );
}

function Field({ s, set }: { s: Setting; set: (v: unknown) => void }) {
  const id = `set-${s.key}`;
  switch (s.type) {
    case "bool":
      return (
        <label className={css.row}><span>{s.label}</span>
          <input type="checkbox" className={ui.check} checked={s.value === true} onChange={(e) => set(e.target.checked)} /></label>
      );
    case "select":
    case "font":
      return s.options ? (
        <label className={css.field}><span className={css.name}>{s.label}</span>
          <select className={ui.select} value={String(s.value ?? "")} onChange={(e) => set(e.target.value)}>
            {s.options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select></label>
      ) : <Text s={s} set={set} />;
    case "range": {
      const v = typeof s.value === "number" ? s.value : Number(s.value ?? s.min ?? 0);
      return (
        <label className={css.field} htmlFor={id}><span className={css.name}>{s.label} <span className="num">{v}{s.unit ?? ""}</span></span>
          <input id={id} type="range" className={css.range} min={s.min} max={s.max} step={s.step ?? 1} value={v}
            onChange={(e) => set(Number(e.target.value))} /></label>
      );
    }
    case "color": {
      const auto = typeof s.value !== "string" || s.value.startsWith("auto");
      return (
        <div className={css.field}><span className={css.name}>{s.label}</span>
          <span className={css.color}>
            <label className={css.row}><input type="checkbox" className={ui.check} checked={auto}
              onChange={(e) => set(e.target.checked ? "auto" : "#f6f2ec")} />{t("builder.settings.auto")}</label>
            {!auto && <input type="color" aria-label={s.label} className={css.swatch} value={String(s.value)} onChange={(e) => set(e.target.value)} />}
          </span></div>
      );
    }
    case "image":
      return (
        <div className={css.field}><span className={css.name}>{s.label}</span>
          <span className={css.color}>
            <button className={ui.toolLined} onClick={async () => {
              const [p] = await call<string[]>("dialog.files", { filter: t("builder.filter.images") });
              if (p) await call("theme.image", { key: s.key, path: p });
            }}>{s.value ? t("builder.settings.imageChange") : t("builder.settings.imagePick")}</button>
            {Boolean(s.value) && <button className={ui.tool} onClick={() => set(null)}>{t("builder.settings.reset")}</button>}
          </span></div>
      );
    default:
      return <Text s={s} set={set} />;
  }
}

function Text({ s, set }: { s: Setting; set: (v: unknown) => void }) {
  return (
    <label className={css.field}><span className={css.name}>{s.label}</span>
      <input className={ui.input} defaultValue={String(s.value ?? "")} key={String(s.value ?? "")}
        onBlur={(e) => e.target.value !== String(s.value ?? "") && set(e.target.value)} /></label>
  );
}
