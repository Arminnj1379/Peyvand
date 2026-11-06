import { createBrowserRouter } from 'react-router-dom';
import { AdminRoute, PrivateRoute } from '../features/auth/PrivateRoute';
import { AdminPage } from '../pages/AdminPage';
import { LoginPage } from '../pages/LoginPage';
import { MessengerPage } from '../pages/MessengerPage';
import { NotFoundPage } from '../pages/NotFoundPage';
import { ProfilePage } from '../pages/ProfilePage';
import { RegisterPage } from '../pages/RegisterPage';
import { SupportPage } from '../pages/SupportPage';
import { useAuthStore } from '../features/auth/authStore';

function SupportRoute() {
  const user = useAuthStore((s) => s.user);
  return <SupportPage user={user} />;
}

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/register', element: <RegisterPage /> },
  {
    element: <PrivateRoute />,
    children: [
      { path: '/', element: <MessengerPage /> },
      { path: '/profile', element: <ProfilePage /> },
      { path: '/support', element: <SupportRoute /> },
    ],
  },
  {
    element: <AdminRoute />,
    children: [{ path: '/admin', element: <AdminPage /> }],
  },
  { path: '*', element: <NotFoundPage /> },
]);
