#!/usr/bin/env node
// npm installs only the optional dependency that matches this PC; run the `chencho.exe` binary it holds.
import { spawnSync } from "node:child_process";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { platformPackage } from "../lib/platform.js";

const name = platformPackage(process.platform, process.arch);
if (!name) {
  console.error(`chencho runs on Windows only (this is ${process.platform}-${process.arch}).`);
  process.exit(1);
}

let binary;
try {
  binary = join(dirname(createRequire(import.meta.url).resolve(`${name}/package.json`)), "bin", "chencho.exe");
} catch {
  console.error(`chencho: ${name} is not installed. Install again without --omit=optional.`);
  process.exit(1);
}

const result = spawnSync(binary, process.argv.slice(2), { stdio: "inherit" });
if (result.error) {
  console.error(`chencho: ${result.error.message}`);
  process.exit(1);
}
process.exit(result.status ?? 1);
