import { defineConfig, configDefaults } from "vitest/config";

// Linked sessions keep their own checkouts under .claude/worktrees; only this checkout's tests run here.
export default defineConfig({
  test: { exclude: [...configDefaults.exclude, ".claude/**", "artifacts/**"] },
});
