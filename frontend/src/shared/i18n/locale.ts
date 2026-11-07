export type Locale = 'en' | 'fa';

export const LOCALE_STORAGE_KEY = 'messenger-locale';
export const DEFAULT_LOCALE: Locale = 'en';

export function getInitialLocale(): Locale {
  try {
    const saved = localStorage.getItem(LOCALE_STORAGE_KEY);
    if (saved === 'fa' || saved === 'en') return saved;
  } catch {
    // Private mode: fall through to default.
  }
  return DEFAULT_LOCALE;
}

/**
 * Applies the locale to the document: lang attribute plus text direction
 * (Persian is RTL, English is LTR). Layout uses flex/grid so it mirrors
 * automatically; bidi user content keeps working via dir="auto".
 */
export function applyLocale(locale: Locale): void {
  document.documentElement.lang = locale;
  document.documentElement.dir = locale === 'fa' ? 'rtl' : 'ltr';
}
