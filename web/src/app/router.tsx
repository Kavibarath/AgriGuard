import { createBrowserRouter, createMemoryRouter, Navigate, type RouteObject } from 'react-router'
import { MessagePage } from '@/components/MessagePage'
import { AgentRunPage } from '@/features/agent-runs/AgentRunPage'
import { AgentRunsPage } from '@/features/agent-runs/AgentRunsPage'
import { CaseDetailPage } from '@/features/cases/CaseDetailPage'
import { CasesPage } from '@/features/cases/CasesPage'
import { LoginPage } from '@/features/auth/LoginPage'
import { ProtectedRoute } from '@/features/auth/ProtectedRoute'
import { DashboardPage } from '@/features/dashboard/DashboardPage'
import { InventoryPage } from '@/features/inventory/InventoryPage'
import { OrdersPage } from '@/features/inventory/OrdersPage'
import { CollectionPlannerPage } from '@/features/harvest/CollectionPlannerPage'
import { HarvestPage } from '@/features/harvest/HarvestPage'
import { IntelligencePage } from '@/features/intelligence/IntelligencePage'
import { FarmDetailPage } from '@/features/registry/FarmDetailPage'
import { FarmsPage } from '@/features/registry/FarmsPage'
import { PlotDetailPage } from '@/features/registry/PlotDetailPage'
import { RulesPage } from '@/features/rules/RulesPage'

export const routes: RouteObject[] = [
  { path: '/', element: <Navigate to="/dashboard" replace /> },
  { path: '/login', element: <LoginPage /> },
  {
    element: <ProtectedRoute />,
    children: [
      { path: '/dashboard', element: <DashboardPage /> },
      // Component D's outbreak view is an aggregate with no farmer in it: every signed-in role.
      { path: '/intelligence', element: <IntelligencePage /> },
      // Component A. The OwnsFarm policy admits farmers, agronomists and administrators;
      // the API decides which rows each of them actually sees.
      {
        element: <ProtectedRoute policy="OwnsFarm" />,
        children: [
          { path: '/farms', element: <FarmsPage /> },
          { path: '/farms/:farmId', element: <FarmDetailPage /> },
          { path: '/plots/:plotId', element: <PlotDetailPage /> },
          // Component B. Viewing is open to the same roles (the API scopes the rows); deciding
          // is Field Agronomist only, enforced by the API and explained in the decision panel.
          { path: '/cases', element: <CasesPage /> },
          { path: '/cases/:caseId', element: <CaseDetailPage /> },
          { path: '/agent-runs', element: <AgentRunsPage /> },
          { path: '/agent-runs/:runId', element: <AgentRunPage /> },
          // Component D. Forecasts and bookings are scoped by the API like the registry.
          { path: '/harvest', element: <HarvestPage /> },
          { path: '/collection-planner', element: <CollectionPlannerPage /> },
        ],
      },
      // Component C. A dealer's own shelf and orders; the API limits every row to their shop.
      {
        element: <ProtectedRoute policy="ManagesInventory" />,
        children: [
          { path: '/inventory', element: <InventoryPage /> },
          { path: '/orders', element: <OrdersPage /> },
        ],
      },
      // The regulatory rules table: Co-op Administrator only.
      {
        element: <ProtectedRoute policy="AdministersRules" />,
        children: [{ path: '/rules', element: <RulesPage /> }],
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
