import { createBrowserRouter, createMemoryRouter, Navigate, type RouteObject } from 'react-router'
import { MessagePage } from '@/components/MessagePage'
import { AgentRunPage } from '@/features/agent-runs/AgentRunPage'
import { AgentRunsPage } from '@/features/agent-runs/AgentRunsPage'
import { LoginPage } from '@/features/auth/LoginPage'
import { ProtectedRoute } from '@/features/auth/ProtectedRoute'
import { DashboardPage } from '@/features/dashboard/DashboardPage'
import { FarmDetailPage } from '@/features/registry/FarmDetailPage'
import { FarmsPage } from '@/features/registry/FarmsPage'

export const routes: RouteObject[] = [
  { path: '/', element: <Navigate to="/dashboard" replace /> },
  { path: '/login', element: <LoginPage /> },
  {
    element: <ProtectedRoute />,
    children: [
      { path: '/dashboard', element: <DashboardPage /> },
      // Component A. The OwnsFarm policy admits farmers, agronomists and administrators;
      // the API decides which rows each of them actually sees.
      {
        element: <ProtectedRoute policy="OwnsFarm" />,
        children: [
          { path: '/farms', element: <FarmsPage /> },
          { path: '/farms/:farmId', element: <FarmDetailPage /> },
          // Component B. Viewing is open to the same roles (the API scopes the rows); deciding
          // is Field Agronomist only, enforced by the API and explained in the decision panel.
          { path: '/agent-runs', element: <AgentRunsPage /> },
          { path: '/agent-runs/:runId', element: <AgentRunPage /> },
        ],
      },
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
