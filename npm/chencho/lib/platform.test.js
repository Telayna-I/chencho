import { expect, test } from "bun:test";
import { platformPackage } from "./platform.js";

test("picks the package with the binaries for this PC", () => {
  expect(platformPackage("win32", "x64")).toBe("chencho-win32-x64");
});

test("anything but 64-bit Windows on x64 has none", () => {
  expect(platformPackage("darwin", "arm64")).toBeNull();
  expect(platformPackage("linux", "x64")).toBeNull();
  expect(platformPackage("win32", "arm64")).toBeNull();
  expect(platformPackage("win32", "ia32")).toBeNull();
});
