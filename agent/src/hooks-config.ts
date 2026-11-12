import { HOOK_PORT } from "./config.ts";

/** Hook events the agent needs to track session state. */
export const PANEL_EVENTS = [
  "SessionStart",
  "UserPromptSubmit",
  "PreToolUse",
  "PostToolUse",
  "PostToolUseFailure",
  "Notification",
  "Stop",
  "StopFailure",
  "SubagentStart",
  "SubagentStop",
  "PreCompact",
  "PostCompact",
  "SessionEnd",
] as const;

export const HOOK_URL = `http://127.0.0.1:${HOOK_PORT}/hook`;

/**
 * The `hooks` block for settings.json, forwarding every event to the agent.
 *
 * A command hook with curl rather than `"type": "http"`: when the agent is down,
 * http hooks add a "hook error" to the transcript of every session, while this one
 * always exits 0. Hooks stay synchronous so events arrive in order.
 *
 * Claude Code runs it in Git Bash, or in PowerShell where Git Bash isn't installed, and
 * there a failing hook can block the session. So it reads the same in both shells:
 * `curl.exe` (in Windows PowerShell `curl` is Invoke-WebRequest), double quotes, a quoted
 * `"@-"` (a bare `@` starts a splat in PowerShell), `-o NUL` rather than a redirect, and
 * `; exit 0` rather than `|| true`. It prints nothing, since Claude Code adds a hook's
 * output to the conversation for some events.
 */
export function buildPanelHooks() {
  const command = `curl.exe -s -m 1 -o NUL -H "Content-Type: application/json" --data-binary "@-" ${HOOK_URL}; exit 0`;
  return Object.fromEntries(
    PANEL_EVENTS.map((event) => [event, [{ hooks: [{ type: "command", command, timeout: 2 }] }]]),
  );
}

/** True for handlers installed by this project, including the earlier `http` variant. */
export function isPanelHook(handler: { type?: string; url?: string; command?: unknown }) {
  if (handler.type === "http") return handler.url === HOOK_URL;
  return handler.type === "command" && typeof handler.command === "string" && handler.command.includes(HOOK_URL);
}

type Handler = { type?: string; url?: string; command?: unknown; [k: string]: unknown };
type MatcherGroup = { matcher?: string; hooks: Handler[] };
export type Settings = { hooks?: Record<string, MatcherGroup[]>; [k: string]: unknown };

/** `settings` with our hooks removed, then added back when `install`; everything else stays as it was. */
export function mergeHooks(settings: Settings, { install }: { install: boolean }): Settings {
  const hooks: Record<string, MatcherGroup[]> = {};
  for (const [event, groups] of Object.entries(settings.hooks ?? {})) {
    const kept = groups
      .map((group) => ({ ...group, hooks: group.hooks.filter((h) => !isPanelHook(h)) }))
      .filter((group) => group.hooks.length > 0);
    if (kept.length > 0) hooks[event] = kept;
  }
  if (install) {
    for (const [event, groups] of Object.entries(buildPanelHooks())) hooks[event] = [...(hooks[event] ?? []), ...groups];
  }
  const { hooks: _, ...rest } = settings;
  return Object.keys(hooks).length > 0 ? { ...rest, hooks } : rest;
}

export function hasPanelHooks(settings: Settings) {
  return Object.values(settings.hooks ?? {}).some((groups) => groups.some((group) => group.hooks.some(isPanelHook)));
}
