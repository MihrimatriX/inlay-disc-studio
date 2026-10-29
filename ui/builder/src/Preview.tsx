import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { t } from "./i18n";
import { Icon } from "./icons";
import css from "./Preview.module.css";

/** The live preview: the disc's own Menu.html render with the SDK standing in for the launcher (§7.3), drawn at
 *  1280×720 and scaled into a 16:9 frame so it looks like the TV/PC it is made for. Full screen fills the window
 *  edge to edge; Esc or the button leaves it. */
export function Preview({ src, glow, max = 880 }: { src: string; glow?: string; max?: number }) {
  const box = useRef<HTMLDivElement>(null);
  const [w, setW] = useState(max);
  const [full, setFull] = useState(false);
  useLayoutEffect(() => {
    const el = box.current!;
    const [cap, pad] = full ? [Infinity, 0] : [max, 80];
    const fit = () => setW(Math.max(320, Math.min(cap, el.clientWidth - pad, ((el.clientHeight - pad) * 16) / 9)));
    fit();
    const ro = new ResizeObserver(fit);
    ro.observe(el);
    return () => ro.disconnect();
  }, [max, full]);
  useEffect(() => {
    const change = () => setFull(document.fullscreenElement === box.current);
    document.addEventListener("fullscreenchange", change);
    return () => document.removeEventListener("fullscreenchange", change);
  }, []);
  const label = full ? t("builder.menu.exitFullscreen") : t("builder.menu.fullscreen");
  return (
    <div ref={box} className={css.stage}>
      {glow && <img src={glow} alt="" className={css.glow} />}
      <figure aria-label={t("builder.menu.preview")} className={css.frame} style={{ width: w, height: (w * 9) / 16 }}>
        <iframe title={t("builder.menu.preview")} src={src} className={css.iframe} style={{ transform: `scale(${w / 1280})` }} />
      </figure>
      <button className={css.full} title={label} aria-label={label}
        onClick={() => void (full ? document.exitFullscreen() : box.current!.requestFullscreen())}>
        <Icon name={full ? "shrink" : "expand"} size={16} stroke={2} />
      </button>
    </div>
  );
}
