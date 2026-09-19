import { useMutation } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { signOut } from '@/features/auth/api'
import { useCurrentUser } from '@/features/auth/auth-store'
import { can, type Policy } from '@/features/auth/policies'
import { roleLabels } from '@/features/auth/types'

interface NavCard {
  policy: Policy
  title: string
  description: string
  to: string
}

// Role-driven navigation: each card is visible only to roles its policy admits.
const cards: NavCard[] = [
  { policy: 'CanApprovePrescriptions', title: 'Agent runs', description: 'Review proposals, approve or request revision.', to: '/agent-runs' },
  { policy: 'OwnsFarm', title: 'Farms & plots', description: 'Registry, crop cycles and safety profiles.', to: '/farms' },
  { policy: 'ManagesInventory', title: 'Inventory', description: 'Stock, batches, expiry and orders.', to: '/inventory' },
  { policy: 'AdministersRules', title: 'Regulatory rules', description: 'Product approvals, PHI and dose limits.', to: '/rules' },
]

export function DashboardPage() {
  const user = useCurrentUser()
  const logout = useMutation({ mutationFn: signOut })

  if (!user) return null

  const visible = cards.filter((c) => can(user.role, c.policy))

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

      <section aria-labelledby="nav-heading" className="space-y-3">
        <h2 id="nav-heading" className="text-sm font-medium uppercase tracking-wide text-stone-500">
          Your areas
        </h2>
        <ul className="grid gap-4 sm:grid-cols-2">
          {visible.map((card) => (
            <li key={card.policy}>
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
