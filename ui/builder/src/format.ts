// Numbers as discs are sold: decimal units ("4,7 GB", "700 MB"), tabular in the UI.
import { currentLocale, t } from "./i18n";
import type { DiscMedia } from "./types";

const nf = (d: number) => new Intl.NumberFormat(currentLocale(), { minimumFractionDigits: d, maximumFractionDigits: d });

/** Always in GB ("0,6 GB fazla"). */
export const gb = (bytes: number, digits = 1) => `${nf(digits).format(bytes / 1e9)} GB`;

export function size(bytes: number, digits = 2): string {
  if (bytes >= 1e9) return gb(bytes, digits);
  if (bytes >= 1e6) return `${nf(0).format(Math.round(bytes / 1e6))} MB`;
  return `${nf(0).format(Math.max(1, Math.round(bytes / 1e3)))} KB`;
}

/** Nominal capacity on the label (design: "CD 700 MB", "DVD 4,7 GB", "DVD DL 8,5 GB"). */
export const NOMINAL: Record<DiscMedia, number> = { cd700: 700e6, dvd5: 4.7e9, dvd9: 8.5e9 };

/** "DVD 4,7 GB" on the ruler; short = false gives just the medium ("DVD DL"). */
export const mediaLabel = (m: DiscMedia, withSize = true) => t(withSize ? `builder.media.${m}` : `builder.media.${m}.short`);

/** Ruler position 0..1 on the design's linear 0–8,5 GB scale. */
export const rulerPos = (bytes: number) => Math.min(1, Math.max(0, bytes / NOMINAL.dvd9));

export function remaining(sec: number): string {
  if (!isFinite(sec) || sec <= 0) return "";
  if (sec < 60) return t("builder.job.lessThanMinute");
  return t("builder.job.minutesLeft", Math.ceil(sec / 60));
}
