import { Navigate, Outlet } from 'react-router-dom';
import { isAdmin } from './authTypes';
import { useAuthStore } from './authStore';

export function PrivateRoute() {
  const mode = useAuthStore((s) => s.mode);
  if (mode === 'anonymous') {
    return <Navigate to="/login" replace />;
  }
  return <Outlet />;
}

export function AdminRoute() {
  const mode = useAuthStore((s) => s.mode);
  const user = useAuthStore((s) => s.user);
  if (mode === 'anonymous') {
    return <Navigate to="/login" replace />;
  }
  if (!isAdmin(user)) {
    return <Navigate to="/" replace />;
  }
  return <Outlet />;
}
