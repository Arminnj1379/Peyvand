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
import { Avatar } from '../shared/ui/Avatar';

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString();
}

export function ProfilePage() {
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
      setProfileMsg('Profile updated.');
    },
    onError: (e) => setProfileMsg(e instanceof ApiError ? e.message : 'Update failed.'),
  });

  const changePw = useMutation({
    mutationFn: () => authApi.changePassword(currentPw, newPw),
    onSuccess: () => {
      setCurrentPw('');
      setNewPw('');
      setPwMsg('Password changed. Other sessions were signed out.');
      void queryClient.invalidateQueries({ queryKey: ['sessions'] });
    },
    onError: (e) => setPwMsg(e instanceof ApiError ? e.message : 'Change failed.'),
  });

  async function onAvatarFile(file: File | undefined): Promise<void> {
    if (!file) return;
    setAvatarMsg(null);
    try {
      const u = await authApi.uploadAvatar(file);
      setUser(u);
      setAvatarBust((n) => n + 1);
    } catch (e) {
      setAvatarMsg(e instanceof ApiError ? e.message : 'Upload failed.');
    }
  }

  async function onRemoveAvatar(): Promise<void> {
    try {
      const u = await authApi.removeAvatar();
      setUser(u);
      setAvatarBust((n) => n + 1);
    } catch (e) {
      setAvatarMsg(e instanceof ApiError ? e.message : 'Remove failed.');
    }
  }

  if (!user) {
    return (
      <div className="center-page">
        <div className="card">
          <p>Loading profile…</p>
        </div>
      </div>
    );
  }

  return (
    <div className="center-page">
      <div className="card wide">
        <Link to="/">← Back to chats</Link>
        <div className="profile-hero">
          {avatarSrc ? (
            <img className="avatar-img" src={avatarSrc} alt="Profile avatar" />
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
              {!user.isActive ? <span className="chip danger">deactivated</span> : null}
            </div>
          </div>
        </div>

        <h2>Avatar</h2>
        <div className="row-actions">
          <label className="ghost-btn inline">
            Upload image
            <input
              type="file"
              accept="image/jpeg,image/png,image/webp"
              hidden
              onChange={(e) => void onAvatarFile(e.target.files?.[0])}
            />
          </label>
          {user.avatarUrl ? (
            <button className="ghost-btn inline" onClick={() => void onRemoveAvatar()}>
              Remove
            </button>
          ) : null}
        </div>
        <p className="hint">JPEG, PNG or WebP, max 2 MB.</p>
        {avatarMsg ? <div className="form-error">{avatarMsg}</div> : null}

        <h2>Profile</h2>
        <form
          onSubmit={(e: FormEvent) => {
            e.preventDefault();
            saveProfile.mutate();
          }}
        >
          <div className="field">
            <label htmlFor="pf-name">Display name</label>
            <input id="pf-name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} maxLength={64} required />
          </div>
          <div className="field">
            <label htmlFor="pf-bio">Bio</label>
            <input id="pf-bio" value={bio} onChange={(e) => setBio(e.target.value)} maxLength={255} />
          </div>
          {profileMsg ? <p className="hint">{profileMsg}</p> : null}
          <button className="primary-btn" type="submit" disabled={saveProfile.isPending}>
            {saveProfile.isPending ? 'Saving…' : 'Save profile'}
          </button>
        </form>

        <h2>Preferences</h2>
        <form
          onSubmit={(e: FormEvent) => {
            e.preventDefault();
            void useSettingsStore
              .getState()
              .update(theme, notify)
              .then(() => setSettingsMsg('Preferences saved.'))
              .catch((err: unknown) =>
                setSettingsMsg(err instanceof ApiError ? err.message : 'Save failed.'),
              );
          }}
        >
          <div className="field">
            <label htmlFor="pf-theme">Theme</label>
            <select id="pf-theme" value={theme} onChange={(e) => setTheme(e.target.value as Theme)}>
              <option value="system">System</option>
              <option value="light">Light</option>
              <option value="dark">Dark</option>
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
              In-app notifications (badges, toasts)
            </label>
          </div>
          {settingsMsg ? <p className="hint">{settingsMsg}</p> : null}
          <button className="primary-btn" type="submit">
            Save preferences
          </button>
        </form>

        <h2>Password</h2>
        <form
          onSubmit={(e: FormEvent) => {
            e.preventDefault();
            changePw.mutate();
          }}
        >
          <div className="field">
            <label htmlFor="pf-cur">Current password</label>
            <input id="pf-cur" type="password" value={currentPw} onChange={(e) => setCurrentPw(e.target.value)} required autoComplete="current-password" />
          </div>
          <div className="field">
            <label htmlFor="pf-new">New password</label>
            <input id="pf-new" type="password" value={newPw} onChange={(e) => setNewPw(e.target.value)} required minLength={8} autoComplete="new-password" />
          </div>
          {pwMsg ? <p className="hint">{pwMsg}</p> : null}
          <button className="primary-btn" type="submit" disabled={changePw.isPending}>
            {changePw.isPending ? 'Changing…' : 'Change password'}
          </button>
        </form>

        <h2>Sessions</h2>
        {sessionsQuery.isPending ? <p className="hint">Loading sessions…</p> : null}
        {sessionsQuery.data?.map((s) => (
          <div key={s.id} className="session-row">
            <div>
              <strong>{s.deviceLabel ?? 'Unknown device'}</strong>
              {s.isCurrent ? <span className="chip">this device</span> : null}
              {s.isRevoked ? <span className="chip danger">revoked</span> : null}
              <div className="hint">
                {s.ipAddress ?? 'no ip'} · last used {fmtDate(s.lastUsedAt)}
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
                Revoke
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
            Sign out all devices
          </button>
          {isAdmin(user) ? (
            <Link className="ghost-btn inline" to="/admin">
              User administration
            </Link>
          ) : null}
          <button
            className="ghost-btn inline"
            onClick={() => {
              void logout().then(() => navigate('/login'));
            }}
          >
            Sign out
          </button>
        </div>
      </div>
    </div>
  );
}
