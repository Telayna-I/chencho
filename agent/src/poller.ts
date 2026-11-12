import { $ } from "bun";
import type { SessionHub } from "./hub.ts";
import type { AgentEntry } from "./state.ts";

/** Feeds `claude agents --json` to the hub every `intervalMs`. Returns a stop function. */
export function startPoller(hub: SessionHub, intervalMs = 3000) {
  let stopped = false;
  let lastError = "";

  (async () => {
    while (!stopped) {
      try {
        const agents: AgentEntry[] = await $`claude agents --json`.quiet().json();
        hub.handleAgents(agents);
        lastError = "";
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        if (message !== lastError) console.error(`poll failed: ${message}`); // log each distinct failure once
        lastError = message;
      }
      await Bun.sleep(intervalMs);
    }
  })();

  return () => {
    stopped = true;
  };
}
