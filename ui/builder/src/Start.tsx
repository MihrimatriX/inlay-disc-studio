import { useState } from "react";
import { call } from "./host";
import { t } from "./i18n";
import { pickFiles, pickFolder } from "./actions";
import { Icon } from "./icons";
import { useDrop } from "./drop";
import { DriveCapsule } from "./DriveCapsule";
import { ToolPanel, type RunJob, type Tool } from "./Tools";
import type { View } from "./types";
import ui from "./ui.module.css";
import css from "./Start.module.css";

export type StartKind = "movie" | "music" | "photo" | "app" | "data";
const KINDS: StartKind[] = ["movie", "music", "photo", "app", "data"];

const TOOLS: { id: Tool | "recent"; icon: string }[] = [{ id: "copy", icon: "copy" }, { id: "iso", icon: "iso" }, { id: "erase", icon: "erase" }, { id: "recent", icon: "recent" }];

/** Başlangıç (design/screens/01): pick a disc kind, drop or choose a folder. The whole panel is the drop target. */
export function Start({ view, kind, setKind, openSettings, openProject, resume, runJob }: {
  view: View; kind: StartKind; setKind: (k: StartKind) => void; openSettings: () => void; openProject: () => void; resume: () => void; runJob: RunJob;
}) {
  const [tool, setTool] = useState<Tool | "recent" | null>(null);
  const recent = tool === "recent";
  const drop = useDrop();

  return (
    <div className={css.layout}>
      <nav aria-label={t("builder.start.nav")} className={css.nav}>
        <span className={css.group}>{t("builder.start.newDisc")}</span>
        {KINDS.map((k) => (
          <button key={k} className={!tool && kind === k ? css.itemActive : css.item} aria-current={!tool && kind === k ? "page" : undefined}
            onClick={() => { setKind(k); setTool(null); }}>
            <Icon name={k} />{t(`builder.start.kind.${k}`)}
          </button>
        ))}
        <span className={css.groupTools}>{t("builder.start.tools")}</span>
        {TOOLS.map((x) => (
          <button key={x.id} className={tool === x.id ? css.itemActive : css.item} aria-current={tool === x.id ? "page" : undefined} onClick={() => setTool(x.id)}>
            <Icon name={x.icon} />{t(x.id === "recent" ? "builder.start.recent" : `builder.tool.${x.id}.nav`)}
          </button>
        ))}
        <div className={ui.spacer} />
        <button className={css.itemSettings} onClick={openSettings}><Icon name="settings" />{t("builder.settings.title")}</button>
      </nav>

      {tool && tool !== "recent" ? <ToolPanel key={tool} tool={tool} view={view} runJob={runJob} /> : recent ? (
        <main className={css.panel}>
          <div className={css.recent}>
            <h1 className={css.h2}>{t("builder.start.recent")}</h1>
            {view.project.recent.length === 0 && <p className={css.lead}>{t("builder.recent.none")}</p>}
            <ul className={css.recentList}>
              {view.project.recent.map((r) => (
                <li key={r.path}>
                  <button className={css.recentItem} onClick={async () => { await call("project.open", { path: r.path }); resume(); }}>
                    <span className={css.recentDisc} aria-hidden="true" />
                    <span><b>{r.name}</b><span className={css.recentPath}>{r.path}</span></span>
                  </button>
                </li>
              ))}
            </ul>
            <div><button className={ui.secondary} onClick={openProject}>{t("builder.recent.open")}</button></div>
          </div>
        </main>
      ) : (
        <main className={drop.over ? css.panelOver : css.panel} {...drop.props}>
          {view.phase !== "empty" && (
            <button className={`${ui.tool} ${css.resume}`} onClick={resume}><Icon name="back" size={15} stroke={2} />{view.project.name}</button>
          )}
          <div className={css.light} aria-hidden="true" />
          <div className={`${ui.disc} ${css.disc}`} aria-hidden="true" />
          <div className={css.text}>
            <h1 className={css.h1}>{t(`builder.start.title.${kind}`)}</h1>
            <p className={css.lead}>{t(`builder.start.lead.${kind}`)}</p>
          </div>
          <div className={css.actions}>
            <button className={ui.primary} onClick={pickFolder}>{t("builder.start.pickFolder")}</button>
            <button className={ui.secondary} onClick={pickFiles}>{t("builder.start.addFiles")}</button>
          </div>
          <div className={css.drive}><DriveCapsule view={view} /></div>
        </main>
      )}
    </div>
  );
}
