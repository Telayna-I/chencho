const SUPPORTED = new Set(["win32-x64"]);

/** The npm package holding the binaries for this OS and CPU, or null where Chencho doesn't run. */
export function platformPackage(platform, arch) {
  const target = `${platform}-${arch}`;
  return SUPPORTED.has(target) ? `chencho-${target}` : null;
}
