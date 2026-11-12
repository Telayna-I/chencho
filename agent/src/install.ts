import { $ } from "bun";
import type { PanelConfig } from "../../shared/protocol.ts";
import { existsSync } from "node:fs";
import { copyFile, mkdir, readdir, rename, rm } from "node:fs/promises";
import { basename, dirname, join, resolve } from "node:path";
import { register, start, stop, unregister } from "./autostart.ts";
import { BIN_DIR, CLAUDE_SETTINGS, DATA_DIR, LOG_DIR, loadConfig, panelUrls } from "./config.ts";
import { t } from "./i18n.ts";
import { writeHooks } from "./settings.ts";

/** What a release ships next to `chencho.exe`, and setup copies into BIN_DIR. */
const SHIPPED = ["chencho.exe", "chencho-overlay.exe"];
const DOTNET_DOWNLOAD = "https://dotnet.microsoft.com/download/dotnet/9.0";

export async function setup({ pi }: { pi: boolean }) {
  const source = dirname(process.execPath);
  const missing = SHIPPED.filter((name) => !existsSync(join(source, name)));
  if (missing.length > 0) {
    const names = missing.join(", ");
    throw new Error(
      t(
        `${names} not found next to ${process.execPath}; run setup from a release build`,
        `no se ${missing.length > 1 ? "encontraron" : "encontró"} ${names} junto a ${process.execPath}; hay que ejecutar setup desde una versión publicada (release)`,
      ),
    );
  }
  if (!(await hasDesktopRuntime())) {
    const needsRuntime = t(
      `the strip needs the .NET 9 Desktop Runtime. Install it from ${DOTNET_DOWNLOAD}, then run setup again.`,
      `la franja necesita .NET 9 Desktop Runtime. Hay que instalarlo desde ${DOTNET_DOWNLOAD} y volver a ejecutar setup.`,
    );
    console.error(`chencho: ${needsRuntime}`);
    process.exit(1);
  }

  await stop();
  if (resolve(source).toLowerCase() !== resolve(BIN_DIR).toLowerCase()) await copyInto(source, BIN_DIR);
  const config = await loadConfig();
  await mkdir(LOG_DIR, { recursive: true });
  const hooksChanged = await writeHooks({ install: true });
  await register({ lan: pi });
  start({ lan: pi });

  console.log(
    t(
      `Chencho is installed and running.
  binaries : ${BIN_DIR}
  data     : ${DATA_DIR}
  logs     : ${LOG_DIR}
  hooks    : ${hooksChanged ? "added to" : "already in"} ${CLAUDE_SETTINGS}`,
      `Chencho está instalado y corriendo.
  binarios  : ${BIN_DIR}
  datos     : ${DATA_DIR}
  registros : ${LOG_DIR}
  hooks     : ${hooksChanged ? "agregados a" : "ya estaban en"} ${CLAUDE_SETTINGS}`,
    ),
  );
  if (pi) {
    const pairing: PanelConfig = { agentUrls: panelUrls(), token: config.token };
    const intro = t(
      "The agent also listens on your network. Pair the other device with this config.json:",
      "El agente también escucha en la red local. Para emparejar el otro dispositivo, usar este config.json:",
    );
    console.log(`\n${intro}\n${JSON.stringify(pairing, null, 2)}`);
  }
}

/** The overlay is framework-dependent: it needs Microsoft.WindowsDesktop.App 9. */
async function hasDesktopRuntime() {
  const listed = await $`dotnet --list-runtimes`.quiet().nothrow().text().catch(() => "");
  // A runtime installed after this terminal opened is missing from its PATH, so look where the installer puts it too.
  const shared = join(process.env.ProgramFiles ?? "C:\\Program Files", "dotnet", "shared", "Microsoft.WindowsDesktop.App");
  const installed = await readdir(shared).catch(() => []);
  return /^Microsoft\.WindowsDesktop\.App 9\./m.test(listed) || installed.some((version) => version.startsWith("9."));
}

/** Each binary lands under a temporary name, then replaces the old one in a single rename. */
async function copyInto(source: string, target: string) {
  await mkdir(target, { recursive: true });
  for (const name of SHIPPED) {
    const temporary = join(target, `.${name}.new`);
    await copyFile(join(source, name), temporary);
    await rename(temporary, join(target, name));
  }
}

export async function uninstall({ purge }: { purge: boolean }) {
  await stop();
  await unregister();
  const hooksRemoved = await writeHooks({ install: false });
  // Fails when this very chencho.exe runs from BIN_DIR: Windows won't delete a running binary.
  const binRemoved = await rm(BIN_DIR, { recursive: true, force: true }).then(() => true, () => false);

  const question = t(
    `Delete your pins, icons and logs in ${DATA_DIR}? [y/N]`,
    `¿Borrar las sesiones ancladas, los íconos y los registros de ${DATA_DIR}? [s/N]`,
  );
  const deleteData = purge || (process.stdin.isTTY && isYes(prompt(question)));
  if (deleteData) await rm(DATA_DIR, { recursive: true, force: true }).catch(() => {});
  if (!binRemoved) removeAfterExit(deleteData ? DATA_DIR : BIN_DIR);

  console.log(
    t(
      `Chencho is uninstalled.
  hooks : ${hooksRemoved ? "removed from" : "were not in"} ${CLAUDE_SETTINGS}
  data  : ${deleteData ? "deleted" : `kept in ${DATA_DIR}`}`,
      `Chencho está desinstalado.
  hooks : ${hooksRemoved ? "quitados de" : "no estaban en"} ${CLAUDE_SETTINGS}
  datos : ${deleteData ? "borrados" : `conservados en ${DATA_DIR}`}`,
    ),
  );
}

/** A yes to the [y/N] prompt; in Spanish "s", "si" and "sí" count too. */
function isYes(answer: string | null): boolean {
  return t("y", "y s si sí").split(" ").includes(answer?.trim().toLowerCase() ?? "");
}

/**
 * Windows won't delete a running binary, so a hidden cmd.exe removes `dir` once this chencho.exe has exited.
 * It runs from `dir`'s parent and names it relatively: a quoted path wouldn't survive cmd.exe's parsing.
 */
function removeAfterExit(dir: string) {
  const command = `ping -n 3 127.0.0.1 >nul & rmdir /s /q ${basename(dir)}`;
  Bun.spawn(["cmd.exe", "/d", "/c", command], {
    cwd: dirname(dir),
    detached: true,
    stdio: ["ignore", "ignore", "ignore"],
    windowsHide: true,
  }).unref();
}
