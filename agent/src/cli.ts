import { t } from "./i18n.ts";

export type Command =
  | { name: "agent"; lan: boolean }
  | { name: "setup"; pi: boolean }
  | { name: "status" }
  | { name: "uninstall"; purge: boolean }
  | { name: "version" }
  | { name: "help"; error?: string };

export const USAGE = t(
  `Usage: chencho <command>

  setup [--pi]         install the agent, the strip and the Claude Code hooks, and start them
                       --pi also lets another device on the local network (e.g. a Raspberry Pi display) connect
  status               show what is installed and running
  uninstall [--purge]  remove everything; --purge also deletes pins and icons without asking
  agent [--lan]        run the agent in the foreground (what the strip runs)
  version              print the version`,
  `Uso: chencho <comando>

  setup [--pi]         instalar el agente, la franja y los hooks de Claude Code, y arrancarlos
                       --pi también permite que otro dispositivo de la red local (p. ej. una pantalla con Raspberry Pi) se conecte
  status               mostrar qué está instalado y corriendo
  uninstall [--purge]  quitar todo; --purge también borra las sesiones ancladas e íconos sin preguntar
  agent [--lan]        ejecutar el agente en primer plano (lo que ejecuta la franja)
  version              mostrar la versión`,
);

const FLAGS: Record<string, Record<string, string>> = {
  agent: { "--lan": "lan" },
  setup: { "--pi": "pi" },
  status: {},
  uninstall: { "--purge": "purge" },
};

export function parseCommand(args: string[]): Command {
  const [name, ...rest] = args;
  if (name === undefined || name === "help" || name === "--help" || name === "-h") return { name: "help" };
  if (name === "version" || name === "--version" || name === "-v") return { name: "version" };
  const allowed = FLAGS[name];
  if (!allowed) return { name: "help", error: t(`unknown command: ${name}`, `comando desconocido: ${name}`) };

  const command: Record<string, unknown> = { name };
  for (const key of Object.values(allowed)) command[key] = false;
  for (const arg of rest) {
    const key = allowed[arg];
    if (!key) return { name: "help", error: t(`unknown option for ${name}: ${arg}`, `opción desconocida para ${name}: ${arg}`) };
    command[key] = true;
  }
  return command as Command;
}
