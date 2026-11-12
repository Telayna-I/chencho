import { afterAll, expect, test } from "bun:test";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { onPath, planOpen } from "./open-session.ts";

const ID = "0603472f-b7c5-4dae-b7a1-142468264b30";
const GONE = "34446bd8-9f71-4831-9f0b-d6094803dee2";
const NO_TRANSCRIPT = "22222222-2222-4222-8222-222222222222";

const root = mkdtempSync(join(tmpdir(), "chencho-open-"));
afterAll(() => rmSync(root, { recursive: true, force: true }));

const cwd = join(root, "R&D project"); // cmd.exe would split this name at the &; it never sees it
const liveDir = join(root, "sessions");
const projectsDir = join(root, "projects");
mkdirSync(cwd);
mkdirSync(liveDir);
mkdirSync(join(projectsDir, "project"), { recursive: true });
const transcript = (id: string, folder: string) =>
  writeFileSync(join(projectsDir, "project", `${id}.jsonl`), JSON.stringify({ type: "user", cwd: folder, sessionId: id }) + "\n");
transcript(ID, cwd);
transcript(GONE, join(root, "deleted"));

const having = (...exes: string[]) => ({ liveDir, projectsDir, has: (exe: string) => exes.includes(exe) });

test("resumes in a new Windows Terminal tab, in the session's folder", async () => {
  expect(await planOpen(ID, having("wt.exe", "pwsh.exe"))).toEqual({
    ok: true,
    command: ["cmd.exe", "/d", "/c", "wt.exe", "-w", "0", "new-tab", "-d", ".", "pwsh.exe", "-NoExit", "-Command", "claude", "--resume", ID],
    cwd,
  });
});

test("without Windows Terminal or PowerShell 7, a console window with Windows PowerShell", async () => {
  expect(await planOpen(ID, having())).toEqual({
    ok: true,
    command: ["cmd.exe", "/d", "/c", "start", "", "powershell.exe", "-NoExit", "-Command", `claude --resume ${ID}`],
    cwd,
  });
});

test("refuses a session that is running, or that can't be resumed", async () => {
  expect(await planOpen("not-a-uuid", having())).toEqual({ ok: false, error: "invalid session id" });
  expect(await planOpen(NO_TRANSCRIPT, having())).toEqual({ ok: false, error: "its transcript is gone" });
  expect(await planOpen(GONE, having())).toEqual({ ok: false, error: `its folder no longer exists: ${join(root, "deleted")}` });

  writeFileSync(join(liveDir, "1.json"), JSON.stringify({ pid: 2 ** 31 - 2, sessionId: ID })); // a pid that isn't running
  expect((await planOpen(ID, having())).ok).toBe(true);

  writeFileSync(join(liveDir, "2.json"), JSON.stringify({ pid: process.pid, sessionId: ID }));
  expect(await planOpen(ID, having())).toEqual({ ok: false, error: `already running in a terminal (pid ${process.pid})` });
});

test("onPath finds executables in PATH folders", () => {
  const bin = join(root, "bin");
  mkdirSync(bin);
  writeFileSync(join(bin, "fake-tool.exe"), "");
  const saved = process.env.PATH;
  process.env.PATH = `${join(root, "missing")};${bin}`;
  try {
    expect(onPath("fake-tool.exe")).toBe(true);
    expect(onPath("other-tool.exe")).toBe(false);
  } finally {
    process.env.PATH = saved;
  }
});
