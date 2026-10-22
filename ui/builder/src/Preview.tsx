import { useLayoutEffect, useRef, useState } from "react";
import { t } from "./i18n";
import css from "./Preview.module.css";

/** The live preview: the disc's own Menu.html render with the SDK standing in for the launcher (§7.3), drawn at
 *  1280×720 and scaled into a 16:9 frame so it looks like the TV/PC it is made for. */
export function Preview({ src, glow, max = 880 }: { src: string; glow?: string; max?: number }) {
  const box = useRef<HTMLDivElement>(null);
  const [w, setW] = useState(max);
  useLayoutEffect(() => {
    const el = box.current!;
    const fit = () => setW(Math.max(320, Math.min(max, el.clientWidth - 80, ((el.clientHeight - 80) * 16) / 9)));
    fit();
    const ro = new ResizeObserver(fit);
    ro.observe(el);
    return () => ro.disconnect();
  }, [max]);
  return (
    <div ref={box} className={css.stage}>
      {glow && <img src={glow} alt="" className={css.glow} />}
      <figure aria-label={t("builder.menu.preview")} className={css.frame} style={{ width: w, height: (w * 9) / 16 }}>
        <iframe title={t("builder.menu.preview")} src={src} className={css.iframe} style={{ transform: `scale(${w / 1280})` }} />
      </figure>
    </div>
  );
}
