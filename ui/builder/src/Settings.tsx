import { useEffect, useRef, useState } from "react";
import { call } from "./host";
import { t } from "./i18n";
import type { View } from "./types";
import ui from "./ui.module.css";
import css from "./Settings.module.css";

const ACCENTS = ["orange", "blue", "purple"] as const;

/** Ayarlar: accent colour and, under Gelişmiş, every technical option (§11: nowhere else). */
export function Settings({ view, close }: { view: View; close: () => void }) {
  const p = view.prefs;
  const box = useRef<HTMLElement>(null);
  const set = (edit: object) => call("prefs.set", edit);
  useEffect(() => { box.current?.querySelector<HTMLElement>("input, button")?.focus(); }, []);

  return (
    <div className={css.backdrop} onKeyDown={(e) => e.key === "Escape" && close()}>
      <section ref={box} role="dialog" aria-modal="true" aria-labelledby="settings-h" className={css.dialog}>
        <h1 id="settings-h" className={css.h1}>{t("builder.settings.title")}</h1>
        <div className={css.body}>
          <fieldset className={css.group}>
            <legend className={ui.label}>{t("builder.settings.accent")}</legend>
            <div className={css.accents}>
              {ACCENTS.map((a) => (
                <label key={a} className={css.accent}>
                  <input type="radio" name="accent" checked={p.accent === a} onChange={() => set({ accent: a })} />
                  <span className={{ orange: css.swOrange, blue: css.swBlue, purple: css.swPurple }[a]} aria-hidden="true" />{t(`builder.settings.accent.${a}`)}
                </label>
              ))}
            </div>
          </fieldset>

          <fieldset className={css.group}>
            <legend className={ui.label}>{t("builder.settings.online")}</legend>
            <p className={ui.note}>{t("builder.settings.onlineHelp")}</p>
            {[...new Set(Object.keys(p.keys).map(service))].map((s) => <Service key={s} name={s} saved={p.keys} />)}
          </fieldset>

          <fieldset className={css.group}>
            <legend className={ui.label}>{t("builder.settings.advanced")}</legend>
            {!p.launcherSigned && <p className={`${ui.note} ${ui.warn}`} role="note">{t("builder.adv.unsigned")}</p>}
            <label className={css.row}><span>{t("builder.adv.keepOriginals")}<small>{t("builder.adv.keepOriginalsHelp")}</small></span>
              <input type="checkbox" className={ui.check} checked={p.keepOriginals} onChange={(e) => set({ keepOriginals: e.target.checked })} /></label>
            <label className={css.row}><span>{t("builder.adv.maxCompat")}<small>{t("builder.adv.maxCompatHelp")}</small></span>
              <input type="checkbox" className={ui.check} checked={p.maxCompatibility} onChange={(e) => set({ maxCompatibility: e.target.checked })} /></label>
            <label className={css.field}><span>{t("builder.adv.encoder")}</span>
              <select className={ui.select} value={p.encoder} onChange={(e) => set({ encoder: e.target.value })}>
                {["software", "nvenc", "qsv", "amf"].map((v) => <option key={v} value={v}>{t(`builder.adv.encoder.${v}`)}</option>)}
              </select></label>
            <label className={css.field}><span>{t("builder.adv.udf")}</span>
              <select className={ui.select} value={p.udfRevision} onChange={(e) => set({ udfRevision: Number(e.target.value) })}>
                {[0x102, 0x201, 0x250].map((v) => <option key={v} value={v}>UDF {(v >> 8)}.{(v & 0xff).toString(16).padStart(2, "0")}</option>)}
              </select></label>
            <label className={css.field}><span>{t("builder.adv.drive")}</span>
              <select className={ui.select} value={p.recorder ?? ""} onChange={(e) => set({ recorder: e.target.value })}>
                <option value="">{t("builder.adv.driveAuto")}</option>
                {view.drives.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
              </select></label>
            <label className={css.field}><span>{t("builder.adv.speed")}</span>
              <select className={ui.select} value={p.speed < 0 ? -1 : 0} onChange={(e) => set({ speed: Number(e.target.value) })}>
                <option value={0}>{t("builder.adv.speed.recommended")}</option>
                <option value={-1}>{t("builder.adv.speed.slowest")}</option>
              </select></label>
            <label className={css.row}><span>{t("builder.adv.verify")}</span>
              <input type="checkbox" className={ui.check} checked={p.verify} onChange={(e) => set({ verify: e.target.checked })} /></label>
          </fieldset>
        </div>
        <div className={css.buttons}><button className={ui.secondary} onClick={close}>{t("builder.job.close")}</button></div>
      </section>
    </div>
  );
}

/** "igdb.clientSecret" → "igdb": the service a key belongs to (Session.KeyNames). */
const service = (key: string) => key.split(".")[0];

/** A service's key fields and "Test", which makes one live request with the saved keys. */
function Service({ name, saved }: { name: string; saved: Record<string, boolean> }) {
  const fields = Object.keys(saved).filter((k) => service(k) === name);
  const ready = fields.every((k) => saved[k]);
  const [result, setResult] = useState<string>();
  useEffect(() => setResult(undefined), [fields.map((k) => saved[k]).join()]);
  const test = async () => {
    setResult("testing");
    setResult(await call<string>("keys.test", { service: name }).catch(() => "unreachable"));
  };
  return (
    <div className={css.service}>
      {fields.map((k) => <KeyField key={k} name={k} saved={saved[k]} />)}
      <div className={css.test}>
        <button type="button" className={ui.toolLined} disabled={!ready || result === "testing"} onClick={test}>{t("builder.keys.test")}</button>
        {result && <span role="status" className={result === "ok" ? css.saved : result === "testing" ? ui.note : `${ui.note} ${ui.warn}`}>{t(`builder.keys.${result}`)}</span>}
      </div>
    </div>
  );
}

/** Keys are write-only from here: the page learns only whether one is saved (DPAPI KeyStore). */
function KeyField({ name, saved }: { name: string; saved: boolean }) {
  const [value, setValue] = useState("");
  return (
    <form className={css.key} onSubmit={async (e) => { e.preventDefault(); await call("keys.set", { name, value }); setValue(""); }}>
      <label className={css.field}><span>{t(`builder.keys.${name}`)}{saved && <em className={css.saved}> · {t("builder.keys.saved")}</em>}</span>
        <input className={ui.input} type="password" autoComplete="off" value={value} placeholder={saved ? "••••••••" : ""} onChange={(e) => setValue(e.target.value)} /></label>
      <button type="submit" className={ui.toolFilled} disabled={!value}>{t("builder.keys.save")}</button>
      {saved && <button type="button" className={ui.tool} onClick={() => call("keys.set", { name })}>{t("builder.keys.remove")}</button>}
    </form>
  );
}
