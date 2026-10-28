// Folder/file drop target. The page never sees paths; the host reads them from the dropped File objects.
import { useState, type DragEvent } from "react";
import { drop } from "./host";

export function useDrop(after?: () => void) {
  const [over, setOver] = useState(false);
  const has = (e: DragEvent) => [...e.dataTransfer.types].includes("Files");
  return {
    over,
    props: {
      onDragOver: (e: DragEvent) => { if (!has(e)) return; e.preventDefault(); e.dataTransfer.dropEffect = "copy"; setOver(true); },
      onDragLeave: (e: DragEvent) => { if (!e.currentTarget.contains(e.relatedTarget as Node)) setOver(false); },
      onDrop: async (e: DragEvent) => {
        e.preventDefault();
        setOver(false);
        if (e.dataTransfer.files.length === 0) return;
        const paths = await drop(e.dataTransfer.files);
        if (paths.length > 0) after?.();
      },
    },
  };
}
