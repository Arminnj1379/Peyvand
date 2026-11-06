import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuthStore } from '../features/auth/authStore';
import { ApiError } from '../shared/api/client';
import { Icon } from '../shared/ui/Icon';

export function LoginPage() {
  const navigate = useNavigate();
  const mode = useAuthStore((s) => s.mode);
  const login = useAuthStore((s) => s.login);
  const [loginName, setLoginName] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (mode === 'authenticated') {
    void navigate('/', { replace: true });
  }

  async function onSubmit(e: FormEvent): Promise<void> {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      await login(loginName.trim(), password);
      await navigate('/');
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Sign-in failed.');
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
        <h1>Messenger</h1>
        <p>Enterprise messaging — sign in to continue.</p>
        <form onSubmit={(e) => void onSubmit(e)}>
          <div className="field">
            <label htmlFor="login">Username or phone</label>
            <input
              id="login"
              value={loginName}
              onChange={(e) => setLoginName(e.target.value)}
              autoComplete="username"
              placeholder="sara.m or +989121234567"
              required
            />
          </div>
          <div className="field">
            <label htmlFor="password">Password</label>
            <input
              id="password"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
              required
            />
          </div>
          {error ? <div className="form-error">{error}</div> : null}
          <button className="primary-btn" type="submit" disabled={busy}>
            {busy ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
        <p className="phase-note">
          No account yet? <Link to="/register">Create one</Link>
        </p>
      </div>
    </div>
  );
}
