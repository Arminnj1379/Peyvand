import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { isAdmin, type User } from '../features/auth/authTypes';
import { supportApi } from '../features/support/supportApi';
import type { Ticket } from '../features/support/supportApi';
import { ApiError } from '../shared/api/client';
import { EmptyState } from '../shared/ui/EmptyState';

const NEXT: Record<string, string[]> = {
  Open: ['Assigned', 'Closed'],
  Assigned: ['InProgress', 'Closed'],
  InProgress: ['Resolved', 'Closed'],
  Resolved: ['InProgress', 'Closed'],
  Closed: [],
};

function isAgent(user: User | null): boolean {
  if (!user) return false;
  return user.roles.includes('SupportAgent') || isAdmin(user);
}

function TicketCard({
  ticket,
  agent,
  myUserId,
  onChanged,
}: {
  ticket: Ticket;
  agent: boolean;
  myUserId: string | undefined;
  onChanged: () => void;
}) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function act(fn: () => Promise<Ticket>): Promise<void> {
    setBusy(true);
    setError(null);
    try {
      await fn();
      onChanged();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Action failed.');
    } finally {
      setBusy(false);
    }
  }

  const next = agent ? (NEXT[ticket.status] ?? []) : ticket.status === 'Closed' ? [] : ['Closed'];

  return (
    <div className="ticket-card">
      <div className="ticket-top">
        <strong>{ticket.subject}</strong>
        <span className={`ticket-status s-${ticket.status.toLowerCase()}`}>{ticket.status}</span>
      </div>
      {ticket.description ? <p className="ticket-desc">{ticket.description}</p> : null}
      <p className="hint">
        {ticket.customerName ?? 'Customer'}
        {ticket.assigneeName ? ` · ${ticket.assigneeName}` : ' · unassigned'} ·{' '}
        {new Date(ticket.createdAt).toLocaleString()}
      </p>
      {ticket.chatId ? <Link to={`/?chat=${ticket.chatId}`}>Open conversation ›</Link> : null}
      {next.length > 0 || (agent && !ticket.assigneeId && ticket.status !== 'Closed') ? (
        <div className="row-actions">
          {agent && !ticket.assigneeId && ticket.status !== 'Closed' ? (
            <button
              className="ghost-btn inline"
              disabled={busy}
              onClick={() => void act(() => supportApi.assign(ticket.id))}
            >
              Take ticket
            </button>
          ) : null}
          {next.map((s) => (
            <button
              key={s}
              className="ghost-btn inline"
              disabled={busy}
              onClick={() => void act(() => supportApi.setStatus(ticket.id, s))}
            >
              {s === 'Closed' && ticket.customerId === myUserId ? 'Close (accept)' : s}
            </button>
          ))}
        </div>
      ) : null}
      {error ? <div className="form-error">{error}</div> : null}
    </div>
  );
}

export function SupportPage({ user }: { user: User | null }) {
  const queryClient = useQueryClient();
  const agent = isAgent(user);
  const [statusFilter, setStatusFilter] = useState('Open');
  const [subject, setSubject] = useState('');
  const [description, setDescription] = useState('');
  const [formError, setFormError] = useState<string | null>(null);
  const [formBusy, setFormBusy] = useState(false);

  const ticketsQuery = useQuery({
    queryKey: ['tickets', agent, statusFilter],
    queryFn: ({ signal }) =>
      agent
        ? supportApi.list({ status: statusFilter === 'All' ? undefined : statusFilter, limit: 30 }, signal)
        : supportApi.list({ mine: true, limit: 30 }, signal),
  });
  const tickets = ticketsQuery.data ?? [];

  async function openTicket(e: FormEvent): Promise<void> {
    e.preventDefault();
    setFormBusy(true);
    setFormError(null);
    try {
      await supportApi.open(subject.trim(), description.trim());
      setSubject('');
      setDescription('');
      await queryClient.invalidateQueries({ queryKey: ['tickets'] });
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : 'Could not open ticket.');
    } finally {
      setFormBusy(false);
    }
  }

  return (
    <div className="center-page">
      <div className="card wide">
        <Link to="/">← Back to chats</Link>
        <h1>{agent ? 'Support inbox' : 'Support'}</h1>

        {agent ? (
          <nav className="folder-tabs" aria-label="Ticket status filter">
            {['Open', 'Assigned', 'InProgress', 'Resolved', 'Closed', 'All'].map((s) => (
              <button
                key={s}
                className={`folder-tab${statusFilter === s ? ' active' : ''}`}
                onClick={() => setStatusFilter(s)}
                aria-pressed={statusFilter === s}
              >
                {s}
              </button>
            ))}
          </nav>
        ) : (
          <form onSubmit={(e) => void openTicket(e)}>
            <h2>Contact support</h2>
            <div className="field">
              <label htmlFor="sup-subject">Subject</label>
              <input
                id="sup-subject"
                value={subject}
                onChange={(e) => setSubject(e.target.value)}
                maxLength={128}
                required
              />
            </div>
            <div className="field">
              <label htmlFor="sup-desc">What happened?</label>
              <textarea
                id="sup-desc"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                maxLength={2000}
                rows={4}
              />
            </div>
            {formError ? <div className="form-error">{formError}</div> : null}
            <button className="primary-btn" type="submit" disabled={formBusy || subject.trim() === ''}>
              {formBusy ? 'Opening…' : 'Open ticket'}
            </button>
          </form>
        )}

        <h2>{agent ? `Queue (${tickets.length})` : 'My tickets'}</h2>
        {ticketsQuery.isPending ? <p className="hint">Loading tickets…</p> : null}
        {ticketsQuery.isError ? (
          <div>
            <div className="form-error">Could not load tickets.</div>
            <button className="ghost-btn inline" onClick={() => void ticketsQuery.refetch()}>
              Retry
            </button>
          </div>
        ) : null}
        {!ticketsQuery.isPending && tickets.length === 0 ? (
          <EmptyState title="No tickets" hint={agent ? 'The queue is clear.' : 'Open one above and an agent will help.'} />
        ) : null}
        {tickets.map((t) => (
          <TicketCard
            key={t.id}
            ticket={t}
            agent={agent}
            myUserId={user?.id}
            onChanged={() => void queryClient.invalidateQueries({ queryKey: ['tickets'] })}
          />
        ))}
      </div>
    </div>
  );
}
