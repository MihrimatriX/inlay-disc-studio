// Line icons from design/screens (1.6 px stroke, round caps, 19 px in the menu).
import type { ReactNode } from "react";

const paths: Record<string, ReactNode> = {
  movie: <><rect x="3" y="5" width="18" height="14" rx="3" /><path d="M10 9.5v5l4.5-2.5z" fill="currentColor" /></>,
  music: <><path d="M9 18V5l11-2v13" /><circle cx="6" cy="18" r="3" /><circle cx="17" cy="16" r="3" /></>,
  photo: <><rect x="3" y="5" width="18" height="14" rx="3" /><circle cx="9" cy="10" r="1.6" /><path d="m3 16 5-4 4 3 3-2 6 4" /></>,
  app: <><path d="M7 8h10a5 5 0 0 1 0 10h-1l-2-2h-4l-2 2H7A5 5 0 0 1 7 8z" /><path d="M8 11v4M6 13h4" /></>,
  data: <path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z" />,
  copy: <><circle cx="9" cy="12" r="6" /><circle cx="15" cy="12" r="6" /></>,
  iso: <><rect x="3" y="7" width="18" height="10" rx="2.5" /><path d="M7 12h6" /></>,
  erase: <><path d="M4 4v6h6M20 20v-6h-6" /><path d="M20 10A8 8 0 0 0 6 6L4 10M4 14a8 8 0 0 0 14 4l2-4" /></>,
  recent: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>,
  settings: <><circle cx="12" cy="12" r="3" /><path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M4.9 19.1 7 17M17 7l2.1-2.1" /></>,
  back: <path d="M15 5l-7 7 7 7" />,
  add: <path d="M12 5v14M5 12h14" />,
  remove: <path d="M5 12h14" />,
  screen: <><rect x="3" y="4" width="18" height="13" rx="2.5" /><path d="M8 21h8" /></>,
  disc: <><circle cx="12" cy="12" r="9" /><circle cx="12" cy="12" r="2.5" /></>,
  check: <path d="M5 12.5l4.5 4.5L19 7.5" />,
  undo: <path d="M9 7 4 12l5 5M4 12h11a5 5 0 0 1 0 10h-2" />,
  code: <path d="m9 7-5 5 5 5M15 7l5 5-5 5" />,
};

export function Icon({ name, size = 19, stroke = 1.6 }: { name: keyof typeof paths | string; size?: number; stroke?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={stroke} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {paths[name]}
    </svg>
  );
}
