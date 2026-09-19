import { createBrowserRouter, createMemoryRouter, Navigate, type RouteObject } from 'react-router'
import { MessagePage } from '@/components/MessagePage'
import { LoginPage } from '@/features/auth/LoginPage'
import { ProtectedRoute } from '@/features/auth/ProtectedRoute'
import { DashboardPage } from '@/features/dashboard/DashboardPage'

export const routes: RouteObject[] = [
  { path: '/', element: <Navigate to="/dashboard" replace /> },
  { path: '/login', element: <LoginPage /> },
  {
    element: <ProtectedRoute />,
    children: [
      { path: '/dashboard', element: <DashboardPage /> },
      // Feature areas are added by their owners; until then the dashboard links land here.
      {
        path: '/forbidden',
        element: <MessagePage title="Not available for your role">This area needs a different role. If you think that is wrong, contact the co-op administrator.</MessagePage>,
      },
      { path: '*', element: <MessagePage title="Page not found">That page does not exist yet.</MessagePage> },
    ],
  },
]

export const createAppRouter = () => createBrowserRouter(routes)

/** In-memory router for tests: same route table, no browser history. */
export const createTestRouter = (initialPath: string) =>
  createMemoryRouter(routes, { initialEntries: [initialPath] })
