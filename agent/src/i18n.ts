/**
 * The language of the CLI's human-readable text: the overlay's `language` setting in overlay.json
 * ("auto", "en" or "es"), with "auto" following the system locale. The agent's logs and anything
 * sent over the WebSocket stay in English; the overlay translates those itself.
 *
 * `CHENCHO_LANG=en|es` overrides both. The tests set it to "en" (see test-setup.ts) so their
 * assertions on English text pass on any machine.
 */
import { readFileSync } from "node:fs";
import { join } from "node:path";
import { DATA_DIR } from "./config.ts";

export type Language = "en" | "es";

const isLanguage = (value: unknown): value is Language => value === "en" || value === "es";

/** `preference` is overlay.json's `language`; anything but "en" or "es" means the locale's language. */
export function pickLanguage(preference: unknown, locale: string | undefined): Language {
  if (isLanguage(preference)) return preference;
  return /^es(?:[-_]|$)/i.test(locale ?? "") ? "es" : "en";
}

function overlayPreference(): unknown {
  try {
    return JSON.parse(readFileSync(join(DATA_DIR, "overlay.json"), "utf8"))?.language;
  } catch {
    return undefined;
  }
}

const override = process.env.CHENCHO_LANG;
export const language: Language = isLanguage(override)
  ? override
  : pickLanguage(overlayPreference(), Intl.DateTimeFormat().resolvedOptions().locale);

/** The text in the CLI's language, with both versions side by side at the call site. */
export function t(en: string, es: string): string {
  return language === "es" ? es : en;
}
