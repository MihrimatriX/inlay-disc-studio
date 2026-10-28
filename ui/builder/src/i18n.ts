// User-facing text comes from the same i18n files as the disc (i18n/*.json, "builder.*" keys).
import tr from "../../../i18n/tr-TR.json";
import en from "../../../i18n/en-US.json";

const tables: Record<string, Record<string, string>> = { "tr-TR": tr, "en-US": en };
let table: Record<string, string> = tr;
let locale = "tr-TR";

export function setLocale(l: string) {
  locale = l.toLowerCase().startsWith("en") ? "en-US" : "tr-TR";
  table = tables[locale];
  document.documentElement.lang = locale.slice(0, 2);
}

export const currentLocale = () => locale;

/** t("builder.info.tracks", 12) → "12 parça". Missing keys fall back to Turkish, then to the key itself. */
export function t(key: string, ...args: (string | number)[]): string {
  const s = table[key] ?? (tr as Record<string, string>)[key] ?? key;
  return s.replace(/\{(\d+)\}/g, (_, i) => String(args[+i] ?? ""));
}
