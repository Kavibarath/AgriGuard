import type { ReactNode } from 'react'
import {
  Basket,
  Clipboard,
  Fields,
  Home,
  Package,
  Pulse,
  Receipt,
  Scale,
  Truck,
  Workflow,
} from '@/components/icons'
import type { Policy } from '@/features/auth/policies'

export interface NavItem {
  to: string
  label: string
  icon: ReactNode
  /** Omitted: every signed-in role. The same policies the dashboard's area cards used. */
  policy?: Policy
}

export interface NavGroup {
  label?: string
  items: NavItem[]
}

/**
 * The sidebar, grouped by the work it serves. Each item is shown only to roles its policy admits;
 * the route and the API enforce the same policy, so this only spares people links they cannot use.
 */
export const navigation: NavGroup[] = [
  { items: [{ to: '/dashboard', label: 'Dashboard', icon: <Home /> }] },
  {
    label: 'Field work',
    items: [
      { to: '/cases', label: 'Crop cases', icon: <Clipboard />, policy: 'OwnsFarm' },
      { to: '/agent-runs', label: 'Agent runs', icon: <Workflow />, policy: 'CanApprovePrescriptions' },
    ],
  },
  {
    label: 'Farms and harvest',
    items: [
      { to: '/farms', label: 'Farms & plots', icon: <Fields />, policy: 'OwnsFarm' },
      { to: '/harvest', label: 'Harvests', icon: <Basket />, policy: 'OwnsFarm' },
      { to: '/collection-planner', label: 'Collection planner', icon: <Truck />, policy: 'OwnsFarm' },
    ],
  },
  {
    label: 'Dealer',
    items: [
      { to: '/inventory', label: 'Inventory', icon: <Package />, policy: 'ManagesInventory' },
      { to: '/orders', label: 'Orders', icon: <Receipt />, policy: 'ManagesInventory' },
    ],
  },
  { label: 'Insight', items: [{ to: '/intelligence', label: 'Disease intelligence', icon: <Pulse /> }] },
  { label: 'Administration', items: [{ to: '/rules', label: 'Regulatory rules', icon: <Scale />, policy: 'AdministersRules' }] },
]

/** Breadcrumb names: the top-level section, then what a detail page shows. */
const sections: Record<string, { label: string; to: string; detail?: string }> = {
  dashboard: { label: 'Dashboard', to: '/dashboard' },
  cases: { label: 'Crop cases', to: '/cases', detail: 'Case' },
  'agent-runs': { label: 'Agent runs', to: '/agent-runs', detail: 'Run' },
  farms: { label: 'Farms & plots', to: '/farms', detail: 'Farm' },
  plots: { label: 'Farms & plots', to: '/farms', detail: 'Plot' },
  harvest: { label: 'Harvests', to: '/harvest' },
  'collection-planner': { label: 'Collection planner', to: '/collection-planner' },
  intelligence: { label: 'Disease intelligence', to: '/intelligence' },
  inventory: { label: 'Inventory', to: '/inventory' },
  orders: { label: 'Orders', to: '/orders' },
  rules: { label: 'Regulatory rules', to: '/rules' },
  'design-system': { label: 'Design system', to: '/design-system' },
}

export interface Crumb {
  label: string
  /** Absent for the current page. */
  to?: string
}

export function breadcrumbFor(pathname: string): Crumb[] {
  const [first, second] = pathname.split('/').filter(Boolean)
  const section = first ? sections[first] : undefined
  if (!section) return []
  if (!second) return [{ label: section.label }]
  return [{ label: section.label, to: section.to }, { label: section.detail ?? second }]
}
