// Public i18n API. Area string modules self-register on import;
// keep every strings.* module imported here so dictionaries load once.
import './strings.common';
import './strings.pages';
import './strings.chat';
import './strings.panels';

export { useLocaleStore } from './localeStore';
export { applyLocale, getInitialLocale } from './locale';
export type { Locale } from './locale';
export { useT, useLocaleTag } from './useT';
export type { TFunction } from './useT';
