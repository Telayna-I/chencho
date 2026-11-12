import { describe, expect, test } from "bun:test";
import {
  applyHookEvent,
  describeTool,
  GRACE_MS,
  reconcile,
  type AgentEntry,
  type HookEvent,
  type SessionRecord,
} from "./state.ts";

const ID = "0603472f-b7c5-4dae-b7a1-142468264b30";
const CWD = "C:/dev/webshop";
const T0 = 1_000_000;

const ev = (hook_event_name: string, extra: Record<string, unknown> = {}): HookEvent => ({
  hook_event_name,
  session_id: ID,
  cwd: CWD,
  ...extra,
});

const bash = (command: string) => ({ tool_name: "Bash", tool_input: { command } });

/** Feed events in order, one second apart, and return the final record. */
function run(events: HookEvent[], start = T0): SessionRecord {
  let rec: SessionRecord | undefined;
  events.forEach((e, i) => {
    rec = applyHookEvent(rec, e, start + i * 1000);
  });
  return rec!;
}

describe("applyHookEvent", () => {
  test("a normal turn ends in done", () => {
    const rec = run([
      ev("UserPromptSubmit"),
      ev("PreToolUse", bash("npm test")),
      ev("PostToolUse", bash("npm test")),
      ev("Stop"),
    ]);
    expect(rec.state).toBe("done");
    expect(rec.activity).toBeNull();
    expect(rec.live).toBe(true);
  });

  test("while using a tool it says what it is doing", () => {
    const rec = run([ev("UserPromptSubmit"), ev("PreToolUse", bash("npm test"))]);
    expect(rec.state).toBe("working");
    expect(rec.activity).toBe("Bash: npm test");
  });

  test("a permission prompt needs you; approving resumes work", () => {
    const waiting = run([
      ev("PreToolUse", bash("rm -rf dist")),
      ev("Notification", { notification_type: "permission_prompt", message: "Claude needs your permission to use Bash" }),
    ]);
    expect(waiting.state).toBe("needs_permission");
    expect(waiting.activity).toBe("Claude needs your permission to use Bash");

    const resumed = applyHookEvent(waiting, ev("PostToolUse", bash("rm -rf dist")), T0 + 10_000);
    expect(resumed.state).toBe("working");
  });

  test("AskUserQuestion and ExitPlanMode wait for your answer", () => {
    expect(run([ev("PreToolUse", { tool_name: "AskUserQuestion", tool_input: {} })]).state).toBe("needs_input");
    expect(run([ev("PreToolUse", { tool_name: "ExitPlanMode", tool_input: {} })]).state).toBe("needs_input");
  });

  test("an API failure is an error with the reason", () => {
    const rec = run([ev("UserPromptSubmit"), ev("StopFailure", { error: "authentication_failed" })]);
    expect(rec.state).toBe("error");
    expect(rec.activity).toBe("authentication_failed");
  });

  test("SessionEnd marks it ended and not live", () => {
    const rec = run([ev("UserPromptSubmit"), ev("SessionEnd", { reason: "other" })]);
    expect(rec.state).toBe("ended");
    expect(rec.live).toBe(false);
  });

  test("stateSince only moves when the state changes", () => {
    const rec = run([ev("PreToolUse", bash("a")), ev("PostToolUse", bash("a")), ev("PreToolUse", bash("b"))]);
    expect(rec.state).toBe("working");
    expect(rec.stateSince).toBe(T0);
    expect(rec.lastEventAt).toBe(T0 + 2000);
  });

  test("cwd sticks to where the session started", () => {
    const rec = run([ev("UserPromptSubmit"), { ...ev("PreToolUse", bash("ls")), cwd: `${CWD}/agent` }]);
    expect(rec.cwd).toBe(CWD);
  });

  test("compaction shows up and then work continues", () => {
    expect(run([ev("PreCompact", { trigger: "auto" })]).state).toBe("compacting");
    expect(run([ev("PreCompact", { trigger: "auto" }), ev("PostCompact", { trigger: "auto" })]).state).toBe("working");
    expect(run([ev("PreCompact", { trigger: "manual" }), ev("PostCompact", { trigger: "manual" })]).state).toBe("done");
  });

  test("SessionStart after a compaction keeps the current state", () => {
    const rec = run([ev("UserPromptSubmit"), ev("SessionStart", { source: "compact" })]);
    expect(rec.state).toBe("working");
  });

  test("idle_prompt after a missed Stop settles to done", () => {
    const rec = run([ev("PreToolUse", bash("ls")), ev("Notification", { notification_type: "idle_prompt", message: "" })]);
    expect(rec.state).toBe("done");
  });

  test("a subagent finishing after the turn ended does not wake the session", () => {
    const rec = run([ev("SubagentStart", { agent_type: "Explore" }), ev("Stop"), ev("SubagentStop", { agent_type: "Explore" })]);
    expect(rec.state).toBe("done");
  });
});

describe("describeTool", () => {
  test("file tools show the file name only", () => {
    expect(describeTool("Edit", { file_path: "C:/dev/webshop/agent/src/state.ts" })).toBe("Edit: state.ts");
  });

  test("MCP tools show server and tool", () => {
    expect(describeTool("mcp__github__create_issue", {})).toBe("github: create_issue");
  });

  test("long details are truncated", () => {
    const label = describeTool("Bash", { command: "x".repeat(200) });
    expect(label.length).toBeLessThanOrEqual(48);
    expect(label.endsWith("…")).toBe(true);
  });
});

describe("reconcile (poll of `claude agents --json`)", () => {
  const agent = (status: AgentEntry["status"], extra: Partial<AgentEntry> = {}): AgentEntry => ({
    sessionId: ID,
    name: "Webshop",
    cwd: CWD,
    kind: "interactive",
    pid: 42,
    status,
    startedAt: 0,
    ...extra,
  });
  const records = (...recs: SessionRecord[]) => new Map(recs.map((r) => [r.id, r]));

  test("an unknown live session gets its state from the poll", () => {
    expect(reconcile(new Map(), [agent("busy")], T0)[0]!.state).toBe("working");
    expect(reconcile(new Map(), [agent("waiting")], T0)[0]!.state).toBe("needs_input");
    expect(reconcile(new Map(), [agent("idle")], T0)[0]!.state).toBe("idle");
  });

  test("the poll fills in the name and pid", () => {
    const rec = run([ev("UserPromptSubmit")]);
    const [next] = reconcile(records(rec), [agent("busy")], T0 + 500);
    expect(next!.name).toBe("Webshop");
    expect(next!.pid).toBe(42);
  });

  test("fresh hook state wins over a contradicting poll", () => {
    const rec = run([ev("UserPromptSubmit")]);
    const [next] = reconcile(records(rec), [agent("idle")], T0 + 1000);
    expect(next!.state).toBe("working");
  });

  test("stale working is corrected to done when the poll says idle", () => {
    const rec = run([ev("UserPromptSubmit")]);
    const [next] = reconcile(records(rec), [agent("idle")], T0 + GRACE_MS + 1);
    expect(next!.state).toBe("done");
  });

  test("a stale permission prompt clears once the poll says busy again", () => {
    const rec = run([ev("Notification", { notification_type: "permission_prompt", message: "…" })]);
    const [next] = reconcile(records(rec), [agent("busy")], T0 + GRACE_MS + 1);
    expect(next!.state).toBe("working");
  });

  test("a session missing from the poll ends after the grace period", () => {
    const rec = run([ev("Stop")]);
    expect(reconcile(records(rec), [], T0 + 1000)).toEqual([]);
    const [gone] = reconcile(records(rec), [], T0 + GRACE_MS + 1);
    expect(gone!.state).toBe("ended");
    expect(gone!.live).toBe(false);
  });

  test("a poll that raced the session's end doesn't bring it back", () => {
    const rec = run([ev("SessionEnd", { reason: "other" })]);
    expect(reconcile(records(rec), [agent("busy")], T0 + 1000)).toEqual([]);
  });

  test("an ended session that shows up again comes back to life", () => {
    const rec = run([ev("SessionEnd", { reason: "other" })]);
    const [back] = reconcile(records(rec), [agent("idle")], T0 + GRACE_MS + 1);
    expect(back!.live).toBe(true);
    expect(back!.state).toBe("idle");
  });

  test("nothing changes → nothing is reported", () => {
    const rec = reconcile(new Map(), [agent("idle")], T0)[0]!;
    expect(reconcile(records(rec), [agent("idle")], T0 + 3000)).toEqual([]);
  });
});
