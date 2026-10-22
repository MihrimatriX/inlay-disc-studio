import { useState } from "react";
import { call } from "./host";
import { t } from "./i18n";
import { currentDrive, DriveCapsule } from "./DriveCapsule";
import type { JobKind } from "./JobDialog";
import type { View } from "./types";
import ui from "./ui.module.css";
import css from "./Start.module.css";

export type Tool = "copy" | "iso" | "erase";
export type RunJob = <T>(kind: JobKind, method: string, params?: object, path?: string) => Promise<T | null>;

/** Araçlar (§3.1): the same calm panel as Başlangıç, one primary action at a time. */
export function ToolPanel({ tool, view, runJob }: { tool: Tool; view: View; runJob: RunJob }) {
  const [iso, setIso] = useState<string | null>(null);
  const [confirm, setConfirm] = useState(false);
  const drive = currentDrive(view);
  const tray = () => void call("disc.tray");
  const name = iso?.split(/[\\/]/).pop();

  const write = (path: string) => (drive?.ready
    ? <button className={ui.primary} onClick={() => runJob("isoburn", "burn.iso", { path }, path)}>{t("builder.burn.write")}</button>
    : <button className={ui.primary} onClick={tray} disabled={!drive}>{t("builder.burn.insert")}</button>);

  const body = (() => {
    switch (tool) {
      case "iso":
        return {
          title: t("builder.tool.iso.title"),
          lead: name ? t("builder.tool.iso.chosen", name) : t("builder.tool.iso.lead"),
          actions: <>
            {iso ? write(iso) : <button className={ui.primary} onClick={pick}>{t("builder.tool.iso.pick")}</button>}
            {iso && <button className={ui.secondary} onClick={pick}>{t("builder.tool.iso.other")}</button>}
          </>,
        };
      case "erase":
        return {
          title: t("builder.tool.erase.title"),
          lead: drive && !drive.erasable && drive.ready ? t("builder.tool.erase.blank") : t("builder.tool.erase.lead"),
          actions: confirm
            ? <>
                <button className={ui.primary} onClick={() => { setConfirm(false); void runJob("erase", "disc.erase"); }}>{t("builder.tool.erase.yes")}</button>
                <button className={ui.secondary} onClick={() => setConfirm(false)}>{t("builder.tool.erase.no")}</button>
              </>
            : <button className={ui.primary} onClick={() => setConfirm(true)} disabled={!drive?.erasable}>{t("builder.tool.erase.action")}</button>,
        };
      default:
        return iso
          ? { title: t("builder.tool.copy.title"), lead: t("builder.tool.copy.insertBlank"), actions: <>{write(iso)}<button className={ui.secondary} onClick={() => setIso(null)}>{t("builder.tool.copy.restart")}</button></> }
          : {
              title: t("builder.tool.copy.title"),
              lead: t("builder.tool.copy.lead"),
              actions: <button className={ui.primary} disabled={!drive?.readable}
                onClick={async () => { const path = await runJob<string>("read", "disc.read", { recorder: drive!.id }); if (path) setIso(path); }}>{t("builder.tool.copy.read")}</button>,
            };
    }
  })();

  async function pick() {
    const [p] = await call<string[]>("dialog.files", { filter: t("builder.filter.iso") });
    if (p) setIso(p);
  }

  return (
    <main className={css.panel}>
      <div className={css.light} aria-hidden="true" />
      <div className={`${ui.disc} ${css.disc}`} aria-hidden="true" />
      <div className={css.text}>
        <h1 className={css.h1}>{body.title}</h1>
        <p className={css.lead}>{body.lead}</p>
        {confirm && tool === "erase" && <p className={`${css.lead} ${ui.warn}`} role="alert">{t("builder.tool.erase.warn")}</p>}
      </div>
      <div className={css.actions}>{body.actions}</div>
      <div className={css.drive}><DriveCapsule view={view} /></div>
    </main>
  );
}
