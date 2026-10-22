// Actions several screens and shortcuts share (Ctrl+O, Ctrl+S, …).
import { call } from "./host";
import { t } from "./i18n";
import type { View } from "./types";

export async function pickFolder() {
  const paths = await call<string[]>("dialog.folder");
  if (paths.length > 0) await call("content.add", { paths });
  return paths.length > 0;
}

export async function pickFiles() {
  const paths = await call<string[]>("dialog.files", { filter: t("builder.filter.all") });
  if (paths.length > 0) await call("content.add", { paths });
  return paths.length > 0;
}

export async function save(view: View) {
  if (view.project.named) return call("project.save");
  const path = await call<string>("dialog.save", { filter: t("builder.filter.project"), name: view.project.name || t("builder.project.untitled") });
  if (path) await call("project.save", { path });
}

export async function open() {
  const [path] = await call<string[]>("dialog.files", { filter: t("builder.filter.project") });
  if (path) await call("project.open", { path });
  return !!path;
}

export async function saveIsoPath(view: View) {
  return call<string>("dialog.save", { filter: t("builder.filter.iso"), name: view.menu?.label ?? view.project.name });
}
