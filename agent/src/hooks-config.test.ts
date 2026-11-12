import { describe, expect, test } from "bun:test";
import { buildPanelHooks, hasPanelHooks, HOOK_URL, mergeHooks, PANEL_EVENTS } from "./hooks-config.ts";

const theirs = { type: "command", command: "echo mine" };

test("every event posts to the agent with a command that reads the same in Git Bash and PowerShell, and always exits 0", () => {
  const hooks = buildPanelHooks();
  expect(Object.keys(hooks)).toEqual([...PANEL_EVENTS]);
  expect(hooks.Stop).toEqual([
    {
      hooks: [
        {
          type: "command",
          command: `curl.exe -s -m 1 -o NUL -H "Content-Type: application/json" --data-binary "@-" ${HOOK_URL}; exit 0`,
          timeout: 2,
        },
      ],
    },
  ]);
});

describe("mergeHooks", () => {
  test("install adds every event and keeps the user's hooks and other settings", () => {
    const merged = mergeHooks({ theme: "dark", hooks: { Stop: [{ hooks: [theirs] }] } }, { install: true });
    expect(merged.theme).toBe("dark");
    expect(merged.hooks?.Stop).toEqual([{ hooks: [theirs] }, ...buildPanelHooks().Stop!]);
    expect(Object.keys(merged.hooks!).sort()).toEqual(Object.keys(buildPanelHooks()).sort());
  });

  test("installing again changes nothing", () => {
    const once = mergeHooks({}, { install: true });
    expect(mergeHooks(once, { install: true })).toEqual(once);
  });

  test("replaces earlier variants of our hooks instead of duplicating them", () => {
    const bashCurl = { type: "command", command: `curl -s -m 1 -H 'Content-Type: application/json' --data-binary @- ${HOOK_URL} >/dev/null 2>&1 || true` };
    const old = { hooks: { Stop: [{ hooks: [{ type: "http", url: HOOK_URL }] }], SessionStart: [{ hooks: [bashCurl] }] } };
    expect(mergeHooks(old, { install: true })).toEqual(mergeHooks({}, { install: true }));
  });

  test("remove drops only ours, then empty groups, events and the hooks key", () => {
    const installed = mergeHooks({ hooks: { Stop: [{ hooks: [theirs] }] } }, { install: true });
    expect(mergeHooks(installed, { install: false })).toEqual({ hooks: { Stop: [{ hooks: [theirs] }] } });
    expect(mergeHooks(mergeHooks({ model: "opus" }, { install: true }), { install: false })).toEqual({ model: "opus" });
  });
});

test("hasPanelHooks", () => {
  expect(hasPanelHooks({})).toBe(false);
  expect(hasPanelHooks({ hooks: { Stop: [{ hooks: [theirs] }] } })).toBe(false);
  expect(hasPanelHooks(mergeHooks({}, { install: true }))).toBe(true);
});
