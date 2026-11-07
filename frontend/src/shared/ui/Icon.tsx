export type IconName =
  | 'search'
  | 'menu'
  | 'phone'
  | 'video'
  | 'info'
  | 'send'
  | 'back'
  | 'check'
  | 'checks'
  | 'dots'
  | 'attach'
  | 'download'
  | 'close'
  | 'mic'
  | 'micOff'
  | 'phoneDown'
  | 'bell'
  | 'file'
  | 'edit'
  | 'reply'
  | 'forward'
  | 'copy'
  | 'pin'
  | 'trash'
  | 'plus'
  | 'archive'
  | 'mute'
  | 'users'
  | 'logout'
  | 'settings'
  | 'chevron'
  | 'emoji'
  | 'verified'
  | 'down'
  | 'gift'
  | 'image'
  | 'stop'
  | 'gif'
  | 'play'
  | 'pause'
  | 'volume'
  | 'volumeOff'
  | 'expand';

function paths(name: IconName): string {
  switch (name) {
    case 'search':
      return '<circle cx="11" cy="11" r="7"/><line x1="21" y1="21" x2="16.5" y2="16.5"/>';
    case 'menu':
      return '<line x1="4" y1="7" x2="20" y2="7"/><line x1="4" y1="12" x2="20" y2="12"/><line x1="4" y1="17" x2="20" y2="17"/>';
    case 'phone':
      return '<path d="M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 2 .7 2.9a2 2 0 0 1-.5 2.1L8.1 10a16 16 0 0 0 6 6l1.3-1.2a2 2 0 0 1 2.1-.5c.9.3 1.9.6 2.9.7a2 2 0 0 1 1.6 2z"/>';
    case 'video':
      return '<rect x="2" y="6" width="13" height="12" rx="2"/><path d="m15 10 7-4v10l-7-4z"/>';
    case 'info':
      return '<circle cx="12" cy="12" r="9"/><line x1="12" y1="11" x2="12" y2="16"/><circle cx="12" cy="8" r="0.5"/>';
    case 'send':
      return '<path d="m22 2-7 20-4-9-9-4z"/><path d="M22 2 11 13"/>';
    case 'back':
      return '<polyline points="15 18 9 12 15 6"/>';
    case 'check':
      return '<polyline points="20 6 9 17 4 12"/>';
    case 'checks':
      return '<polyline points="17 6 6 17 1 12"/><polyline points="22 6 11 17 8 14"/>';
    case 'dots':
      return '<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>';
    case 'attach':
      return '<path d="m21 11-9.5 9.5a5.5 5.5 0 0 1-7.8-7.8L13 3.4a3.7 3.7 0 0 1 5.2 5.2l-9.3 9.3a1.85 1.85 0 0 1-2.6-2.6L15 6.6"/>';
    case 'download':
      return '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/>';
    case 'close':
      return '<line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>';
    case 'mic':
      return '<rect x="9" y="2" width="6" height="12" rx="3"/><path d="M5 10a7 7 0 0 0 14 0"/><line x1="12" y1="17" x2="12" y2="22"/>';
    case 'micOff':
      return '<line x1="2" y1="2" x2="22" y2="22"/><rect x="9" y="2" width="6" height="12" rx="3"/><path d="M5 10a7 7 0 0 0 14 0"/><line x1="12" y1="17" x2="12" y2="22"/>';
    case 'phoneDown':
      return '<path d="M10.7 13.3a16 16 0 0 1-2.6-3.4l1.2-1.2a2 2 0 0 0 .5-2.1c-.3-1-.6-2-.7-2.9A2 2 0 0 0 7.1 2H4.1a2 2 0 0 0-2 2 19.8 19.8 0 0 0 3.1 8.6 19.5 19.5 0 0 0 6 6 19.8 19.8 0 0 0 8.6 3.1 2 2 0 0 0 2-2v-3a2 2 0 0 0-1.7-2c-.9-.1-1.9-.4-2.9-.7a2 2 0 0 0-2.1.5l-1.3 1.3a16 16 0 0 1-3.1-2.5z"/><line x1="2" y1="2" x2="22" y2="22"/>';
    case 'bell':
      return '<path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/>';
    case 'file':
      return '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/>';
    case 'edit':
      return '<path d="M17 3a2.8 2.8 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5z"/>';
    case 'reply':
      return '<polyline points="9 17 4 12 9 7"/><path d="M20 18v-2a4 4 0 0 0-4-4H4"/>';
    case 'forward':
      return '<polyline points="15 17 20 12 15 7"/><path d="M4 18v-2a4 4 0 0 1 4-4h12"/>';
    case 'copy':
      return '<rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>';
    case 'pin':
      return '<path d="M12 17v5"/><path d="M9 3h6l1 7 3 3H5l3-3z"/>';
    case 'trash':
      return '<polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>';
    case 'plus':
      return '<line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>';
    case 'archive':
      return '<rect x="2" y="3" width="20" height="5" rx="1"/><path d="M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8"/><line x1="10" y1="12" x2="14" y2="12"/>';
    case 'mute':
      return '<path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/><line x1="2" y1="2" x2="22" y2="22"/>';
    case 'users':
      return '<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>';
    case 'logout':
      return '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/>';
    case 'settings':
      return '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>';
    case 'chevron':
      return '<polyline points="9 18 15 12 9 6"/>';
    case 'emoji':
      return '<circle cx="12" cy="12" r="9"/><path d="M8 14s1.5 2 4 2 4-2 4-2"/><line x1="9" y1="9" x2="9.01" y2="9"/><line x1="15" y1="9" x2="15.01" y2="9"/>';
    case 'verified':
      return '<path d="M12 2 14.5 3.5 17.5 3.6 18.4 6.5 20.8 8.2 20.1 11.1 21.4 13.8 19.3 15.7 18.9 18.7 15.9 19.2 14.3 21.6 12 20.4 9.7 21.6 8.1 19.2 5.1 18.7 4.7 15.7 2.6 13.8 3.9 11.1 3.2 8.2 5.6 6.5 6.5 3.6 9.5 3.5z"/><polyline points="9 12 11 14 15 10"/>';
    case 'down':
      return '<polyline points="6 9 12 15 18 9"/>';
    case 'gift':
      return '<rect x="3" y="8" width="18" height="4"/><path d="M5 12v9h14v-9"/><path d="M12 8v13"/><path d="M12 8S7 8 5.5 6.5 6 3 8 3.5 12 8 12 8z"/><path d="M12 8s5 0 6.5-1.5S18 3 16 3.5 12 8 12 8z"/>';
    case 'image':
      return '<rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/><path d="m21 15-5-5L5 21"/>';
    case 'stop':
      return '<rect x="6" y="6" width="12" height="12" rx="2" fill="currentColor"/>';
    case 'gif':
      return '<rect x="2" y="5" width="20" height="14" rx="3"/><text x="12" y="15.5" text-anchor="middle" font-size="8.5" font-weight="700" fill="currentColor" stroke="none" font-family="inherit">GIF</text>';
    case 'play':
      return '<polygon points="7 4 20 12 7 20 7 4" fill="currentColor" stroke="none"/>';
    case 'pause':
      return '<rect x="6" y="4" width="4" height="16" rx="1" fill="currentColor" stroke="none"/><rect x="14" y="4" width="4" height="16" rx="1" fill="currentColor" stroke="none"/>';
    case 'volume':
      return '<polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5" fill="currentColor" stroke="none"/><path d="M15.5 8.5a5 5 0 0 1 0 7"/><path d="M18.6 5.4a9.2 9.2 0 0 1 0 13.2"/>';
    case 'volumeOff':
      return '<polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5" fill="currentColor" stroke="none"/><line x1="23" y1="9" x2="17" y2="15"/><line x1="17" y1="9" x2="23" y2="15"/>';
    case 'expand':
      return '<path d="M8 3H5a2 2 0 0 0-2 2v3"/><path d="M16 3h3a2 2 0 0 1 2 2v3"/><path d="M8 21H5a2 2 0 0 1-2-2v-3"/><path d="M16 21h3a2 2 0 0 0 2-2v-3"/>';
  }
}

export function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      dangerouslySetInnerHTML={{ __html: paths(name) }}
    />
  );
}
