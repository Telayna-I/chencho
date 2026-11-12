import { expect, test } from "bun:test";
import type { SessionRecord } from "./state.ts";
import { Store } from "./store.ts";

const ended: SessionRecord = {
  id: "11111111-1111-1111-1111-111111111111",
  name: "pinned and closed",
  cwd: "C:\\src",
  state: "ended",
  activity: null,
  live: false,
  pid: null,
  stateSince: 5,
  lastEventAt: 5,
};

test("sessions are saved, updated in place and deleted", () => {
  const store = new Store();
  store.saveSession(ended);
  store.saveSession({ ...ended, state: "working", live: true, pid: 4164 });
  expect(store.loadSessions()).toEqual([{ ...ended, state: "working", live: true, pid: 4164 }]);
  store.deleteSession(ended.id);
  expect(store.loadSessions()).toEqual([]);
});

test("pins append in order; an icon pins the session if needed", () => {
  const store = new Store();
  store.pin("a");
  store.pin("a");
  store.setIcon("b", { character: "cat", color: "#4f7cff" });
  expect([...store.loadPins().values()]).toEqual([
    { sessionId: "a", position: 0, icon: { character: "default", color: "#d97757" } },
    { sessionId: "b", position: 1, icon: { character: "cat", color: "#4f7cff" } },
  ]);
  store.unpin("a");
  expect([...store.loadPins().keys()]).toEqual(["b"]);
});
