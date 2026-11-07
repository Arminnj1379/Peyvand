import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { authApi } from '../features/auth/authApi';
import { isAdmin } from '../features/auth/authTypes';
import { useAuthStore } from '../features/auth/authStore';
import { useAvatarUrl } from '../features/auth/useAvatar';
import { useSettingsStore } from '../features/settings/settingsStore';
import type { Theme } from '../features/settings/settingsStore';
import { ApiError } from '../shared/api/client';
import { useLocaleTag, useT } from '../shared/i18n';
import { Avatar } from '../shared/ui/Avatar';

function fmtDate(iso: string, localeTag: string): string {
  return new Date(iso).toLocaleString(localeTag);
}

export function ProfilePage() {
  const t = useT();
  const localeTag = useLocaleTag();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const user = useAuthStore((s) => s.user);
  const setUser = useAuthStore((s) => s.setUser);
  const logout = useAuthStore((s) => s.logout);

  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [bio, setBio] = useState(user?.bio ?? '');
  const [profileMsg, setProfileMsg] = useState<string | null>(null);
  const [currentPw, setCurrentPw] = useState('');
  const [newPw, setNewPw] = useState('');
  const [pwMsg, setPwMsg] = useState<string | null>(null);
  const [avatarBust, setAvatarBust] = useState(0);
  const [avatarMsg, setAvatarMsg] = useState<string | null>(null);
  const settingsTheme = useSettingsStore((s) => s.theme);
  const settingsNotifications = useSettingsStore((s) => s.notificationsEnabled);
  const [theme, setTheme] = useState<Theme>(settingsTheme);
  const [notify, setNotify] = useState(settingsNotifications);
  const [settingsMsg, setSettingsMsg] = useState<string | null>(null);

  const avatarSrc = useAvatarUrl(user?.avatarUrl ?? null, avatarBust);

  const sessionsQuery = useQuery({
    queryKey: ['sessions'],
    queryFn: ({ signal }) => authApi.sessions(signal),
  });

  const saveProfile = useMutation({
    mutationFn: () => authApi.updateMe(displayName.trim(), bio),
    onSuccess: (u) => {
      setUser(u);
      setDisplayName(u.displayName);
      setBio(u.bio);
      setProfileMsg(t('profile.updated'));
    },
    onError: (e) => setProfileMsg(e instanceof ApiError ? e.message : t('profile.updateFailed')),
  });

  const changePw = useMutation({
    mutationFn: () => authApi.changePassword(currentPw, newPw),
    onSuccess: () => {
      setCurrentPw('');
      setNewPw('');
      setPwMsg(t('profile.pwChanged'));
      void queryClient.invalidateQueries({ queryKey: ['sessions'] });
    },
    onError: (e) => setPwMsg(e instanceof ApiError ? e.message : t('profile.changeFailed')),
  });

  async function onAvatarFile(file: File | undefined): Promise<void> {
    if (!file) return;
    setAvatarMsg(null);
    try {
      const u = await authApi.uploadAvatar(file);
      setUser(u);
      setAvatarBust((n) => n + 1);
      await queryClient.invalidateQueries({ queryKey: ['user', u.id] });
    } catch (e) {
      setAvatarMsg(e instanceof ApiError ? e.message : t('profile.uploadFailed'));
    }
  }

  async function onRemoveAvatar(): Promise<void> {
    try {
      const u = await authApi.removeAvatar();
      setUser(u);
      setAvatarBust((n) => n + 1);
      await queryClient.invalidateQueries({ queryKey: ['user', u.id] });
    } catch (e) {
      setAvatarMsg(e instanceof ApiError ? e.message : t('profile.removeFailed'));
    }
  }

  if (!user) {
    return (
      <div className="center-page">
        <div className="card">
          <p>{t('profile.loading')}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="center-page">
      <div className="card wide">
        <Link to="/">{t('profile.back')}</Link>
        <div className="profile-hero">
          {avatarSrc ? (
            <img className="avatar-img" src={avatarSrc} alt={t('profile.avatarAlt')} />
          ) : (
            <Avatar name={user.displayName} color="#2aabee" size={96} />
          )}
          <div>
            <h1>{user.displayName}</h1>
            <p>
              @{user.username} · {user.userType}
            </p>
            <div className="chip-row">
              {user.roles.map((r) => (
                <span key={r} className="chip">
                  {r}
                </span>
              ))}
              {!user.isActive ? <span className="chip danger">{t('profile.deactivated')}</span> : null}
            </div>
          </div>
        </div>

        <h2>{t('profile.avatar')}</h2>
        <div className="row-actions">
          <label className="ghost-btn inline">
            {t('common.uploadImage')}
            <input
              type="file"
              accept="image/jpeg,image/png,image/webp"
              hidden
              onChange={(e) => void onAvatarFile(e.target.files?.[0])}
            />
          </label>
          {user.avatarUrl ? (
            <button className="ghost-btn inline" onClick={() => void onRemoveAvatar()}>
              {t('common.remove')}
            </button>
          ) : null}
        </div>
        <p className="hint">{t('profile.avatarHint')}</p>
        {avatarMsg ? <div className="form-error">{avatarMsg}</div> : null}

        <h2>{t('profile.profile')}</h2>
        <form
          onSubmit={(e: FormEvent) => {
            e.preventDefault();
            saveProfile.mutate();
          }}
        >
          <div className="field">
            <label htmlFor="pf-name">{t('profile.displayName')}</label>
            <input id="pf-name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} maxLength={64} required />
          </div>
          <div className="field">
            <label htmlFor="pf-bio">{t('profile.bio')}</label>
            <input id="pf-bio" value={bio} onChange={(e) => setBio(e.target.value)} maxLength={255} />
          </div>
          {profileMsg ? <p className="hint">{profileMsg}</p> : null}
          <button className="primary-btn" type="submit" disabled={saveProfile.isPending}>
            {saveProfile.isPending ? t('common.saving') : t('profile.saveProfile')}
          </button>
        </form>

        <h2>{t('profile.preferences')}</h2>
        <form
          onSubmit={(e: FormEvent) => {
            e.preventDefault();
            void useSettingsStore
              .getState()
              .update(theme, notify)
              .then(() => setSettingsMsg(t('profile.preferencesSaved')))
              .catch((err: unknown) =>
                setSettingsMsg(err instanceof ApiError ? err.message : t('profile.saveFailed')),
              );
          }}
        >
          <div className="field">
            <label htmlFor="pf-theme">{t('profile.theme')}</label>
            <select id="pf-theme" value={theme} onChange={(e) => setTheme(e.target.value as Theme)}>
              <option value="system">{t('profile.themeSystem')}</option>
              <option value="light">{t('profile.themeLight')}</option>
              <option value="dark">{t('profile.themeDark')}</option>
            </select>
          </div>
          <div className="field check">
            <label htmlFor="pf-notify">
              <input
                id="pf-notify"
                type="checkbox"
                checked={notify}
                onChange={(e) => setNotify(e.target.checked)}
              />
              {t('profile.notify')}
            </label>
          </div>
          {settingsMsg ? <p className="hint">{settingsMsg}</p> : null}
          <button className="primary-btn" type="submit">
            {t('profile.savePreferences')}
          </button>
        </form>

        <h2>{t('profile.password')}</h2>
        <form
          onSubmit={(e: FormEvent) => {
            e.preventDefault();
            changePw.mutate();
          }}
        >
          <div className="field">
            <label htmlFor="pf-cur">{t('profile.currentPassword')}</label>
            <input id="pf-cur" type="password" value={currentPw} onChange={(e) => setCurrentPw(e.target.value)} required autoComplete="current-password" />
          </div>
          <div className="field">
            <label htmlFor="pf-new">{t('profile.newPassword')}</label>
            <input id="pf-new" type="password" value={newPw} onChange={(e) => setNewPw(e.target.value)} required minLength={8} autoComplete="new-password" />
          </div>
          {pwMsg ? <p className="hint">{pwMsg}</p> : null}
          <button className="primary-btn" type="submit" disabled={changePw.isPending}>
            {changePw.isPending ? t('profile.changing') : t('profile.changePassword')}
          </button>
        </form>

        <h2>{t('profile.sessions')}</h2>
        {sessionsQuery.isPending ? <p className="hint">{t('profile.loadingSessions')}</p> : null}
        {sessionsQuery.data?.map((s) => (
          <div key={s.id} className="session-row">
            <div>
              <strong>{s.deviceLabel ?? t('profile.unknownDevice')}</strong>
              {s.isCurrent ? <span className="chip">{t('common.thisDevice')}</span> : null}
              {s.isRevoked ? <span className="chip danger">{t('common.revoked')}</span> : null}
              <div className="hint">
                {s.ipAddress ?? t('profile.noIp')} · {t('profile.lastUsed', { date: fmtDate(s.lastUsedAt, localeTag) })}
              </div>
            </div>
            {!s.isRevoked ? (
              <button
                className="ghost-btn inline"
                onClick={() =>
                  authApi
                    .revokeSession(s.id)
                    .then(() => queryClient.invalidateQueries({ queryKey: ['sessions'] }))
                    .then(() => {
                      if (s.isCurrent) void logout().then(() => navigate('/login'));
                    })
                }
              >
                {t('common.revoke')}
              </button>
            ) : null}
          </div>
        ))}
        <div className="row-actions">
          <button
            className="ghost-btn inline"
            onClick={() =>
              authApi.logoutAll().then(() => {
                void logout();
                void navigate('/login');
              })
            }
          >
            {t('profile.signOutAll')}
          </button>
          {isAdmin(user) ? (
            <Link className="ghost-btn inline" to="/admin">
              {t('profile.adminLink')}
            </Link>
          ) : null}
          <button
            className="ghost-btn inline"
            onClick={() => {
              void logout().then(() => navigate('/login'));
            }}
          >
            {t('common.signOut')}
          </button>
        </div>
      </div>
    </div>
  );
}
