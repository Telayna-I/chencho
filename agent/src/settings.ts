import { copyFile } from "node:fs/promises";
import { CLAUDE_SETTINGS } from "./config.ts";
import { hasPanelHooks, mergeHooks, type Settings } from "./hooks-config.ts";

async function readSettings(): Promise<Settings> {
  const file = Bun.file(CLAUDE_SETTINGS);
  return (await file.exists()) ? await file.json() : {};
}

/** Adds or removes our hooks in ~/.claude/settings.json, backing the file up first. False when nothing changed. */
export async function writeHooks({ install }: { install: boolean }): Promise<boolean> {
  const exists = await Bun.file(CLAUDE_SETTINGS).exists();
  const before = await readSettings();
  const after = mergeHooks(before, { install });
  if (JSON.stringify(after) === JSON.stringify(before)) return false;
  if (exists) await copyFile(CLAUDE_SETTINGS, `${CLAUDE_SETTINGS}.bak-chencho-${Date.now()}`);
  await Bun.write(CLAUDE_SETTINGS, JSON.stringify(after, null, 2) + "\n");
  return true;
}

export async function hooksInstalled() {
  return hasPanelHooks(await readSettings());
}
