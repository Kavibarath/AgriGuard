import { useMutation } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { StatTile } from '@/components/ui/StatTile'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { pressureTone } from '@/components/ui/status-tones'
import { signOut } from '@/features/auth/api'
import { useCurrentUser } from '@/features/auth/auth-store'
import { can, type Policy } from '@/features/auth/policies'
import { roleLabels, type UserSummary } from '@/features/auth/types'
import { useOutbreakSignal } from '@/features/intelligence/queries'
import { levelLabels, trendLabels } from '@/features/intelligence/types'
import { formatLkr } from '@/features/inventory/format'
import { useLowStock, useStockValuation } from '@/features/inventory/queries'

interface NavCard {
  /** Omitted: every signed-in role. */
  policy?: Policy
  title: string
  description: string
  to: string
}

// Role-driven navigation: each card is visible only to roles its policy admits.
const cards: NavCard[] = [
  { policy: 'CanApprovePrescriptions', title: 'Agent runs', description: 'Review proposals, approve or request revision.', to: '/agent-runs' },
  { policy: 'OwnsFarm', title: 'Crop cases', description: 'Reported problems, on a map, with what the agents did.', to: '/cases' },
  { policy: 'OwnsFarm', title: 'Farms & plots', description: 'Registry, crop cycles and safety profiles.', to: '/farms' },
  { policy: 'OwnsFarm', title: 'Harvests', description: 'Coming harvests and how past forecasts compared.', to: '/harvest' },
  { policy: 'OwnsFarm', title: 'Collection planner', description: 'Slots per centre and day, booked and remaining.', to: '/collection-planner' },
  { title: 'Disease intelligence', description: 'Outbreak pressure by district, crop and pathogen.', to: '/intelligence' },
  { policy: 'ManagesInventory', title: 'Inventory', description: 'Stock, batches, expiry warnings and holds.', to: '/inventory' },
  { policy: 'ManagesInventory', title: 'Orders', description: 'Pack approved prescriptions and hand them over.', to: '/orders' },
  { policy: 'AdministersRules', title: 'Regulatory rules', description: 'Product approvals, PHI and dose limits.', to: '/rules' },
]

/** Disease pressure where the user works (or everywhere, for a user with no district). */
function OutbreakGlance({ user }: { user: UserSummary }) {
  const signal = useOutbreakSignal({ districtId: user.districtId ?? undefined })
  const s = signal.data
  if (!s) return null

  const top = s.topPathogens[0]
  return (
    <Link to="/intelligence" className="block rounded-lg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500">
      <StatTile
        label={`Disease pressure · ${s.districtName ?? 'all districts'}`}
        value={
          <span className="flex flex-wrap items-center gap-2">
            <span>{s.pressureIndex}</span>
            <span className="text-sm font-normal text-stone-500">/ 100</span>
            <StatusBadge label={levelLabels[s.level]} tone={pressureTone[s.level]} />
          </span>
        }
        detail={top ? `${top.name} leads · ${trendLabels[s.trend]}` : `No confirmed outbreak in ${s.windowDays} days`}
      />
    </Link>
  )
}

/** What the shelf is worth and what has run out: the dealer's own shop, or every shop for the administrator. */
function StockGlance({ linkToInventory }: { linkToInventory: boolean }) {
  const valuation = useStockValuation()
  const lowStock = useLowStock()
  if (!valuation.data || !lowStock.data) return null

  const tile = (
    <StatTile
      label="Stock on the shelf"
      value={formatLkr(valuation.data.totalValue)}
      detail={`${lowStock.data.outOfStock} out of stock · ${lowStock.data.low} running low`}
    />
  )
  return linkToInventory ? (
    <Link to="/inventory" className="block rounded-lg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500">
      {tile}
    </Link>
  ) : (
    tile
  )
}

export function DashboardPage() {
  const user = useCurrentUser()
  const logout = useMutation({ mutationFn: signOut })

  if (!user) return null

  const visible = cards.filter((c) => !c.policy || can(user.role, c.policy))

  return (
    <main className="mx-auto max-w-4xl space-y-8 p-6">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold text-brand-700">AgriGuard</h1>
          <p className="text-sm text-stone-600">
            Signed in as <span className="font-medium text-stone-900">{user.fullName}</span> · {roleLabels[user.role]}
          </p>
        </div>
        <Button variant="secondary" loading={logout.isPending} onClick={() => logout.mutate()}>
          Sign out
        </Button>
      </header>

      <section aria-labelledby="glance-heading" className="space-y-3">
        <h2 id="glance-heading" className="text-sm font-medium uppercase tracking-wide text-stone-500">
          At a glance
        </h2>
        <div className="grid gap-4 sm:grid-cols-2">
          <OutbreakGlance user={user} />
          {can(user.role, 'ViewsStockReports') && <StockGlance linkToInventory={can(user.role, 'ManagesInventory')} />}
        </div>
      </section>

      <section aria-labelledby="nav-heading" className="space-y-3">
        <h2 id="nav-heading" className="text-sm font-medium uppercase tracking-wide text-stone-500">
          Your areas
        </h2>
        <ul className="grid gap-4 sm:grid-cols-2">
          {visible.map((card) => (
            <li key={card.to}>
              <Link
                to={card.to}
                className="block h-full rounded-lg border border-stone-200 bg-white p-4 shadow-sm transition hover:border-brand-500 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-brand-500"
              >
                <h3 className="font-medium text-stone-900">{card.title}</h3>
                <p className="mt-1 text-sm text-stone-600">{card.description}</p>
              </Link>
            </li>
          ))}
        </ul>
      </section>
    </main>
  )
}
