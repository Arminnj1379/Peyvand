export interface CallParticipant {
  userId: string;
  status: 'Invited' | 'Accepted' | 'Rejected';
  isJoined: boolean;
  joinedAt: string | null;
}

export interface CallInfo {
  id: string;
  chatId: string;
  initiatorId: string;
  mediaType: 'Audio' | 'Video';
  status: 'Ringing' | 'Active' | 'Ended';
  roomName: string;
  participants: CallParticipant[];
  createdAt: string;
  startedAt: string | null;
  endedAt: string | null;
  endedBy: string | null;
  endReason: 'Completed' | 'Declined' | 'Cancelled' | null;
  durationSeconds: number;
}

export interface CallJoin {
  call: CallInfo;
  token: string;
  url: string;
  roomName: string;
  identity: string;
}

export function formatCallDuration(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds));
  const m = Math.floor(s / 60);
  const rest = s % 60;
  return m > 0 ? `${m}:${String(rest).padStart(2, '0')}` : `0:${String(rest).padStart(2, '0')}`;
}

export function callHistoryLabel(call: CallInfo, isOutgoing: boolean): string {
  if (call.status !== 'Ended') return call.mediaType === 'Video' ? 'Video call' : 'Voice call';
  const kind = call.mediaType === 'Video' ? 'Video call' : 'Voice call';
  if (call.endReason === 'Declined') return isOutgoing ? `${kind} declined` : `Missed ${kind.toLowerCase()}`;
  if (call.endReason === 'Cancelled') return `${kind} cancelled`;
  return `${kind} · ${formatCallDuration(call.durationSeconds)}`;
}
