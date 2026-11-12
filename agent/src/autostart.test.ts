import { expect, test } from "bun:test";
import { commandLine, overlayProgram, parseRunValue, parseTasklist } from "./autostart.ts";

const BIN = "C:\\Users\\Jane Doe\\AppData\\Local\\chencho\\bin";

test("the Run value starts the overlay, quoted, passing --lan only when asked", () => {
  expect(overlayProgram(BIN, { lan: false })).toEqual([`${BIN}\\chencho-overlay.exe`]);
  expect(commandLine(overlayProgram(BIN, { lan: false }))).toBe(`"${BIN}\\chencho-overlay.exe"`);
  expect(commandLine(overlayProgram(BIN, { lan: true }))).toBe(`"${BIN}\\chencho-overlay.exe" --lan`);
});

test("reads our value out of `reg query`", () => {
  const output = [
    "",
    "HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run",
    '    OneDrive    REG_SZ    "C:\\Users\\Jane Doe\\AppData\\Local\\Microsoft\\OneDrive\\OneDrive.exe" /background',
    `    Chencho    REG_SZ    "${BIN}\\chencho-overlay.exe" --lan`,
    "    Chencho Helper    REG_SZ    C:\\other.exe",
    "",
  ].join("\r\n");
  expect(parseRunValue(output)).toBe(`"${BIN}\\chencho-overlay.exe" --lan`);
  expect(parseRunValue("\r\n\r\n")).toBeNull();
  expect(parseRunValue("    Chencho Helper    REG_SZ    C:\\other.exe\r\n")).toBeNull();
});

test("reads pids out of `tasklist /FO CSV /NH`, whatever the language of its notices", () => {
  const output = '"chencho.exe","4164","Console","6","90.160 KB"\r\n"Chencho.exe","8396","Console","6","91.004 KB"\r\n';
  expect(parseTasklist(output, "chencho.exe")).toEqual([4164, 8396]);
  expect(parseTasklist(output, "chencho-overlay.exe")).toEqual([]);
  expect(parseTasklist("INFORMACIÓN: no hay tareas ejecutándose que coincidan con los\r\ncriterios especificados.\r\n", "chencho.exe")).toEqual([]);
});
