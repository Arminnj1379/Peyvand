export interface User {
  id: string;
  username: string;
  displayName: string;
  bio: string;
  avatarUrl: string | null;
  userType: string;
  roles: string[];
  isActive: boolean;
  phone: string | null;
  lastSeenAt: string | null;
}

export interface AuthResult {
  accessToken: string;
  accessExpiresAt: string;
  refreshToken: string;
  refreshExpiresAt: string;
  sessionId: string;
  user: User;
}

export interface Session {
  id: string;
  deviceLabel: string | null;
  ipAddress: string | null;
  createdAt: string;
  lastUsedAt: string;
  expiresAt: string;
  isCurrent: boolean;
  isRevoked: boolean;
}

export function isAdmin(user: User | null): boolean {
  return !!user && (user.roles.includes('SystemAdministrator') || user.roles.includes('OrganizationAdministrator'));
}
