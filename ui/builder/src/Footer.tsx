import { call } from "./host";
import { t } from "./i18n";
import { gb, mediaLabel, NOMINAL, rulerPos, size } from "./format";
import { BurnActions, DriveHint, type Actions } from "./BurnActions";
import type { View } from "./types";
import ui from "./ui.module.css";
import css from "./Footer.module.css";

/** Doluluk cetveli + "Diske yaz" (§2.4, §3.5): CD / DVD / DVD DL marks on a linear 0–8,5 GB scale. */
export function Footer({ view, actions }: { view: View; actions: Actions }) {
  const c = view.capacity;
  const blocked = !c || !c.fits || view.editor?.blocking || view.phase !== "ready";
  return (
    <footer className={css.footer}>
      <div className={css.ruler}>
        {c && <Ruler used={c.used} media={c.media} fits={c.fits} />}
        {c && !c.fits && c.suggestions.length > 0 && (
          <div className={css.suggestions}>
            {c.suggestions.slice(0, 3).map((s) => (
              <button key={s.index} className={ui.toolLined} onClick={() => call("content.suggest", { index: s.index })}>{s.text}</button>
            ))}
          </div>
        )}
      </div>
      <div className={css.actions}>
        {c && (c.fits
          ? <span className={css.status}><b className="num">{size(c.used)}</b> · {t("builder.capacity.fits")}</span>
          : <span className={`${css.status} ${ui.warn}`}><b className="num">{t("builder.capacity.over", c.over >= 1e8 ? gb(c.over) : size(c.over))}</b>{c.needs && <> · {t("builder.capacity.needs", mediaLabel(c.needs, false))}</>}</span>)}
        {view.editor?.blocking && <span className={`${css.status} ${ui.warn}`}>{t("builder.capacity.themeError")}</span>}
        {c?.fits && !view.editor?.blocking && <DriveHint view={view} />}
        <BurnActions view={view} actions={actions} blocked={!!blocked} />
      </div>
    </footer>
  );
}

export function Ruler({ used, media, fits }: { used: number; media: "cd700" | "dvd5" | "dvd9"; fits: boolean }) {
  const pct = (b: number) => `${(rulerPos(b) * 100).toFixed(1)}%`;
  const targetLabel = mediaLabel(media);
  return (
    <div className={css.rulerBox}>
      <div role="img" aria-label={t("builder.capacity.aria", size(used), targetLabel)} className={css.track}>
        <span className={fits ? css.fill : css.fillWarn} style={{ width: pct(used) }} />
        {media !== "cd700" && <span className={css.markCd} style={{ left: pct(NOMINAL.cd700) }} />}
        {media !== "dvd9" && <span className={css.markTarget} style={{ left: pct(NOMINAL[media]) }} />}
      </div>
      <div className={css.labels}>
        <span className={media === "cd700" ? css.labelTarget : css.label} style={{ left: pct(NOMINAL.cd700) }}>{mediaLabel("cd700")}</span>
        {media === "dvd5" && <span className={css.labelTarget} style={{ left: pct(NOMINAL.dvd5) }}>{mediaLabel("dvd5")}</span>}
        <span className={media === "dvd9" ? css.labelEndTarget : css.labelEnd}>{mediaLabel("dvd9")}</span>
      </div>
    </div>
  );
}
