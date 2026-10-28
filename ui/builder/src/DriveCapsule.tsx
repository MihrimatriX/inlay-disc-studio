import { t } from "./i18n";
import type { Drive, View } from "./types";
import ui from "./ui.module.css";

export const currentDrive = (view: View): Drive | undefined =>
  view.drives.find((d) => d.id === view.prefs.recorder) ?? view.drives.find((d) => d.ready) ?? view.drives[0];

/** Sürücü kapsülü: "E: ASUS DRW-24D5MT · Boş DVD-R, 4,7 GB", grey when there is no disc. */
export function DriveCapsule({ view }: { view: View }) {
  const d = currentDrive(view);
  return (
    <div className={ui.capsule} role="status">
      <span className={d?.ready ? ui.dotOk : ui.dot} aria-hidden="true" />
      {d ? <>{d.name} <span className={ui.sep}>·</span> {d.status}</> : t("builder.drive.none")}
    </div>
  );
}
