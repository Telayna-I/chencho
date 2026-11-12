import { expect, test } from "bun:test";
import { pickLanguage } from "./i18n.ts";

test("the overlay's language setting wins over the locale", () => {
  expect(pickLanguage("en", "es-MX")).toBe("en");
  expect(pickLanguage("es", "en-US")).toBe("es");
  expect(pickLanguage("es", undefined)).toBe("es");
});

test("auto, a missing or a garbage setting follows the locale", () => {
  for (const preference of ["auto", undefined, null, "", "ES", "fr", 1, { language: "es" }]) {
    expect(pickLanguage(preference, "es-MX")).toBe("es");
    expect(pickLanguage(preference, "en-US")).toBe("en");
  }
});

test("Spanish for any locale whose language is es, English otherwise", () => {
  expect(pickLanguage("auto", "es-AR")).toBe("es");
  expect(pickLanguage("auto", "es")).toBe("es");
  expect(pickLanguage("auto", "es-419")).toBe("es");
  expect(pickLanguage("auto", "en-US")).toBe("en");
  expect(pickLanguage("auto", "pt-BR")).toBe("en");
  expect(pickLanguage("auto", "est")).toBe("en");
  expect(pickLanguage("auto", "")).toBe("en");
  expect(pickLanguage("auto", undefined)).toBe("en");
});
