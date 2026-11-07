import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { adminApi } from '../features/auth/authApi';
import { ApiError } from '../shared/api/client';
import { useT } from '../shared/i18n';

const ALL_ROLES = ['Employee', 'Customer', 'SupportAgent', 'OrganizationAdministrator', 'SystemAdministrator'];

export function AdminPage() {
  const t = useT();
  const queryClient = useQueryClient();
  const [username, setUsername] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [roles, setRoles] = useState<string[]>(['SupportAgent']);
  const [formMsg, setFormMsg] = useState<string | null>(null);

  const usersQuery = useQuery({
    queryKey: ['admin', 'users'],
    queryFn: ({ signal }) => adminApi.list({ limit: 100 }, signal),
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['admin', 'users'] });

  const create = useMutation({
    mutationFn: () => adminApi.createEmployee({ username: username.trim().toLowerCase(), displayName: displayName.trim(), password, roles }),
    onSuccess: () => {
      setUsername('');
      setDisplayName('');
      setPassword('');
      setFormMsg(null);
      void invalidate();
    },
    onError: (e) => setFormMsg(e instanceof ApiError ? e.message : t('admin.createFailed')),
  });

  function toggleRole(role: string): void {
    setRoles((prev) => (prev.includes(role) ? prev.filter((r) => r !== role) : [...prev, role]));
  }

  async function onSubmit(e: FormEvent): Promise<void> {
    e.preventDefault();
    setFormMsg(null);
    create.mutate();
  }

  return (
    <div className="center-page">
      <div className="card wide">
        <Link to="/">{t('admin.back')}</Link>
        <h1>{t('admin.title')}</h1>
        <p>{t('admin.subtitle')}</p>

        <h2>{t('admin.newEmployee')}</h2>
        <form onSubmit={(e) => void onSubmit(e)}>
          <div className="field">
            <label htmlFor="ad-username">{t('admin.username')}</label>
            <input id="ad-username" value={username} onChange={(e) => setUsername(e.target.value)} required minLength={3} maxLength={32} />
          </div>
          <div className="field">
            <label htmlFor="ad-name">{t('admin.displayName')}</label>
            <input id="ad-name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} required maxLength={64} />
          </div>
          <div className="field">
            <label htmlFor="ad-password">{t('admin.password')}</label>
            <input id="ad-password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required minLength={8} autoComplete="new-password" />
          </div>
          <div className="field">
            <span className="hint">{t('admin.rolesHint')}</span>
            <div className="chip-row">
              {ALL_ROLES.filter((r) => r !== 'Employee').map((r) => (
                <label key={r} className="check">
                  <input type="checkbox" checked={roles.includes(r)} onChange={() => toggleRole(r)} />
                  {r}
                </label>
              ))}
            </div>
          </div>
          {formMsg ? <div className="form-error">{formMsg}</div> : null}
          <button className="primary-btn" type="submit" disabled={create.isPending}>
            {create.isPending ? t('admin.creating') : t('admin.create')}
          </button>
        </form>

        <h2>{t('admin.users')}</h2>
        {usersQuery.isPending ? <p className="hint">{t('common.loading')}</p> : null}
        {usersQuery.isError ? <div className="form-error">{t('admin.loadFailed')}</div> : null}
        <div className="table-scroll">
          <table className="table" aria-label={t('admin.users')}>
          <thead>
            <tr>
              <th>{t('admin.username')}</th>
              <th>{t('admin.nameCol')}</th>
              <th>{t('admin.typeCol')}</th>
              <th>{t('admin.rolesCol')}</th>
              <th>{t('admin.statusCol')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {usersQuery.data?.map((u) => (
              <tr key={u.id}>
                <td>@{u.username}</td>
                <td>{u.displayName}</td>
                <td>{u.userType}</td>
                <td>
                  <div className="chip-row">
                    {u.roles.map((r) => (
                      <span key={r} className="chip">
                        {r}
                      </span>
                    ))}
                  </div>
                </td>
                <td>{u.isActive ? t('admin.active') : t('admin.deactivated')}</td>
                <td>
                  <button
                    className="ghost-btn inline small"
                    onClick={() => adminApi.setActive(u.id, !u.isActive).then(() => invalidate())}
                  >
                    {u.isActive ? t('admin.deactivate') : t('admin.activate')}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
