/**
 * Builds the npm packages into dist/npm: chencho, the launcher, and
 * chencho-win32-x64 with the binaries, which setup copies into place.
 *
 *   bun scripts/release.ts [--version 1.2.3]
 */
import { $ } from "bun";
import { existsSync } from "node:fs";
import { cp, mkdir, mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { parseArgs } from "node:util";

const ROOT = dirname(import.meta.dir);
const DIST = `${ROOT}/dist/npm`;
const TARGET = "win32-x64";
const OVERLAY = `${ROOT}/overlay/Chencho.Overlay/Chencho.Overlay.csproj`;

const { values } = parseArgs({ options: { version: { type: "string" } } });
const version = values.version ?? "0.0.0-dev";

const launcher = await Bun.file(`${ROOT}/npm/chencho/package.json`).json();
const common = { version, license: launcher.license, homepage: launcher.homepage, repository: launcher.repository };

await rm(DIST, { recursive: true, force: true });

const name = `chencho-${TARGET}`;
const bin = `${DIST}/${name}/bin`;
await mkdir(bin, { recursive: true });

await $`bun build ${`${ROOT}/agent/src/chencho.ts`} --compile --minify --target=bun-windows-x64 --define ${`CHENCHO_VERSION=${JSON.stringify(version)}`} --outfile ${`${bin}/chencho.exe`}`;

// Framework-dependent: setup checks for the .NET 9 Desktop Runtime instead of shipping it.
if (existsSync(OVERLAY)) {
  const published = await mkdtemp(join(tmpdir(), "chencho-overlay-"));
  await $`dotnet publish ${OVERLAY} -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ${published}`;
  await cp(join(published, "chencho-overlay.exe"), `${bin}/chencho-overlay.exe`);
  await rm(published, { recursive: true, force: true });
} else {
  console.warn(`warning: no ${OVERLAY}; ${name} ships without chencho-overlay.exe, so its setup will refuse to run`);
}

await Bun.write(
  `${DIST}/${name}/package.json`,
  JSON.stringify({ name, ...common, description: "Windows x64 binaries for chencho", os: ["win32"], cpu: ["x64"], files: ["bin"] }, null, 2) + "\n",
);
await cp(`${ROOT}/LICENSE`, `${DIST}/${name}/LICENSE`);

const main = `${DIST}/chencho`;
await mkdir(`${main}/lib`, { recursive: true });
await cp(`${ROOT}/npm/chencho/bin`, `${main}/bin`, { recursive: true });
await cp(`${ROOT}/npm/chencho/lib/platform.js`, `${main}/lib/platform.js`);
await cp(`${ROOT}/LICENSE`, `${main}/LICENSE`);
// npmjs.com can't resolve the README's relative image path.
const readme = await Bun.file(`${ROOT}/README.md`).text();
await Bun.write(`${main}/README.md`, readme.replaceAll('src="images/', 'src="https://raw.githubusercontent.com/Telayna-I/chencho/main/images/'));
const optionalDependencies = { [name]: version };
await Bun.write(`${main}/package.json`, JSON.stringify({ ...launcher, version, optionalDependencies }, null, 2) + "\n");

console.log(`Built ${version} for ${TARGET} in ${DIST}`);
