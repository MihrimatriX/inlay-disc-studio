import { useEffect, useRef, useState } from "react";
import { call } from "./host";
import { t } from "./i18n";
import { mediaLabel, remaining, size } from "./format";
import { Icon } from "./icons";
import { currentDrive } from "./DriveCapsule";
import type { BurnResult, Job, Stage, View } from "./types";
import ui from "./ui.module.css";
import css from "./JobDialog.module.css";

/** iso: "ISO olarak kaydet" · burn: "Diske yaz" · isoburn: "ISO kalıbı yaz" · erase: "Diski sil" · read: "Disk kopyala" step 1. */
export type JobKind = "iso" | "burn" | "isoburn" | "erase" | "read";

export interface JobState {
  kind: JobKind;
  path?: string;
  started: number;
  progress: Job | null;
  done: boolean;
  error: string | null;
  result?: BurnResult | null;
}

type Span = [number, number];
/** Share of the bar each stage fills, per kind of job. */
const WEIGHTS: Record<JobKind, Partial<Record<Stage, Span>>> = {
  iso: { prepare: [0, 0.7], write: [0.7, 1] },
  burn: { prepare: [0, 0.25], hash: [0.25, 0.35], write: [0.35, 0.85], finalize: [0.85, 0.88], verify: [0.88, 1] },
  isoburn: { write: [0, 0.8], finalize: [0.8, 0.83], verify: [0.83, 1] },
  erase: { erase: [0, 1] },
  read: { read: [0, 1] },
};

const burns = (k: JobKind) => k === "burn" || k === "isoburn";

/** Diske yazılıyor (design/screens/04): a calm modal, one number that matters, cancel always there. */
export function JobDialog({ view, job, close, again }: { view: View; job: JobState; close: () => void; again: () => void }) {
  const dialog = useRef<HTMLElement>(null);
  const [details, setDetails] = useState(false);
  const [check, setCheck] = useState(view.prefs.verify);
  const p = job.progress;
  const [a, b] = WEIGHTS[job.kind][p?.stage ?? "prepare"] ?? [0, 0];
  const overall = job.done ? 1 : a + (b - a) * (p?.value ?? 0);
  const elapsed = (Date.now() - job.started) / 1000;
  const left = p?.stage === "write" && p.remainingSec ? p.remainingSec : overall > 0.03 ? (elapsed / overall) * (1 - overall) : NaN;

  useEffect(() => { dialog.current?.querySelector<HTMLElement>(".focus-first, button")?.focus(); }, [job.done, job.error]);

  const title = job.error ? t(`builder.job.failed.${job.kind}`)
    : job.done ? t(`builder.job.done.${job.kind}`)
    : t(`builder.job.stage.${p?.stage === "write" && job.kind === "iso" ? "writeIso" : p?.stage ?? (job.kind === "erase" ? "erase" : job.kind === "read" ? "read" : "prepare")}`);
  const drive = currentDrive(view);
  const where = job.kind === "iso" ? job.path?.split(/[\\/]/).pop() : drive?.letter;
  const media = job.kind === "burn" && view.capacity ? mediaLabel(view.capacity.media, false) : "";
  const subject = job.kind === "burn" || job.kind === "iso" ? view.menu?.title ?? view.project.name : job.kind === "isoburn" ? job.path?.split(/[\\/]/).pop() : drive?.name;
  const errorCode = job.error?.startsWith("burn:") ? job.error.slice(5) : null;
  const r = job.result;

  return (
    <div className={css.backdrop}>
      <div className={css.light} aria-hidden="true" />
      <section ref={dialog} role="dialog" aria-modal="true" aria-labelledby="job-h" className={css.dialog}
        onKeyDown={(e) => e.key === "Escape" && (job.done || job.error) && close()}>
        <div className={css.head}>
          {job.done ? <span className={css.ok} aria-hidden="true"><Icon name="check" size={44} stroke={2.2} /></span>
            : <div className={job.error ? css.discStill : css.disc} aria-hidden="true">
                {job.kind === "burn" && view.menu?.glow && <img src={view.menu.glow} alt="" />}
                <span className={css.hub} />
              </div>}
          <div>
            <h1 id="job-h" className={css.h1}>{title}</h1>
            <span className={css.sub}>{[subject, media, where].filter(Boolean).join(" · ")}</span>
          </div>
        </div>

        {!job.done && !job.error && (
          <div className={css.progress}>
            <div className={css.numbers}>
              <span className={`${css.pct} num`}>{t("builder.job.percent", Math.floor(overall * 100))}</span>
              <span className={css.left}>{remaining(left)}</span>
            </div>
            <div role="progressbar" aria-label={t("builder.job.aria")} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.floor(overall * 100)} className={css.track}>
              <div className={css.fill} style={{ width: `${overall * 100}%` }} />
            </div>
            <span className={`${css.bytes} num`}>
              {p && p.totalBytes > 0 && (p.stage === "write" || p.stage === "verify" || p.stage === "read")
                ? `${size(p.doneBytes)} / ${size(p.totalBytes)}${p.speedX ? ` · ${Math.round(p.speedX)}x` : ""}`
                : t(p?.stage === "hash" ? "builder.job.hashing" : "builder.job.preparing")}
            </span>
          </div>
        )}

        {!job.done && !job.error && burns(job.kind) && (
          <label className={css.check}>
            <input type="checkbox" className={`${ui.check} focus-first`} checked={check}
              onChange={(e) => { setCheck(e.target.checked); void call("burn.check", { on: e.target.checked }); }} />
            {t("builder.job.checkAndEject")}
          </label>
        )}

        {job.done && job.kind === "iso" && <p className={css.text}>{t("builder.job.savedTo", job.path ?? "")}</p>}
        {job.done && burns(job.kind) && r && <Report r={r} />}
        {job.error && (
          <div className={css.text}>
            <p>{errorCode ? t(`builder.burn.error.${errorCode}`) : t(`builder.job.failedText.${job.kind}`)}</p>
            {details && <p className={css.details}>{job.error}</p>}
          </div>
        )}

        <div className={css.buttons}>
          {!job.done && !job.error && <button className={ui.secondary} onClick={() => call("job.cancel")}>{t("builder.job.cancel")}</button>}
          {job.error && <>
            {!errorCode && <button className={ui.tool} onClick={() => setDetails(!details)}>{t("builder.job.details")}</button>}
            <button className={ui.secondary} onClick={close}>{t("builder.job.close")}</button>
            <button className={ui.primary} onClick={() => { close(); again(); }}>{t("builder.job.retry")}</button>
          </>}
          {job.done && <>
            <button className={ui.secondary} onClick={close}>{t("builder.job.close")}</button>
            {job.kind === "burn" && <button className={ui.primary} onClick={() => { close(); again(); }}>{t("builder.job.another")}</button>}
          </>}
        </div>
      </section>
    </div>
  );
}

/** §8.5 report: verified or not, which files read back wrong, and how fast the disc read. */
function Report({ r }: { r: BurnResult }) {
  if (!r.checked) return <p className={css.text}>{t("builder.burn.notChecked")}</p>;
  const bad = [...r.mismatched, ...r.missing];
  const max = Math.max(1, ...r.readMBps);
  const w = 220, h = 40;
  const points = r.readMBps.map((v, i) => `${(i / Math.max(1, r.readMBps.length - 1)) * w},${h - (v / max) * h}`).join(" ");
  return (
    <div className={css.report}>
      <p className={bad.length ? `${css.text} ${ui.warn}` : css.text}>
        {bad.length ? t("builder.burn.bad", bad.length, r.files) : t("builder.burn.good", r.files)}
      </p>
      {bad.length > 0 && <ul className={css.badList}>{bad.slice(0, 6).map((f) => <li key={f}>{f}</li>)}</ul>}
      {r.readMBps.length > 1 && (
        <figure className={css.graph}>
          <svg width={w} height={h} viewBox={`0 0 ${w} ${h}`} role="img" aria-label={t("builder.burn.speedAria", Math.round(max))}>
            <polyline points={points} fill="none" stroke="var(--accent)" strokeWidth="2" strokeLinejoin="round" />
          </svg>
          <figcaption className="num">{t("builder.burn.speed", Math.round(max))}</figcaption>
        </figure>
      )}
    </div>
  );
}
