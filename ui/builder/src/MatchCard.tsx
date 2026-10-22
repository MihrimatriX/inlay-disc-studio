import { useState } from "react";
import { call } from "./host";
import { t } from "./i18n";
import type { Row } from "./types";
import ui from "./ui.module.css";
import css from "./MatchCard.module.css";

/** "Bu film hangisi?" under the row (§3.2): up to three candidates or the user's own details. No separate screen. */
export function MatchCard({ row }: { row: Row }) {
  const m = row.match!;
  const [manual, setManual] = useState(m.notFound);
  const [title, setTitle] = useState(row.title);
  const [year, setYear] = useState(/^\d{4}/.exec(row.info ?? "")?.[0] ?? "");
  const [poster, setPoster] = useState<string | null>(null);
  const choose = (choice: object) => call("content.match", { id: row.id, choice });

  return (
    <div className={css.card}>
      <span className={css.question}>{m.notFound ? t("builder.match.notFound") : t(`builder.match.${m.question}`)}</span>
      {!manual && (
        <div className={css.options}>
          {m.candidates.map((c) => (
            <button key={c.id} className={css.option} onClick={() => choose({ provider: c.provider, id: c.id, title: c.title, year: c.year })}>
              {c.thumbUrl ? <img src={c.thumbUrl} alt="" className={css.poster} /> : <span className={css.poster} aria-hidden="true" />}
              <span className={css.text}><b>{c.title}</b><span>{[c.year, c.detail].filter(Boolean).join(" · ")}</span></span>
            </button>
          ))}
          <button className={css.self} onClick={() => setManual(true)}>{t("builder.match.self")}</button>
        </div>
      )}
      {manual && (
        <form className={css.form} onSubmit={(e) => { e.preventDefault(); choose({ title: title.trim() || row.title, year: Number(year) || undefined, poster: poster ?? undefined }); }}>
          <label className={ui.field}><span className={ui.label}>{t("builder.match.name")}</span>
            <input className={ui.input} value={title} onChange={(e) => setTitle(e.target.value)} /></label>
          <label className={`${ui.field} ${css.year}`}><span className={ui.label}>{t("builder.match.year")}</span>
            <input className={`${ui.input} num`} inputMode="numeric" maxLength={4} value={year} onChange={(e) => setYear(e.target.value.replace(/\D/g, ""))} /></label>
          <button type="button" className={ui.toolLined} onClick={async () => {
            const [p] = await call<string[]>("dialog.files", { filter: t("builder.filter.images") });
            if (p) setPoster(p);
          }}>{poster ? t("builder.match.posterChosen") : t("builder.match.poster")}</button>
          <button type="submit" className={ui.toolFilled}>{t("builder.match.save")}</button>
        </form>
      )}
    </div>
  );
}
