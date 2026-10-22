import { t } from "./i18n";
import { Icon } from "./icons";
import { currentDrive } from "./DriveCapsule";
import type { View } from "./types";
import ui from "./ui.module.css";

export interface Actions { burn: () => void; iso: () => void; tray: () => void }

/** The screen's one primary action (§3.5): "Diske yaz" with a writable disc that fits, "Disk tak" (opens the tray)
 *  when there is none or it is too small, "ISO olarak kaydet" when there is no recorder at all. */
export function BurnActions({ view, actions, blocked }: { view: View; actions: Actions; blocked: boolean }) {
  const drive = currentDrive(view);
  const used = view.capacity?.used ?? 0;
  const fits = !!drive?.ready && drive.free >= used;
  if (!drive) {
    return <button className={ui.primary} onClick={actions.iso} disabled={blocked}><Icon name="disc" size={16} stroke={2} />{t("builder.iso.save")}</button>;
  }
  return (
    <>
      <button className={ui.tool} onClick={actions.iso} disabled={blocked}>{t("builder.iso.save")}</button>
      {fits
        ? <button className={ui.primary} onClick={actions.burn} disabled={blocked}><Icon name="disc" size={16} stroke={2} />{t("builder.burn.write")}</button>
        : <button className={ui.primary} onClick={actions.tray}><Icon name="disc" size={16} stroke={2} />{t("builder.burn.insert")}</button>}
    </>
  );
}

/** Why "Diske yaz" waits, in a few words next to the ruler. */
export function DriveHint({ view }: { view: View }) {
  const drive = currentDrive(view);
  if (!drive || !view.capacity) return null;
  if (drive.ready && drive.free < view.capacity.used) return <span className={ui.warn}>{t("builder.burn.discTooSmall")}</span>;
  if (!drive.ready) return <span className={ui.note}>{drive.status}</span>;
  return null;
}
