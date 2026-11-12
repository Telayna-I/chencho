/**
 * Every Claude Code session, running or not, from its transcript (undocumented):
 * ~/.claude/projects/<project>/<session id>.jsonl, one JSON object per line. Transcripts can run
 * to many megabytes, so only the head (for the cwd) and the tail (for the title) are read.
 */
import { open, readdir, stat, type FileHandle } from "node:fs/promises";
import { homedir } from "node:os";
import { join } from "node:path";
import { isSessionId } from "./protocol-guard.ts";

export const PROJECTS_DIR = join(homedir(), ".claude", "projects");

export interface TranscriptSession {
  id: string;
  title: string | null;
  /** Where the session started, which `claude --resume` must run from. */
  cwd: string;
  /** Epoch ms the transcript was last written. */
  lastActivityAt: number;
}

interface Transcript {
  id: string;
  path: string;
  mtime: number;
}

const CHUNK = 64 * 1024;
/** A first prompt with pasted images can push the first cwd megabytes in. */
const HEAD_LIMIT = 16 * 1024 * 1024;
/** Title lines, best first: the user's /rename, the session's name (as in `claude agents`), Claude's summary. */
const TITLES = { "custom-title": "customTitle", "agent-name": "agentName", "ai-title": "aiTitle" } as const;
const NEWLINE = Buffer.from("\n");

/** Most recently active first. Files that can't be read or have no cwd are skipped. */
export async function listTranscriptSessions(limit = 100, dir = PROJECTS_DIR): Promise<TranscriptSession[]> {
  const transcripts = (await allTranscripts(dir)).sort((a, b) => b.mtime - a.mtime);
  const sessions: TranscriptSession[] = [];
  for (let next = 0; sessions.length < limit && next < transcripts.length; ) {
    const batch = transcripts.slice(next, (next += limit - sessions.length));
    for (const session of await Promise.all(batch.map(readSession))) if (session) sessions.push(session);
  }
  return sessions;
}

export async function findTranscriptSession(id: string, dir = PROJECTS_DIR): Promise<TranscriptSession | undefined> {
  if (!isSessionId(id)) return undefined;
  for (const project of await projects(dir)) {
    const path = join(dir, project, `${id}.jsonl`);
    const info = await stat(path).catch(() => null);
    if (info?.isFile()) return (await readSession({ id, path, mtime: info.mtimeMs })) ?? undefined;
  }
}

async function projects(dir: string) {
  const entries = await readdir(dir, { withFileTypes: true }).catch(() => []);
  return entries.filter((e) => e.isDirectory()).map((e) => e.name);
}

/** Top-level transcripts only: subagents keep theirs in subfolders. */
async function allTranscripts(dir: string): Promise<Transcript[]> {
  const perProject = await Promise.all(
    (await projects(dir)).map(async (project) => {
      const names = (await readdir(join(dir, project)).catch(() => [])).filter((n) => n.endsWith(".jsonl") && isSessionId(n.slice(0, -6)));
      const found = await Promise.all(
        names.map(async (name) => {
          const path = join(dir, project, name);
          const info = await stat(path).catch(() => null);
          return info?.isFile() ? [{ id: name.slice(0, -6), path, mtime: info.mtimeMs }] : [];
        }),
      );
      return found.flat();
    }),
  );
  return perProject.flat();
}

async function readSession({ id, path, mtime }: Transcript): Promise<TranscriptSession | null> {
  let file: FileHandle | undefined;
  try {
    file = await open(path, "r");
    const { size } = await file.stat();
    const cwd = await firstCwd(file, size);
    if (!cwd) return null;
    return { id, title: await lastTitle(file, size), cwd, lastActivityAt: Math.trunc(mtime) };
  } catch {
    return null;
  } finally {
    await file?.close();
  }
}

async function readAt(file: FileHandle, position: number, length: number) {
  const buffer = Buffer.alloc(length);
  const { bytesRead } = await file.read(buffer, 0, length, position);
  return buffer.subarray(0, bytesRead);
}

function parse(line: Buffer | string): Record<string, unknown> | null {
  try {
    const value = JSON.parse(line.toString());
    return typeof value === "object" && value !== null ? value : null;
  } catch {
    return null;
  }
}

/** The cwd of the first line that has one, reading chunk by chunk from the start. */
async function firstCwd(file: FileHandle, size: number): Promise<string | null> {
  let pending = Buffer.alloc(0);
  for (let position = 0; position < Math.min(size, HEAD_LIMIT); position += CHUNK) {
    const chunk = await readAt(file, position, CHUNK);
    // A last line without its newline still counts.
    pending = Buffer.concat(position + CHUNK >= size ? [pending, chunk, NEWLINE] : [pending, chunk]);
    let start = 0;
    for (let end; (end = pending.indexOf(NEWLINE, start)) !== -1; start = end + 1) {
      const line = pending.subarray(start, end);
      if (!line.includes('"cwd"')) continue;
      const cwd = parse(line)?.cwd;
      if (typeof cwd === "string" && cwd) return cwd;
    }
    pending = pending.subarray(start);
  }
  return null;
}

/** The best title in the last CHUNK bytes; Claude Code appends title lines again as a session goes on. */
async function lastTitle(file: FileHandle, size: number): Promise<string | null> {
  const position = Math.max(0, size - CHUNK);
  const lines = (await readAt(file, position, size - position)).toString().split("\n");
  if (position > 0) lines.shift(); // starts mid-line
  const found: Partial<Record<keyof typeof TITLES, string>> = {};
  for (const line of lines.reverse()) {
    if (!line.includes('-title"') && !line.includes('"agent-name"')) continue;
    const entry = parse(line);
    const type = entry?.type as keyof typeof TITLES;
    const title = entry?.[TITLES[type]];
    if (typeof title === "string" && title.trim()) found[type] ??= title.trim();
    if (found["custom-title"]) break;
  }
  return found["custom-title"] ?? found["agent-name"] ?? found["ai-title"] ?? null;
}
