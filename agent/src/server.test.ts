import { afterEach, beforeEach, expect, test } from "bun:test";
import type { ServerMessage } from "../../shared/protocol.ts";
import { SessionHub } from "./hub.ts";
import { startServers } from "./server.ts";
import { Store } from "./store.ts";
import type { TranscriptSession } from "./transcripts.ts";

const ID = "0603472f-b7c5-4dae-b7a1-142468264b30";
const TOKEN = "test-token";

const IDLE = "22222222-2222-4222-8222-222222222222";
const UNTITLED = "33333333-3333-4333-8333-333333333333";
const TRANSCRIPTS: TranscriptSession[] = [
  { id: IDLE, title: "Idle work", cwd: "C:\\Users\\dev\\idle", lastActivityAt: 5000 },
  { id: UNTITLED, title: null, cwd: "D:\\src\\untitled", lastActivityAt: 1 },
];

let servers: ReturnType<typeof startServers>;
let opened: string[];
let openError: string | null;

beforeEach(() => {
  opened = [];
  openError = null;
  servers = startServers({
    hub: new SessionHub(new Store()),
    token: TOKEN,
    hookPort: 0, // 0 = any free port
    panelPort: 0,
    panelHost: "127.0.0.1",
    open: async (id) => {
      opened.push(id);
      return openError ? { ok: false, error: openError } : { ok: true, command: ["fake-open", id], cwd: "C:\\" };
    },
    listTranscripts: async (limit) => TRANSCRIPTS.slice(0, limit),
    findTranscript: async (id) => TRANSCRIPTS.find((s) => s.id === id),
  });
});
afterEach(() => servers.stop());

const hookUrl = () => `http://127.0.0.1:${servers.hookServer.port}/hook`;
const panelHttp = () => `http://127.0.0.1:${servers.panelServer.port}`;

function postHook(event: Record<string, unknown>, url = hookUrl()) {
  return fetch(url, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(event) });
}

/** Opens a panel connection; next() resolves with the following server message. */
async function connect(token = TOKEN) {
  const ws = new WebSocket(`ws://127.0.0.1:${servers.panelServer.port}/ws?token=${token}`);
  const inbox: ServerMessage[] = [];
  const waiting: ((m: ServerMessage) => void)[] = [];
  ws.onmessage = (e) => {
    const msg = JSON.parse(String(e.data)) as ServerMessage;
    const resolve = waiting.shift();
    if (resolve) resolve(msg);
    else inbox.push(msg);
  };
  await new Promise((resolve, reject) => {
    ws.onopen = resolve;
    ws.onerror = reject;
  });
  const next = () =>
    inbox.length > 0 ? Promise.resolve(inbox.shift()!) : new Promise<ServerMessage>((resolve) => waiting.push(resolve));
  return { ws, next };
}

async function waitFor(condition: () => boolean, timeoutMs = 1000) {
  const start = Date.now();
  while (!condition()) {
    if (Date.now() - start > timeoutMs) throw new Error("timed out");
    await Bun.sleep(5);
  }
}

test("a wrong token is rejected", async () => {
  expect((await fetch(`${panelHttp()}/ws?token=nope`)).status).toBe(401);
  expect((await fetch(`${panelHttp()}/ws`)).status).toBe(401);
});

test("the network-facing port does not accept hook events", async () => {
  const res = await postHook({ hook_event_name: "Stop", session_id: ID, cwd: "/tmp" }, `${panelHttp()}/hook`);
  expect(res.status).toBe(404);
});

test("the panel gets a snapshot, then live updates from hooks", async () => {
  const { ws, next } = await connect();
  expect(await next()).toEqual({ type: "snapshot", sessions: [], focusedId: null });

  const res = await postHook({ hook_event_name: "UserPromptSubmit", session_id: ID, cwd: "/tmp/demo" });
  expect(res.status).toBe(204);
  expect(await next()).toMatchObject({ type: "session", session: { id: ID, name: "demo", state: "working", live: true } });
  ws.close();
});

test("pin, icon and open commands; anything else is refused", async () => {
  await postHook({ hook_event_name: "Stop", session_id: ID, cwd: "/tmp/demo" });
  const { ws, next } = await connect();
  await next(); // snapshot

  ws.send(JSON.stringify({ type: "pin", id: ID, pinned: true }));
  expect(await next()).toMatchObject({ type: "session", session: { id: ID, pinned: true, position: 0 } });

  ws.send(JSON.stringify({ type: "pin", id: ID, pinned: false }));
  expect(await next()).toMatchObject({ type: "session", session: { pinned: false } });

  ws.send(JSON.stringify({ type: "icon", id: ID, icon: { character: "crab", color: "#ff6a1a" } }));
  expect(await next()).toMatchObject({ type: "session", session: { pinned: true, icon: { character: "crab" } } });

  // Opening sends nothing back: the overlay works out focus itself.
  ws.send(JSON.stringify({ type: "open", id: ID }));
  await waitFor(() => opened.length === 1);
  expect(opened).toEqual([ID]);

  ws.send(JSON.stringify({ type: "open", id: "11111111-1111-1111-1111-111111111111" }));
  expect(await next()).toEqual({ type: "error", message: "unknown session" });

  ws.send("rm -rf /");
  expect(await next()).toEqual({ type: "error", message: "invalid message" });
  ws.close();
});

test("the library lists transcripts, and pinning one that isn't running adopts it", async () => {
  const { ws, next } = await connect();
  await next(); // snapshot

  ws.send(JSON.stringify({ type: "library" }));
  expect(await next()).toEqual({
    type: "library",
    sessions: [
      { id: IDLE, name: "Idle work", cwd: "C:\\Users\\dev\\idle", lastActivityAt: 5000, pinned: false },
      { id: UNTITLED, name: "untitled", cwd: "D:\\src\\untitled", lastActivityAt: 1, pinned: false },
    ],
  });

  ws.send(JSON.stringify({ type: "pin", id: IDLE, pinned: true }));
  expect(await next()).toMatchObject({ type: "session", session: { id: IDLE, name: "Idle work", live: false, state: "ended", pid: null } });
  expect(await next()).toMatchObject({ type: "session", session: { id: IDLE, pinned: true } });

  ws.send(JSON.stringify({ type: "library" }));
  expect(await next()).toMatchObject({ type: "library", sessions: [{ id: IDLE, pinned: true }, { id: UNTITLED, pinned: false }] });
  ws.close();
});

test("open errors reach the panel", async () => {
  openError = "already running in a terminal (pid 42)";
  await postHook({ hook_event_name: "Stop", session_id: ID, cwd: "C:\\src\\demo" });
  const { ws, next } = await connect();
  await next(); // snapshot

  ws.send(JSON.stringify({ type: "open", id: ID }));
  expect(await next()).toEqual({ type: "error", message: "already running in a terminal (pid 42)" });
  ws.close();
});
