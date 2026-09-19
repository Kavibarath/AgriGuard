import { Navigate, Outlet, useLocation } from 'react-router'
import { useCurrentUser, useIsAuthenticated } from './auth-store'
import { can, type Policy } from './policies'

/**
 * Route guard. Unauthenticated → /login (remembering where we were). Authenticated but not
 * admitted by `policy` → /forbidden. The API enforces the same policy server-side; this only
 * spares the user a screen they cannot use.
 */
export function ProtectedRoute({ policy }: { policy?: Policy }) {
  const isAuthenticated = useIsAuthenticated()
  const user = useCurrentUser()
  const location = useLocation()

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  }

  if (policy && !can(user?.role, policy)) {
    return <Navigate to="/forbidden" replace />
  }

  return <Outlet />
}
