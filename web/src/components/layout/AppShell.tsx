import { useMutation } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import { Bell, ChevronRight, Close, Menu, Search, SignOut } from '@/components/icons'
import { signOut } from '@/features/auth/api'
import { useCurrentUser } from '@/features/auth/auth-store'
import { can } from '@/features/auth/policies'
import { roleLabels, type UserSummary } from '@/features/auth/types'
import { useCaseQueue } from '@/features/agent-runs/queries'
import { useOrders } from '@/features/inventory/queries'
import { cn } from '@/lib/utils'
import { BrandMark } from './BrandMark'
import { breadcrumbFor, navigation } from './navigation'

/**
 * The console's frame: a fixed 240px canopy-green sidebar with the role's navigation, a top bar
 * with the breadcrumb, search and the work-waiting bell, and the page in a 1440px column. Below
 * the lg breakpoint the sidebar becomes a drawer behind the menu button.
 */
export function AppShell() {
  const user = useCurrentUser()
  const [drawerOpen, setDrawerOpen] = useState(false)

  if (!user) return null

  return (
    <div className="min-h-screen bg-surface-page">
      <a
        href="#main"
        className="sr-only z-50 rounded-md bg-surface-card px-3 py-2 font-medium text-brand-700 shadow-lifted focus:not-sr-only focus:fixed focus:top-2 focus:left-2"
      >
        Skip to content
      </a>

      <Sidebar user={user} className="fixed inset-y-0 left-0 z-30 hidden w-60 lg:flex" />

      {drawerOpen && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <div className="absolute inset-0 bg-brand-900/45" aria-hidden="true" onClick={() => setDrawerOpen(false)} />
          <Sidebar user={user} className="relative flex h-full w-72 shadow-overlay" onClose={() => setDrawerOpen(false)} />
        </div>
      )}

      <div className="lg:pl-60">
        <TopBar user={user} onMenu={() => setDrawerOpen(true)} />
        <main id="main" tabIndex={-1} className="mx-auto w-full max-w-[1440px] px-4 pb-12 sm:px-6 focus:outline-none">
          <Outlet />
        </main>
      </div>
    </div>
  )
}

function Sidebar({ user, className, onClose }: { user: UserSummary; className?: string; onClose?: () => void }) {
  const logout = useMutation({ mutationFn: signOut })
  const groups = navigation
    .map((group) => ({ ...group, items: group.items.filter((item) => !item.policy || can(user.role, item.policy)) }))
    .filter((group) => group.items.length > 0)

  return (
    <aside
      className={cn(
        'flex-col bg-brand-800 text-brand-50',
        // The global brand-600 focus outline would vanish on this ground; use a light one here.
        '[&_:focus-visible]:outline-brand-200',
        className,
      )}
    >
      <div className="flex h-14 shrink-0 items-center justify-between gap-2 border-b border-white/10 px-4">
        <Link to="/dashboard" className="flex items-center gap-2.5 rounded-md">
          <BrandMark />
          <span className="font-display text-lg font-semibold tracking-tight text-white">AgriGuard</span>
        </Link>
        {onClose && (
          <button type="button" onClick={onClose} aria-label="Close menu" className="rounded-md p-1.5 text-brand-100 hover:bg-white/10">
            <Close />
          </button>
        )}
      </div>

      <nav aria-label="Main" className="flex-1 space-y-5 overflow-y-auto px-3 py-4">
        {groups.map((group, index) => (
          <div key={group.label ?? index}>
            {group.label && <p className="mb-1 px-2.5 text-xs font-medium text-brand-200">{group.label}</p>}
            <ul className="space-y-0.5">
              {group.items.map((item) => (
                <li key={item.to}>
                  <NavLink
                    to={item.to}
                    // In the drawer, following a link also closes it, so the page is not left behind the menu.
                    onClick={onClose}
                    className={({ isActive }) =>
                      cn(
                        'relative flex h-9 items-center gap-2.5 rounded-md px-2.5 text-sm font-medium',
                        isActive ? 'bg-white/12 text-white' : 'text-brand-100 hover:bg-white/6 hover:text-white',
                      )
                    }
                  >
                    {({ isActive }) => (
                      <>
                        {isActive && <span aria-hidden="true" className="absolute top-1.5 bottom-1.5 left-0 w-[3px] rounded-full bg-brand-300" />}
                        <span className={isActive ? 'text-brand-200' : 'text-brand-300'}>{item.icon}</span>
                        {item.label}
                      </>
                    )}
                  </NavLink>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </nav>

      <div className="flex items-center gap-2.5 border-t border-white/10 px-4 py-3">
        <span
          aria-hidden="true"
          className="flex size-9 shrink-0 items-center justify-center rounded-full bg-brand-700 text-sm font-semibold text-white ring-1 ring-white/15"
        >
          {initials(user.fullName)}
        </span>
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium text-white">{user.fullName}</p>
          <p className="truncate text-xs text-brand-200">{roleLabels[user.role]}</p>
        </div>
        <button
          type="button"
          onClick={() => logout.mutate()}
          disabled={logout.isPending}
          aria-label="Sign out"
          title="Sign out"
          className="rounded-md p-2 text-brand-100 hover:bg-white/10 hover:text-white"
        >
          <SignOut />
        </button>
      </div>
    </aside>
  )
}

function TopBar({ user, onMenu }: { user: UserSummary; onMenu: () => void }) {
  const location = useLocation()
  const crumbs = breadcrumbFor(location.pathname)

  return (
    <header className="sticky top-0 z-20 border-b border-border-subtle bg-surface-page/95 backdrop-blur-[2px]">
      <div className="mx-auto flex h-14 max-w-[1440px] items-center gap-3 px-4 sm:px-6">
        <button type="button" onClick={onMenu} aria-label="Open menu" className="rounded-md p-1.5 text-stone-700 hover:bg-surface-inset lg:hidden">
          <Menu />
        </button>

        <nav aria-label="Breadcrumb" className="min-w-0 flex-1">
          <ol className="flex items-center gap-1 text-sm">
            {crumbs.map((crumb, index) => (
              <li key={crumb.label + index} className="flex min-w-0 items-center gap-1">
                {index > 0 && <ChevronRight size={16} className="shrink-0 text-stone-400" />}
                {crumb.to ? (
                  <Link to={crumb.to} className="truncate rounded-sm text-stone-600 hover:text-stone-900">
                    {crumb.label}
                  </Link>
                ) : (
                  <span aria-current="page" className="truncate font-medium text-stone-900">
                    {crumb.label}
                  </span>
                )}
              </li>
            ))}
          </ol>
        </nav>

        <GlobalSearch user={user} />
        <WorkBell user={user} />
      </div>
    </header>
  )
}

/** Search jumps to the list the role works from: cases for field roles, orders for a dealer. */
function GlobalSearch({ user }: { user: UserSummary }) {
  const navigate = useNavigate()
  const [value, setValue] = useState('')
  const target = can(user.role, 'OwnsFarm')
    ? { path: '/cases', label: 'Search cases', placeholder: 'Case, plot or farmer' }
    : can(user.role, 'ManagesInventory')
      ? { path: '/orders', label: 'Search orders', placeholder: 'Order, prescription or farmer' }
      : null
  if (!target) return null

  const submit = (event: FormEvent) => {
    event.preventDefault()
    const term = value.trim()
    navigate(term ? `${target.path}?search=${encodeURIComponent(term)}` : target.path)
  }

  return (
    <form role="search" onSubmit={submit} className="relative hidden w-72 md:block">
      <label htmlFor="global-search" className="sr-only">
        {target.label}
      </label>
      <Search size={16} className="pointer-events-none absolute top-1/2 left-2.5 -translate-y-1/2 text-stone-500" />
      <input
        id="global-search"
        type="search"
        value={value}
        onChange={(event) => setValue(event.target.value)}
        placeholder={target.placeholder}
        className="h-9 w-full rounded-md border border-border-strong bg-surface-card pr-3 pl-8 text-sm placeholder:text-stone-500 focus-visible:border-brand-600"
      />
    </form>
  )
}

/**
 * The bell counts the one thing waiting on this role, from the same list endpoints the queue
 * pages use: proposals to decide, orders to pack, cases in manual review, or the farmer's own
 * cases awaiting an agronomist. It is a link to that queue, labelled with the count in words.
 */
function WorkBell({ user }: { user: UserSummary }) {
  switch (user.role) {
    case 'FieldAgronomist':
      return <CaseBell status="PendingApproval" to="/agent-runs" noun={['proposal awaiting your decision', 'proposals awaiting your decision']} />
    case 'CoopAdministrator':
      return <CaseBell status="AwaitingManualReview" to="/cases?status=AwaitingManualReview" noun={['case in manual review', 'cases in manual review']} />
    case 'Farmer':
      return <CaseBell status="PendingApproval" to="/cases?status=PendingApproval" noun={['case awaiting an agronomist', 'cases awaiting an agronomist']} />
    case 'AgroDealer':
      return <OrderBell />
    default:
      return null
  }
}

function CaseBell({ status, to, noun }: { status: 'PendingApproval' | 'AwaitingManualReview'; to: string; noun: [string, string] }) {
  const queue = useCaseQueue({ status, pageSize: 1 })
  return <BellLink to={to} count={queue.data?.totalCount} noun={noun} />
}

function OrderBell() {
  const orders = useOrders({ status: 'Confirmed', pageSize: 1 })
  return <BellLink to="/orders" count={orders.data?.totalCount} noun={['order to pack', 'orders to pack']} />
}

function BellLink({ to, count, noun }: { to: string; count: number | undefined; noun: [string, string] }) {
  const label = count === undefined ? 'Waiting work' : `${count === 0 ? 'No' : count} ${count === 1 ? noun[0] : noun[1]}`
  return (
    <Link to={to} aria-label={label} title={label} className="relative rounded-md p-2 text-stone-700 hover:bg-surface-inset hover:text-stone-900">
      <Bell />
      {count !== undefined && count > 0 && (
        <span
          aria-hidden="true"
          className="absolute -top-0.5 -right-0.5 flex h-4.5 min-w-4.5 items-center justify-center rounded-full bg-warning px-1 text-xs leading-none font-semibold text-white ring-2 ring-surface-page"
        >
          {count > 99 ? '99+' : count}
        </span>
      )}
    </Link>
  )
}

function initials(name: string): string {
  const words = name.replace(/^(Dr|Mr|Mrs|Ms)\.?\s+/i, '').split(/\s+/).filter(Boolean)
  return (words[0]?.[0] ?? '') + (words.length > 1 ? words[words.length - 1][0] : '')
}
