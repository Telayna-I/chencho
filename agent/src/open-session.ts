/**
 * Opens a session that isn't running by resuming it in its folder: in a new Windows Terminal
 * tab, or a new console window without Windows Terminal. A running session is refused, since
 * resuming it would start a second process on the same conversation; the overlay brings its
 * terminal to the front instead.
 */
import { existsSync, lstatSync } from "node:fs";
import { readdir } from "node:fs/promises";
import { homedir } from "node:os";
import { join } from "node:path";
import { CMD, CMD_ENV } from "./config.ts";
import { isSessionId } from "./protocol-guard.ts";
import { findTranscriptSession, PROJECTS_DIR } from "./transcripts.ts";

const LIVE_DIR = join(homedir(), ".claude", "sessions");

/** `command` runs in `cwd`, so the session's folder never passes through cmd.exe's parsing of `&`, `^` or `%`. */
export type OpenResult = { ok: true; command: string[]; cwd: string } | { ok: false; error: string };

interface LiveSession {
  pid: number;
  sessionId: string;
}

function isAlive(pid: number) {
  try {
    process.kill(pid, 0); // signal 0 only checks that the process exists
    return true;
  } catch {
    return false;
  }
}

/** A running Claude Code process for this session, from the CLI's internal ~/.claude/sessions/<pid>.json. */
async function findLive(id: string, dir: string): Promise<LiveSession | undefined> {
  for (const name of await readdir(dir).catch(() => [])) {
    if (!name.endsWith(".json")) continue;
    const info: LiveSession | null = await Bun.file(join(dir, name)).json().catch(() => null);
    if (info?.sessionId === id && isAlive(info.pid)) return info;
  }
}

/**
 * Whether `exe` is in a PATH folder. Unlike Bun.which, this sees the app execution aliases that
 * Store installs of wt.exe and pwsh.exe leave in WindowsApps, which only lstat can read.
 */
export function onPath(exe: string) {
  return (process.env.PATH ?? "").split(";").some((dir) => dir && lstatSync(join(dir, exe), { throwIfNoEntry: false }));
}

/** Picks the command that would open the session, without running it. */
export async function planOpen(
  sessionId: string,
  { liveDir = LIVE_DIR, projectsDir = PROJECTS_DIR, has = onPath } = {},
): Promise<OpenResult> {
  if (!isSessionId(sessionId)) return { ok: false, error: "invalid session id" };

  const live = await findLive(sessionId, liveDir);
  if (live) return { ok: false, error: `already running in a terminal (pid ${live.pid})` };

  const transcript = await findTranscriptSession(sessionId, projectsDir);
  if (!transcript) return { ok: false, error: "its transcript is gone" };
  const { cwd } = transcript;
  if (!existsSync(cwd)) return { ok: false, error: `its folder no longer exists: ${cwd}` };

  const shell = has("pwsh.exe") ? "pwsh.exe" : "powershell.exe";
  // Bun can't spawn app execution aliases such as wt.exe, but cmd.exe can.
  const command = has("wt.exe")
    ? [CMD, "/d", "/c", "wt.exe", "-w", "0", "new-tab", "-d", ".", shell, "-NoExit", "-Command", "claude", "--resume", sessionId]
    : [CMD, "/d", "/c", "start", "", shell, "-NoExit", "-Command", `claude --resume ${sessionId}`];
  return { ok: true, command, cwd };
}

export async function openSession(sessionId: string): Promise<OpenResult> {
  const plan = await planOpen(sessionId);
  if (!plan.ok) return plan;

  const proc = Bun.spawn(plan.command, { cwd: plan.cwd, env: CMD_ENV, stdout: "ignore", stderr: "pipe", windowsHide: true });
  const code = await proc.exited;
  if (code !== 0) {
    const stderr = (await new Response(proc.stderr).text()).trim().slice(0, 200);
    return { ok: false, error: `${plan.command[0]} exited with ${code}${stderr ? `: ${stderr}` : ""}` };
  }
  return plan;
}
