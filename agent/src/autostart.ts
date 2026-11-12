/**
 * Starts Chencho at login through a value under HKCU\…\Run. Only the overlay is registered and
 * started: it launches `chencho.exe agent` itself and keeps it running.
 */
import { $ } from "bun";
import { join } from "node:path";
import { BIN_DIR } from "./config.ts";
import { t } from "./i18n.ts";

const RUN_KEY = String.raw`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`;
const RUN_VALUE = "Chencho";
export const OVERLAY_EXE = "chencho-overlay.exe";
export const AGENT_EXE = "chencho.exe";

/** The overlay and its arguments; it passes `--lan` on to the agent it runs. */
export function overlayProgram(binDir: string, { lan }: { lan: boolean }): string[] {
  return [join(binDir, OVERLAY_EXE), ...(lan ? ["--lan"] : [])];
}

/** A Run value's data: the path quoted, since it may hold spaces. */
export function commandLine([exe, ...args]: string[]): string {
  return [`"${exe}"`, ...args].join(" ");
}

/** Our value's data in `reg query` output, or null when it isn't there. */
export function parseRunValue(output: string): string | null {
  return output.match(/^\s*Chencho\s+REG_\w+\s+(.*?)\s*$/m)?.[1] ?? null;
}

/** Process ids for `image` in `tasklist /FO CSV /NH` output; anything else, such as its localized "no tasks", is skipped. */
export function parseTasklist(output: string, image: string): number[] {
  return output.split("\n").flatMap((line) => {
    const [, name, pid] = line.match(/^"([^"]*)","(\d+)"/) ?? [];
    return name?.toLowerCase() === image.toLowerCase() ? [Number(pid)] : [];
  });
}

async function running(image: string) {
  return parseTasklist(await $`tasklist /FI ${`IMAGENAME eq ${image}`} /FO CSV /NH`.quiet().nothrow().text(), image);
}

/** Running agents, leaving out this process: setup and status run as chencho.exe too. */
async function agents() {
  return (await running(AGENT_EXE)).filter((pid) => pid !== process.pid);
}

export async function register({ lan }: { lan: boolean }) {
  await $`reg add ${RUN_KEY} /v ${RUN_VALUE} /t REG_SZ /d ${commandLine(overlayProgram(BIN_DIR, { lan }))} /f`.quiet();
}

export async function unregister() {
  await $`reg delete ${RUN_KEY} /v ${RUN_VALUE} /f`.quiet().nothrow();
}

/**
 * Starts the overlay detached, so it outlives setup and the terminal it ran in. It runs in BIN_DIR, not in
 * the folder setup ran from: a process holds its working folder open, so that folder couldn't be deleted.
 */
export function start({ lan }: { lan: boolean }) {
  Bun.spawn(overlayProgram(BIN_DIR, { lan }), { cwd: BIN_DIR, detached: true, stdio: ["ignore", "ignore", "ignore"] }).unref();
}

/**
 * Stops the overlay first, since it would restart the agent, then every agent, and waits until
 * they are gone: Windows won't replace or delete the binaries of a running process.
 */
export async function stop() {
  await $`taskkill /F /IM ${OVERLAY_EXE}`.quiet().nothrow();
  await $`taskkill /F /FI ${`PID ne ${process.pid}`} /IM ${AGENT_EXE}`.quiet().nothrow();
  for (let i = 0; i < 50; i++) {
    if ((await running(OVERLAY_EXE)).length === 0 && (await agents()).length === 0) return;
    await Bun.sleep(100);
  }
  throw new Error(t("Chencho did not stop", "Chencho no se detuvo"));
}

/** For `chencho status`: the Run value's data, and what is running. */
export async function describe() {
  const value = parseRunValue(await $`reg query ${RUN_KEY} /v ${RUN_VALUE}`.quiet().nothrow().text());
  const state = (pids: number[]) =>
    pids.length > 0 ? t(`running (pid ${pids.join(", ")})`, `corriendo (pid ${pids.join(", ")})`) : t("not running", "no está corriendo");
  return { login: value ?? t("not registered", "no registrado"), overlay: state(await running(OVERLAY_EXE)), agent: state(await agents()) };
}
