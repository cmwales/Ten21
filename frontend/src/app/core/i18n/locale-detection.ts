export const SUPPORTED_LANGUAGES = ['en-US', 'es-US', 'fr-CA'] as const;
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];
export const DEFAULT_LANGUAGE: SupportedLanguage = 'en-US';

/**
 * DESIGN_SYSTEM.md §1.3: auto-detect locale via navigator.language, falling back to
 * en-US for anything outside the three supported locales. Matches purely on base
 * language (the part before "-") since browsers report a wide variety of region tags
 * ("fr", "fr-FR", "es-MX", ...) that don't exactly match our three locale codes.
 */
export function detectLocale(): SupportedLanguage {
  const browserLang = (typeof navigator !== 'undefined' ? navigator.language : '') ?? '';
  const baseLang = browserLang.toLowerCase().split('-')[0];

  const match = SUPPORTED_LANGUAGES.find(
    (supported) => supported.toLowerCase().split('-')[0] === baseLang,
  );

  return match ?? DEFAULT_LANGUAGE;
}
