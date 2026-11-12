import { PANEL_PORT } from "../../shared/protocol.ts";
import { describe } from "./autostart.ts";
import { LOG_DIR } from "./config.ts";
import { t } from "./i18n.ts";
import { hooksInstalled } from "./settings.ts";

export async function status(version: string) {
  const { login, overlay, agent } = await describe();
  const health = await fetch(`http://127.0.0.1:${PANEL_PORT}/health`, { signal: AbortSignal.timeout(2000) })
    .then((r) => r.json() as Promise<{ sessions: number }>)
    .catch(() => null);
  const sessions = (n: number) => t(`${n} ${n === 1 ? "session" : "sessions"}`, `${n} ${n === 1 ? "sesión" : "sesiones"}`);
  const hooks = await hooksInstalled();
  console.log(
    t(
      `chencho ${version}
  login   : ${login}
  overlay : ${overlay}
  agent   : ${agent}
  hooks   : ${hooks ? "installed" : "not installed"}
  health  : ${health ? `answering, ${sessions(health.sessions)}` : `no answer on :${PANEL_PORT}`}
  logs    : ${LOG_DIR}`,
      `chencho ${version}
  inicio    : ${login}
  franja    : ${overlay}
  agente    : ${agent}
  hooks     : ${hooks ? "instalados" : "no instalados"}
  estado    : ${health ? `responde, ${sessions(health.sessions)}` : `sin respuesta en :${PANEL_PORT}`}
  registros : ${LOG_DIR}`,
    ),
  );
}
