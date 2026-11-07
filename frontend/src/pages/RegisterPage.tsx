import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../features/auth/authStore';
import { ApiError } from '../shared/api/client';
import { useT } from '../shared/i18n';
import { Icon } from '../shared/ui/Icon';

export function RegisterPage() {
  const t = useT();
  const navigate = useNavigate();
  const register = useAuthStore((s) => s.register);
  const [username, setUsername] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [phone, setPhone] = useState('');
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent): Promise<void> {
    e.preventDefault();
    if (password !== confirm) {
      setError(t('register.mismatch'));
      return;
    }
    setError(null);
    setBusy(true);
    try {
      await register({
        username: username.trim().toLowerCase(),
        displayName: displayName.trim(),
        password,
        phone: phone.trim() || undefined,
      });
      await navigate('/');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('register.failed'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="center-page">
      <div className="card">
        <span className="brand-mark" aria-hidden="true">
          <Icon name="send" size={22} />
        </span>
        <h1>{t('register.title')}</h1>
        <p>{t('register.subtitle')}</p>
        <form onSubmit={(e) => void onSubmit(e)}>
          <div className="field">
            <label htmlFor="reg-username">{t('register.username')}</label>
            <input
              id="reg-username"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              autoComplete="username"
              placeholder={t('register.usernamePlaceholder')}
              required
              minLength={3}
              maxLength={32}
            />
          </div>
          <div className="field">
            <label htmlFor="reg-name">{t('register.displayName')}</label>
            <input
              id="reg-name"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              autoComplete="name"
              required
              maxLength={64}
            />
          </div>
          <div className="field">
            <label htmlFor="reg-phone">{t('register.phone')}</label>
            <input
              id="reg-phone"
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              autoComplete="tel"
              placeholder={t('register.phonePlaceholder')}
            />
          </div>
          <div className="field">
            <label htmlFor="reg-password">{t('register.password')}</label>
            <input
              id="reg-password"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="new-password"
              required
              minLength={8}
            />
          </div>
          <div className="field">
            <label htmlFor="reg-confirm">{t('register.confirm')}</label>
            <input
              id="reg-confirm"
              type="password"
              value={confirm}
              onChange={(e) => setConfirm(e.target.value)}
              autoComplete="new-password"
              required
            />
          </div>
          {error ? <div className="form-error">{error}</div> : null}
          <button className="primary-btn" type="submit" disabled={busy}>
            {busy ? t('register.creating') : t('register.create')}
          </button>
        </form>
        <p className="phase-note">
          {t('register.haveAccount')} <Link to="/login">{t('register.signin')}</Link>
        </p>
      </div>
    </div>
  );
}
