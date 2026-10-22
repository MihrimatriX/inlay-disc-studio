import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";

// Served by the WPF host from https://app.inlay/ (a folder mapping), so every URL stays relative.
export default defineConfig({
  root: fileURLToPath(new URL(".", import.meta.url)),
  base: "./",
  plugins: [react()],
  css: { modules: { localsConvention: "camelCaseOnly" } },
  build: {
    outDir: "dist",
    emptyOutDir: true,
    target: "es2022",
    assetsInlineLimit: 0,
    // The CodeMirror chunk (~510 kB) only loads when "HTML olarak düzenle" opens.
    chunkSizeWarningLimit: 600,
  },
});
