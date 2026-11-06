import { useQuery } from '@tanstack/react-query';
import { authApi } from './authApi';

/**
 * Shared profile lookup (DM peers, member lists). Same cache key everywhere
 * (`['user', id]`, 60 s stale) so the list, header, and details panel share
 * one request per user. Disabled without an id — safe to call unconditionally.
 */
export function useUserQuery(userId: string | null | undefined) {
  return useQuery({
    queryKey: ['user', userId],
    queryFn: ({ signal }) => authApi.user(userId!, signal),
    enabled: userId !== null && userId !== undefined,
    staleTime: 60_000,
  });
}
