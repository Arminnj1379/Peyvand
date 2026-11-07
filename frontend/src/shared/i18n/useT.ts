import { useLocaleStore } from './localeStore';
import { lookup } from './registry';

export type TFunction = (key: string, vars?: Record<string, string | number>) => string;

function interpolate(template: string, vars?: Record<string, string | number>): string {
  if (!vars) return template;
  return template.replace(/\{(\w+)\}/g, (match, name: string) =>
    vars[name] !== undefined ? String(vars[name]) : match,
  );
}

/** Returns a translate function bound to the active locale (falls back to English, then the key). */
export function useT(): TFunction {
  const locale = useLocaleStore((s) => s.locale);
  return (key, vars) => interpolate(lookup(locale, key), vars);
}

/** Locale-aware date/time formatting (Persian digits/calendar come from the Intl locale). */
export function useLocaleTag(): string {
  const locale = useLocaleStore((s) => s.locale);
  return locale === 'fa' ? 'fa-IR' : 'en-US';
}
