import { afterAll, expect, test } from "bun:test";
import { mkdirSync, mkdtempSync, rmSync, utimesSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { findTranscriptSession, listTranscriptSessions } from "./transcripts.ts";

const A = "0603472f-b7c5-4dae-b7a1-142468264b30";
const B = "34446bd8-9f71-4831-9f0b-d6094803dee2";
const C = "5c0ffee0-1234-4567-89ab-cdef01234567";
const BIG = "8e27e4e7-aab5-4453-9dd8-aca5b57474f2";
const BROKEN = "11111111-1111-4111-8111-111111111111";

const root = mkdtempSync(join(tmpdir(), "chencho-transcripts-"));
afterAll(() => rmSync(root, { recursive: true, force: true }));

const meta = (type: string, extra: object = {}) => ({ type, ...extra });
const user = (cwd: string, text = "hi") => ({ type: "user", message: { role: "user", content: text }, cwd, sessionId: "x" });

function write(project: string, name: string, lines: object[], mtimeSeconds: number) {
  mkdirSync(join(root, project), { recursive: true });
  const path = join(root, project, name);
  writeFileSync(path, lines.map((l) => JSON.stringify(l)).join("\n") + "\n");
  utimesSync(path, mtimeSeconds, mtimeSeconds);
  return path;
}

write("C--src-a", `${A}.jsonl`, [
  meta("mode", { mode: "normal" }),
  user("C:\\src\\a"),
  user("C:\\src\\a\\moved"), // the session cd'd later; resume still needs where it started
  meta("ai-title", { aiTitle: "Summary" }),
  meta("agent-name", { agentName: "old-name" }),
  meta("agent-name", { agentName: "fix-the-build" }),
  meta("ai-title", { aiTitle: "Later summary" }),
], 2000);

write("D--work-b", `${B}.jsonl`, [
  meta("custom-title", { customTitle: "Renamed by me" }),
  user("D:\\work\\b"),
  meta("agent-name", { agentName: "ignored" }),
  meta("custom-title", { customTitle: "Renamed again" }),
], 1000);

// The first prompt holds a pasted image, so the first cwd sits past the first 64 KB, and only
// the tail, past megabytes of conversation, has a title.
const filler = Array.from({ length: 3000 }, (_, i) => ({ type: "assistant", message: { content: `${i} ${"x".repeat(1000)}` }, cwd: "D:\\big" }));
const tail = Array.from({ length: 20 }, () => ({ type: "assistant", message: { content: "y".repeat(500) }, cwd: "D:\\big" }));
write("D--big", `${BIG}.jsonl`, [
  meta("mode", { mode: "normal" }),
  user("D:\\big", "z".repeat(300 * 1024)),
  ...filler,
  meta("ai-title", { aiTitle: "Found in the tail" }),
  ...tail,
], 3000);

const unfinished = join(root, "D--work-b", `${C}.jsonl`); // its only line has no newline yet
writeFileSync(unfinished, JSON.stringify(user("D:\\work\\c")));
utimesSync(unfinished, 500, 500);

write("D--broken", `${BROKEN}.jsonl`, [meta("mode", { mode: "normal" })], 4000); // no cwd: can't be resumed
writeFileSync(join(root, "D--broken", "not-a-session.jsonl"), JSON.stringify(user("D:\\nope")));
write(join("C--src-a", A, "subagents"), "agent-a1b2c3.jsonl", [user("C:\\src\\a")], 5000);

test("lists top-level session transcripts, most recent first, skipping those without a cwd", async () => {
  const sessions = await listTranscriptSessions(100, root);
  expect(sessions).toEqual([
    { id: BIG, title: "Found in the tail", cwd: "D:\\big", lastActivityAt: 3_000_000 },
    { id: A, title: "fix-the-build", cwd: "C:\\src\\a", lastActivityAt: 2_000_000 },
    { id: B, title: "Renamed again", cwd: "D:\\work\\b", lastActivityAt: 1_000_000 },
    { id: C, title: null, cwd: "D:\\work\\c", lastActivityAt: 500_000 },
  ]);
});

test("stops at the limit", async () => {
  expect((await listTranscriptSessions(2, root)).map((s) => s.id)).toEqual([BIG, A]);
});

test("finds one session by id", async () => {
  expect(await findTranscriptSession(B, root)).toEqual({ id: B, title: "Renamed again", cwd: "D:\\work\\b", lastActivityAt: 1_000_000 });
  expect(await findTranscriptSession("22222222-2222-4222-8222-222222222222", root)).toBeUndefined();
  expect(await findTranscriptSession(BROKEN, root)).toBeUndefined();
  expect(await findTranscriptSession("..\\..\\secrets", root)).toBeUndefined();
});

test("a missing projects folder lists nothing", async () => {
  expect(await listTranscriptSessions(100, join(root, "missing"))).toEqual([]);
});
