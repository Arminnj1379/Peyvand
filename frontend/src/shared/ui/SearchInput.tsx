import { Icon } from './Icon';
import { useT } from '../i18n';

export function SearchInput({
  value,
  onChange,
  placeholder,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  const t = useT();
  const text = placeholder ?? t('common.search');
  return (
    <label className="search-box">
      <Icon name="search" size={18} />
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={text}
        aria-label={text}
      />
    </label>
  );
}
