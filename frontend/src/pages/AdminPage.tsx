import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { adminApi } from '../features/auth/authApi';
import { ApiError } from '../shared/api/client';

const ALL_ROLES = ['Employee', 'Customer', 'SupportAgent', 'OrganizationAdministrator', 'SystemAdministrator'];

export function AdminPage() {
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
    onError: (e) => setFormMsg(e instanceof ApiError ? e.message : 'Create failed.'),
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
        <Link to="/">← Back to chats</Link>
        <h1>User administration</h1>
        <p>Create employee accounts and manage activation.</p>

        <h2>New employee</h2>
        <form onSubmit={(e) => void onSubmit(e)}>
          <div className="field">
            <label htmlFor="ad-username">Username</label>
            <input id="ad-username" value={username} onChange={(e) => setUsername(e.target.value)} required minLength={3} maxLength={32} />
          </div>
          <div className="field">
            <label htmlFor="ad-name">Display name</label>
            <input id="ad-name" value={displayName} onChange={(e) => setDisplayName(e.target.value)} required maxLength={64} />
          </div>
          <div className="field">
            <label htmlFor="ad-password">Password</label>
            <input id="ad-password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required minLength={8} autoComplete="new-password" />
          </div>
          <div className="field">
            <span className="hint">Roles (Employee is always included)</span>
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
            {create.isPending ? 'Creating…' : 'Create employee'}
          </button>
        </form>

        <h2>Users</h2>
        {usersQuery.isPending ? <p className="hint">Loading…</p> : null}
        {usersQuery.isError ? <div className="form-error">Could not load users.</div> : null}
        <div className="table-scroll">
          <table className="table" aria-label="Users">
          <thead>
            <tr>
              <th>Username</th>
              <th>Name</th>
              <th>Type</th>
              <th>Roles</th>
              <th>Status</th>
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
                <td>{u.isActive ? 'active' : 'deactivated'}</td>
                <td>
                  <button
                    className="ghost-btn inline small"
                    onClick={() => adminApi.setActive(u.id, !u.isActive).then(() => invalidate())}
                  >
                    {u.isActive ? 'Deactivate' : 'Activate'}
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
