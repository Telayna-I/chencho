/**
 * Where Chencho lives on disk. The data dir holds config.json with the pairing token (created on
 * first run), chencho.db the SQLite store, bin/ the binaries the Run value starts, and logs/.
 * The overlay reads the token from here too.
 */
import { randomBytes } from "node:crypto";
import { mkdir } from "node:fs/promises";
import { homedir, hostname, networkInterfaces } from "node:os";
import { join } from "node:path";
import { PANEL_PORT } from "../../shared/protocol.ts";

export const HOOK_PORT = 7777;

const HOME = homedir();
export const DATA_DIR = join(process.env.LOCALAPPDATA || join(HOME, "AppData", "Local"), "chencho");
export const BIN_DIR = join(DATA_DIR, "bin");
export const LOG_DIR = join(DATA_DIR, "logs");
export const CLAUDE_SETTINGS = join(HOME, ".claude", "settings.json");
export const CONFIG_FILE = join(DATA_DIR, "config.json");

/**
 * WebSocket URLs other devices on the LAN can use to reach this PC: its IPv4 addresses
 * (Ethernet/Wi-Fi first, ahead of virtual switches such as "vEthernet (WSL)"), then its
 * mDNS name. IPs come first because some routers drop mDNS between WiFi clients; the name
 * still works if the PC's IP changes later.
 */
export function panelUrls(): string[] {
  const physical = (name: string) => /^(Ethernet|Wi-Fi)/.test(name);
  const ips = Object.entries(networkInterfaces())
    .sort(([a], [b]) => Number(physical(b)) - Number(physical(a)))
    .flatMap(([, addresses]) => addresses ?? [])
    .filter((a) => a.family === "IPv4" && !a.internal)
    .map((a) => a.address);
  const host = hostname();
  return [...ips, ...(host ? [`${host}.local`] : [])].map((address) => `ws://${address}:${PANEL_PORT}/ws`);
}

export interface Config {
  dataDir: string;
  dbPath: string;
  token: string;
}

export async function loadConfig(): Promise<Config> {
  const dataDir = DATA_DIR;
  await mkdir(dataDir, { recursive: true });

  const path = CONFIG_FILE;
  const file = Bun.file(path);
  const saved: { token?: string } = (await file.exists()) ? await file.json() : {};

  if (!saved.token) {
    saved.token = randomBytes(24).toString("base64url");
    await Bun.write(path, JSON.stringify(saved, null, 2) + "\n");
  }

  return { dataDir, dbPath: join(dataDir, "chencho.db"), token: saved.token };
}
