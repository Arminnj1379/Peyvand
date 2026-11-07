import { create } from 'zustand';
import { applyLocale, getInitialLocale, LOCALE_STORAGE_KEY } from './locale';
import type { Locale } from './locale';

interface LocaleState {
  locale: Locale;
  setLocale: (locale: Locale) => void;
}

function persist(locale: Locale): void {
  try {
    localStorage.setItem(LOCALE_STORAGE_KEY, locale);
  } catch {
    // Private mode: locale still applies to this session.
  }
}

export const useLocaleStore = create<LocaleState>((set) => ({
  locale: getInitialLocale(),
  setLocale: (locale) => {
    applyLocale(locale);
    persist(locale);
    set({ locale });
  },
}));

// Apply the stored (or default) locale before first paint.
applyLocale(useLocaleStore.getState().locale);
