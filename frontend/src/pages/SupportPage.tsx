import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { isAdmin, type User } from '../features/auth/authTypes';
import { supportApi } from '../features/support/supportApi';
import type { Ticket } from '../features/support/supportApi';
import { ApiError } from '../shared/api/client';
import { useLocaleTag, useT } from '../shared/i18n';
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
  const t = useT();
  const localeTag = useLocaleTag();

  async function act(fn: () => Promise<Ticket>): Promise<void> {
    setBusy(true);
    setError(null);
    try {
      await fn();
      onChanged();
    } catch (e) {
      setError(e instanceof ApiError ? e.message : t('support.actionFailed'));
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
        {ticket.customerName ?? t('support.customer')}
        {ticket.assigneeName ? ` · ${ticket.assigneeName}` : ` · ${t('support.unassigned')}`} ·{' '}
        {new Date(ticket.createdAt).toLocaleString(localeTag)}
      </p>
      {ticket.chatId ? <Link to={`/?chat=${ticket.chatId}`}>{t('support.openConversation')}</Link> : null}
      {next.length > 0 || (agent && !ticket.assigneeId && ticket.status !== 'Closed') ? (
        <div className="row-actions">
          {agent && !ticket.assigneeId && ticket.status !== 'Closed' ? (
            <button
              className="ghost-btn inline"
              disabled={busy}
              onClick={() => void act(() => supportApi.assign(ticket.id))}
            >
              {t('support.takeTicket')}
            </button>
          ) : null}
          {next.map((s) => (
            <button
              key={s}
              className="ghost-btn inline"
              disabled={busy}
              onClick={() => void act(() => supportApi.setStatus(ticket.id, s))}
            >
              {s === 'Closed' && ticket.customerId === myUserId ? t('support.closeAccept') : s}
            </button>
          ))}
        </div>
      ) : null}
      {error ? <div className="form-error">{error}</div> : null}
    </div>
  );
}

export function SupportPage({ user }: { user: User | null }) {
  const t = useT();
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
      setFormError(err instanceof ApiError ? err.message : t('support.openFailed'));
    } finally {
      setFormBusy(false);
    }
  }

  return (
    <div className="center-page">
      <div className="card wide">
        <Link to="/">{t('support.back')}</Link>
        <h1>{agent ? t('support.inbox') : t('support.title')}</h1>

        {agent ? (
          <nav className="folder-tabs" aria-label={t('support.filterLabel')}>
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
            <h2>{t('support.contact')}</h2>
            <div className="field">
              <label htmlFor="sup-subject">{t('support.subject')}</label>
              <input
                id="sup-subject"
                value={subject}
                onChange={(e) => setSubject(e.target.value)}
                maxLength={128}
                required
              />
            </div>
            <div className="field">
              <label htmlFor="sup-desc">{t('support.whatHappened')}</label>
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
              {formBusy ? t('support.opening') : t('support.openTicket')}
            </button>
          </form>
        )}

        <h2>{agent ? t('support.queue', { count: tickets.length }) : t('support.myTickets')}</h2>
        {ticketsQuery.isPending ? <p className="hint">{t('support.loading')}</p> : null}
        {ticketsQuery.isError ? (
          <div>
            <div className="form-error">{t('support.loadFailed')}</div>
            <button className="ghost-btn inline" onClick={() => void ticketsQuery.refetch()}>
              {t('common.retry')}
            </button>
          </div>
        ) : null}
        {!ticketsQuery.isPending && tickets.length === 0 ? (
          <EmptyState title={t('support.noTickets')} hint={agent ? t('support.queueClear') : t('support.openOne')} />
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
