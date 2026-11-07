import type { Locale } from './locale';

type Table = Record<string, string>;

const dicts: Record<Locale, Table> = { en: {}, fa: {} };

/** Registers one area's strings. Each `strings.*.ts` module calls this once per locale. */
export function registerStrings(locale: Locale, strings: Table): void {
  Object.assign(dicts[locale], strings);
}

export function lookup(locale: Locale, key: string): string {
  return dicts[locale][key] ?? dicts.en[key] ?? key;
}
