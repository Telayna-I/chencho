import { runAgent } from "./agent.ts";
import { parseCommand, USAGE } from "./cli.ts";
import { setup, uninstall } from "./install.ts";
import { status } from "./status.ts";

declare const CHENCHO_VERSION: string | undefined;
const VERSION = typeof CHENCHO_VERSION === "string" ? CHENCHO_VERSION : "dev";

const command = parseCommand(process.argv.slice(2));
try {
  switch (command.name) {
    case "agent":
      await runAgent({ lan: command.lan });
      break;
    case "setup":
      await setup({ pi: command.pi });
      break;
    case "status":
      await status(VERSION);
      break;
    case "uninstall":
      await uninstall({ purge: command.purge });
      break;
    case "version":
      console.log(VERSION);
      break;
    case "help":
      if (command.error) console.error(`chencho: ${command.error}\n`);
      console.log(USAGE);
      process.exit(command.error ? 1 : 0);
  }
} catch (error) {
  console.error(`chencho: ${error instanceof Error ? error.message : String(error)}`);
  process.exit(1);
}
