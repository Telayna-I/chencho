/**
 * Two servers: hooks on loopback only, and the panel WebSocket behind the pairing token,
 * on loopback unless another device needs it on the network. Keeping them apart means the
 * network-facing port can never inject hook events.
 */
import { timingSafeEqual } from "node:crypto";
import { basename } from "node:path";
import type { LibraryEntry, ServerMessage } from "../../shared/protocol.ts";
import type { SessionHub } from "./hub.ts";
import { openSession, type OpenResult } from "./open-session.ts";
import { isSessionId, parseClientMessage } from "./protocol-guard.ts";
import type { HookEvent } from "./state.ts";
import { findTranscriptSession, listTranscriptSessions, type TranscriptSession } from "./transcripts.ts";

export interface ServerOptions {
  hub: SessionHub;
  token: string;
  hookPort: number;
  panelPort: number;
  panelHost?: string;
  /** Replaceable so tests don't open real sessions or read real transcripts. */
  open?: (id: string) => Promise<OpenResult>;
  listTranscripts?: (limit: number) => Promise<TranscriptSession[]>;
  findTranscript?: (id: string) => Promise<TranscriptSession | undefined>;
}

const TOPIC = "panel";
const LIBRARY_SIZE = 100;

export function startServers({
  hub,
  token,
  hookPort,
  panelPort,
  panelHost = "127.0.0.1",
  open = openSession,
  listTranscripts = (limit) => listTranscriptSessions(limit),
  findTranscript = (id) => findTranscriptSession(id),
}: ServerOptions) {
  async function library(): Promise<LibraryEntry[]> {
    return (await listTranscripts(LIBRARY_SIZE)).map((s) => ({
      id: s.id,
      name: s.title ?? hub.get(s.id)?.name ?? basename(s.cwd),
      cwd: s.cwd,
      lastActivityAt: s.lastActivityAt,
      pinned: hub.get(s.id)?.pinned ?? false,
    }));
  }

  /** Lets the panel pin a session whose process isn't running, so the agent never saw it. */
  async function adoptFromTranscript(id: string) {
    const session = await findTranscript(id);
    if (session) hub.adopt(session.id, session.title, session.cwd, session.lastActivityAt);
  }

  const hookServer = Bun.serve({
    hostname: "127.0.0.1",
    port: hookPort,
    async fetch(req) {
      const { pathname } = new URL(req.url);
      if (req.method !== "POST" || pathname !== "/hook") return new Response("not found", { status: 404 });
      // A web page can post to 127.0.0.1 too. Browsers send an Origin header with such a request, and
      // can't send application/json across origins without a preflight, which this server never answers.
      if (req.headers.has("origin") || !req.headers.get("content-type")?.startsWith("application/json")) {
        return new Response("forbidden", { status: 403 });
      }
      try {
        const event = (await req.json()) as HookEvent;
        if (isSessionId(event?.session_id) && typeof event.hook_event_name === "string") hub.handleHook(event);
      } catch {
        return new Response("bad json", { status: 400 });
      }
      return new Response(null, { status: 204 }); // an empty 2xx tells Claude Code "no decision"
    },
  });

  const panelServer = Bun.serve({
    hostname: panelHost,
    port: panelPort,
    fetch(req, server) {
      const url = new URL(req.url);
      if (url.pathname === "/health") return Response.json({ ok: true, sessions: hub.snapshot().length });
      if (url.pathname !== "/ws") return new Response("not found", { status: 404 });
      if (!sameSecret(url.searchParams.get("token"), token)) return new Response("unauthorized", { status: 401 });
      if (server.upgrade(req)) return;
      return new Response("expected a WebSocket", { status: 400 });
    },
    websocket: {
      maxPayloadLength: 4 * 1024,
      open(ws) {
        ws.subscribe(TOPIC);
        send(ws, { type: "snapshot", sessions: hub.snapshot(), focusedId: hub.focusedId });
      },
      async message(ws, raw) {
        const msg = parseClientMessage(String(raw));
        if (!msg) return send(ws, { type: "error", message: "invalid message" });
        if (msg.type === "library") return send(ws, { type: "library", sessions: await library() });
        if (!hub.has(msg.id) && msg.type !== "open") await adoptFromTranscript(msg.id);
        if (!hub.has(msg.id)) return send(ws, { type: "error", message: "unknown session" });

        switch (msg.type) {
          case "open": {
            // No focus change here: the overlay works out which terminal is in front itself.
            const result = await open(msg.id);
            if (!result.ok) send(ws, { type: "error", message: result.error });
            break;
          }
          case "pin":
            hub.setPinned(msg.id, msg.pinned);
            break;
          case "icon":
            hub.setIcon(msg.id, msg.icon);
            break;
        }
      },
      close(ws) {
        ws.unsubscribe(TOPIC);
      },
    },
  });

  const unsubscribe = hub.subscribe((change) => panelServer.publish(TOPIC, JSON.stringify(change satisfies ServerMessage)));

  return {
    hookServer,
    panelServer,
    stop() {
      unsubscribe();
      hookServer.stop(true);
      panelServer.stop(true);
    },
  };
}

function send(ws: { send(data: string): unknown }, message: ServerMessage) {
  ws.send(JSON.stringify(message));
}

/** Constant-time, so response timing doesn't leak the token. */
function sameSecret(given: string | null, expected: string) {
  if (!given) return false;
  const a = Buffer.from(given);
  const b = Buffer.from(expected);
  return a.length === b.length && timingSafeEqual(a, b);
}
