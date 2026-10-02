import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { Panel, PanelHeader } from '@/components/layout/PageHeader'
import { Clipboard, Fields, Package, Pulse, Receipt, Scale, Workflow } from '@/components/icons'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { StatTile } from '@/components/ui/StatTile'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { caseStatusTone, orderStatusTone, pressureTone, severityTone, stockLevelTone } from '@/components/ui/status-tones'
import { timeSince } from '@/lib/dates'
import { useCurrentUser } from '@/features/auth/auth-store'
import type { UserSummary } from '@/features/auth/types'
import { useCaseQueue } from '@/features/agent-runs/queries'
import { caseStatusLabels, type CaseStatus, type CaseSummary } from '@/features/agent-runs/types'
import { SprayWindowPanel } from '@/features/harvest/SprayWindowPanel'
import { useOutbreakSignal } from '@/features/intelligence/queries'
import { levelLabels, trendLabels } from '@/features/intelligence/types'
import { formatLkr } from '@/features/inventory/format'
import { AwaitingCollection } from '@/features/inventory/DealerPanels'
import { useLowStock, useOrders, useRules, useStockValuation } from '@/features/inventory/queries'
import { orderStatusLabels, type Order } from '@/features/inventory/types'
import { usePlots } from '@/features/registry/queries'
import { DashboardHero } from './DashboardHero'

/**
 * The dashboard (§7): an opening card with the role's photograph and four figures for what is waiting on them, then
 * their work queue beside the one panel that informs it — spray weather for field roles, low stock
 * for a dealer, the leading pathogens for the administrator. Every figure links to where it is
 * worked, and every panel keeps its loading, empty and error states.
 */
export function DashboardPage() {
  const user = useCurrentUser()
  const signal = useOutbreakSignal({ districtId: user?.districtId ?? undefined })

  if (!user) return null

  return (
    <div className="space-y-6 pt-5">
      <DashboardHero user={user} districtName={signal.data?.districtName}>
        <GlanceTiles user={user} />
        <OutbreakGlance user={user} />
      </DashboardHero>

      <div className="grid items-start gap-4 xl:grid-cols-12">
        <div className="xl:col-span-8">
          <WorkQueue user={user} />
        </div>
        <div className="xl:col-span-4">
          <SidePanel user={user} />
        </div>
      </div>
    </div>
  )
}

// ── Stat tiles ──────────────────────────────────────────────────────────────

/** Three role-specific figures; the fourth tile is always disease pressure. */
function GlanceTiles({ user }: { user: UserSummary }) {
  switch (user.role) {
    case 'FieldAgronomist':
      return (
        <>
          <CaseCountTile status="PendingApproval" label="Awaiting your decision" to="/agent-runs" detail="Proposals that passed the safety rules" icon={<Workflow />} />
          <CaseCountTile status="AwaitingManualReview" label="In manual review" to="/cases?status=AwaitingManualReview" detail="Escalated, failed or rejected runs" icon={<Clipboard />} />
          <CaseCountTile status="AgentProcessing" label="Agents working" to="/agent-runs?view=AgentProcessing" detail="Runs in progress right now" icon={<Workflow />} />
        </>
      )
    case 'Farmer':
      return (
        <>
          <CaseCountTile status="PendingApproval" label="Awaiting an agronomist" to="/cases?status=PendingApproval" detail="A person checks every proposal" icon={<Clipboard />} />
          <CaseCountTile status="Prescribed" label="Prescribed" to="/cases?status=Prescribed" detail="Treatments issued to you" icon={<Clipboard />} />
          <PlotCountTile />
        </>
      )
    case 'AgroDealer':
      return (
        <>
          <StockGlance linkToInventory />
          <OrderCountTile status="Confirmed" label="Orders to pack" detail="Stock already set aside" />
          <OrderCountTile status="Packed" label="Awaiting collection" detail="Handed over on the pickup code" />
        </>
      )
    case 'CoopAdministrator':
      return (
        <>
          <StockGlance linkToInventory={false} />
          <CaseCountTile status="AwaitingManualReview" label="In manual review" to="/cases?status=AwaitingManualReview" detail="Cases a person must look at" icon={<Clipboard />} />
          <ActiveRulesTile />
        </>
      )
    default:
      return null
  }
}

/** A tile that links to where its figure is worked; a dash while the figure loads. */
function LinkedTile({ to, ...tile }: { to: string; label: string; value: ReactNode; detail?: ReactNode; icon?: ReactNode }) {
  return (
    <Link to={to} className="block rounded-xl">
      <StatTile interactive {...tile} />
    </Link>
  )
}

function CaseCountTile({ status, label, to, detail, icon }: { status: CaseStatus; label: string; to: string; detail: string; icon: ReactNode }) {
  const cases = useCaseQueue({ status, pageSize: 1 })
  return <LinkedTile to={to} label={label} value={cases.data?.totalCount ?? '—'} detail={detail} icon={icon} />
}

function OrderCountTile({ status, label, detail }: { status: 'Confirmed' | 'Packed'; label: string; detail: string }) {
  const orders = useOrders({ status, pageSize: 1 })
  return <LinkedTile to={`/orders?view=${status}`} label={label} value={orders.data?.totalCount ?? '—'} detail={detail} icon={<Receipt />} />
}

function PlotCountTile() {
  const plots = usePlots({ pageSize: 100 })
  const growing = plots.data?.items.filter((p) => p.activeCycle).length
  return (
    <LinkedTile
      to="/farms"
      label="Plots growing"
      value={growing ?? '—'}
      detail={plots.data ? `of ${plots.data.totalCount} plot${plots.data.totalCount === 1 ? '' : 's'} registered` : 'Your registered plots'}
      icon={<Fields />}
    />
  )
}

function ActiveRulesTile() {
  const rules = useRules({ isActive: true, pageSize: 1 })
  return <LinkedTile to="/rules" label="Active rules" value={rules.data?.totalCount ?? '—'} detail="Product approvals the validator applies" icon={<Scale />} />
}

/** Disease pressure where the user works (or everywhere, for a user with no district). */
function OutbreakGlance({ user }: { user: UserSummary }) {
  const signal = useOutbreakSignal({ districtId: user.districtId ?? undefined })
  const s = signal.data
  if (!s) return <StatTile label="Disease pressure" value="—" detail="Reading the outbreak signal…" icon={<Pulse />} />

  const top = s.topPathogens[0]
  return (
    <Link to="/intelligence" className="block rounded-xl">
      <StatTile
        interactive
        label={`Disease pressure · ${s.districtName ?? 'all districts'}`}
        value={
          <span className="flex flex-wrap items-center gap-2">
            <span>{s.pressureIndex}</span>
            <span className="font-sans text-sm font-normal text-stone-600">/ 100</span>
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
  if (!valuation.data || !lowStock.data) return <StatTile label="Stock value" value="—" detail="Valuing the batches…" icon={<Package />} />

  const tile = (
    <StatTile
      interactive={linkToInventory}
      label="Stock on the shelf"
      value={formatLkr(valuation.data.totalValue)}
      detail={`${lowStock.data.outOfStock} out of stock · ${lowStock.data.low} running low`}
      icon={<Package />}
    />
  )
  return linkToInventory ? (
    <Link to="/inventory" className="block rounded-xl">
      {tile}
    </Link>
  ) : (
    tile
  )
}

// ── The work queue ──────────────────────────────────────────────────────────

function WorkQueue({ user }: { user: UserSummary }) {
  switch (user.role) {
    case 'FieldAgronomist':
      return (
        <div className="space-y-6">
          <CaseQueuePanel
            headingId="queue-heading"
            status="PendingApproval"
            title="Proposals awaiting your decision"
            description="Oldest first: the farmer on the top row has waited longest."
            empty="Nothing waits on you. New proposals appear here once they pass the safety rules."
            linkToRun
            moreTo="/agent-runs"
          />
          <CaseQueuePanel
            headingId="new-heading"
            status="Submitted"
            title="New reports"
            description="Reported from the field, not yet sent to the agents."
            empty="No new report is waiting."
            newestFirst
            moreTo="/cases?status=Submitted"
          />
        </div>
      )
    case 'CoopAdministrator':
      return (
        <div className="space-y-6">
          <CaseQueuePanel
            headingId="queue-heading"
            status="AwaitingManualReview"
            title="Cases in manual review"
            description="Escalated by triage, or back from a failed or rejected run."
            empty="No case needs a person's review."
            moreTo="/cases?status=AwaitingManualReview"
          />
          <CaseQueuePanel
            headingId="latest-heading"
            title="Latest cases, every district"
            description="Newest first, whatever their status."
            empty="No case has been reported yet."
            newestFirst
            moreTo="/cases"
          />
        </div>
      )
    case 'Farmer':
      return (
        <CaseQueuePanel
          headingId="queue-heading"
          title="Your latest cases"
          description="Reported from the phone; follow each one here or in the app."
          empty="No problems reported yet. Report one from the AgriGuard phone app."
          newestFirst
          moreTo="/cases"
        />
      )
    case 'AgroDealer':
      return (
        <div className="space-y-6">
          <OrdersToPack />
          <AwaitingCollection />
        </div>
      )
    default:
      return null
  }
}

function CaseQueuePanel({
  headingId,
  status,
  title,
  description,
  empty,
  linkToRun = false,
  newestFirst = false,
  moreTo,
}: {
  headingId: string
  status?: CaseStatus
  title: string
  description: string
  empty: string
  linkToRun?: boolean
  newestFirst?: boolean
  moreTo: string
}) {
  const cases = useCaseQueue({ status, pageSize: 6, ...(newestFirst ? { sortBy: 'createdAt', desc: true } : {}) })
  const columns: Column<CaseSummary>[] = [
    {
      key: 'case',
      header: 'Case',
      render: (c) => (
        <Link to={linkToRun && c.latestRunId ? `/agent-runs/${c.latestRunId}` : `/cases/${c.id}`} className="font-semibold whitespace-nowrap text-brand-700 hover:underline">
          {c.referenceNo}
        </Link>
      ),
    },
    {
      key: 'who',
      header: 'Farmer and plot',
      render: (c) => (
        <span>
          {c.farmerName} <span className="text-stone-600">· {c.plotCode}</span>
        </span>
      ),
    },
    { key: 'crop', header: 'Crop', secondary: true, render: (c) => `${c.cropName} (${c.stage})` },
    { key: 'severity', header: 'Severity', render: (c) => <StatusBadge label={c.severity} tone={severityTone[c.severity]} /> },
    status
      ? { key: 'waiting', header: 'Waiting', numeric: true, render: (c) => <span className="whitespace-nowrap">{timeSince(c.createdAt)}</span> }
      : { key: 'status', header: 'Status', render: (c) => <StatusBadge label={caseStatusLabels[c.status]} tone={caseStatusTone[c.status]} /> },
  ]

  return (
    <section aria-labelledby={headingId} className="space-y-3">
      <QueueHeader id={headingId} title={title} description={description} to={moreTo} total={cases.data?.totalCount} />
      <AsyncBoundary isPending={cases.isPending} error={cases.error} onRetry={cases.refetch} label="Loading the queue">
        {cases.data?.items.length === 0 ? (
          <EmptyState title="All clear" description={empty} />
        ) : (
          <DataTable caption={title} columns={columns} rows={cases.data?.items ?? []} rowKey={(c) => c.id} />
        )}
      </AsyncBoundary>
    </section>
  )
}

function OrdersToPack() {
  const orders = useOrders({ status: 'Confirmed', pageSize: 6 })
  const columns: Column<Order>[] = [
    { key: 'order', header: 'Order', render: (o) => <span className="font-semibold text-stone-900">{o.orderNo}</span> },
    { key: 'farmer', header: 'Farmer', render: (o) => o.farmerName },
    { key: 'items', header: 'Items', render: (o) => o.lines.map((l) => `${l.packs} × ${l.productName}`).join(', ') },
    { key: 'spray', header: 'Spray date', secondary: true, render: (o) => o.sprayDate ?? '—' },
    { key: 'status', header: 'Status', render: (o) => <StatusBadge label={orderStatusLabels[o.status]} tone={orderStatusTone[o.status]} /> },
  ]
  return (
    <section aria-labelledby="queue-heading" className="space-y-3">
      <QueueHeader id="queue-heading" title="Orders to pack" description="From approved prescriptions, with the stock already set aside." to="/orders" total={orders.data?.totalCount} />
      <AsyncBoundary isPending={orders.isPending} error={orders.error} onRetry={orders.refetch} label="Loading orders">
        {orders.data?.items.length === 0 ? (
          <EmptyState title="Nothing to pack" description="When an agronomist approves a prescription from your shop, its order appears here." />
        ) : (
          <DataTable caption="Orders to pack" columns={columns} rows={orders.data?.items ?? []} rowKey={(o) => o.id} />
        )}
      </AsyncBoundary>
    </section>
  )
}

function QueueHeader({ id, title, description, to, total }: { id: string; title: string; description: string; to: string; total?: number }) {
  return (
    <header className="flex flex-wrap items-end justify-between gap-2">
      <div>
        <h2 id={id} className="font-display text-xl leading-tight font-semibold text-stone-900">
          {title}
        </h2>
        <p className="text-sm text-stone-600">{description}</p>
      </div>
      <Link to={to} className="rounded-sm text-sm font-medium text-brand-700 hover:underline">
        {total !== undefined && total > 6 ? `Open all ${total}` : 'Open the full list'}
      </Link>
    </header>
  )
}

// ── The side panel ──────────────────────────────────────────────────────────

function SidePanel({ user }: { user: UserSummary }) {
  switch (user.role) {
    case 'FieldAgronomist':
      return <AgronomistWeather />
    case 'Farmer':
      return <FarmerWeather />
    case 'AgroDealer':
      return <LowStockPanel />
    case 'CoopAdministrator':
      return <LeadingPathogens user={user} />
    default:
      return null
  }
}

/** The spray weather at the plot of the proposal that has waited longest. */
function AgronomistWeather() {
  const queue = useCaseQueue({ status: 'PendingApproval', pageSize: 6 })
  const first = queue.data?.items[0]
  return <SprayWindowPanel plotId={first?.plotId} context={first ? `for ${first.referenceNo}, the longest waiting` : undefined} />
}

/** The spray weather at the farmer's first plot with a crop growing. */
function FarmerWeather() {
  const plots = usePlots({ pageSize: 100 })
  const growing = plots.data?.items.find((p) => p.activeCycle)
  return <SprayWindowPanel plotId={growing?.id} context={growing?.activeCycle ? `${growing.activeCycle.cropName}, ${growing.farmName}` : undefined} />
}

function LowStockPanel() {
  const lowStock = useLowStock()
  const rows = lowStock.data?.rows.filter((r) => r.state !== 'Sufficient') ?? []
  return (
    <Panel raised aria-labelledby="low-heading">
      <PanelHeader id="low-heading" title="Running low" description="Products with fewer sellable packs than you keep in reserve." />
      <AsyncBoundary isPending={lowStock.isPending} error={lowStock.error} onRetry={lowStock.refetch} label="Checking stock">
        {rows.length === 0 ? (
          <p className="text-sm text-stone-600">Every product has enough sellable packs.</p>
        ) : (
          <ul className="divide-y divide-border-subtle">
            {rows.map((row) => (
              <li key={row.productId + row.dealerId} className="flex items-center justify-between gap-3 py-2 text-sm">
                <span className="min-w-0">
                  <span className="block truncate font-medium text-stone-900">{row.productName}</span>
                  <span className="text-xs text-stone-600 tabular-nums">
                    {row.sellablePacks} sellable pack{row.sellablePacks === 1 ? '' : 's'}
                  </span>
                </span>
                <StatusBadge label={row.state === 'OutOfStock' ? 'Out of stock' : 'Low'} tone={stockLevelTone[row.state]} />
              </li>
            ))}
          </ul>
        )}
        <Link to="/inventory" className="mt-3 inline-block rounded-sm text-sm font-medium text-brand-700 hover:underline">
          Open the inventory
        </Link>
      </AsyncBoundary>
    </Panel>
  )
}

function LeadingPathogens({ user }: { user: UserSummary }) {
  const signal = useOutbreakSignal({ districtId: user.districtId ?? undefined })
  return (
    <Panel raised aria-labelledby="pathogens-heading">
      <PanelHeader id="pathogens-heading" title="Leading pathogens" description="Share of confirmed cases in the outbreak window." />
      <AsyncBoundary isPending={signal.isPending} error={signal.error} onRetry={signal.refetch} label="Reading the outbreak signal">
        {signal.data && signal.data.topPathogens.length === 0 ? (
          <p className="text-sm text-stone-600">No confirmed cases in the window.</p>
        ) : (
          <ol className="space-y-2.5">
            {signal.data?.topPathogens.map((p, index) => (
              <li key={p.code} className="text-sm">
                <div className="flex items-baseline justify-between gap-2">
                  <span className="font-medium text-stone-900">
                    <span className="mr-1.5 text-stone-600 tabular-nums">{index + 1}.</span>
                    {p.name}
                  </span>
                  <span className="text-xs text-stone-600 tabular-nums">
                    {p.confirmedCases} confirmed, {p.sharePercent}%
                  </span>
                </div>
                <div aria-hidden="true" className="mt-1 h-1.5 rounded-full bg-surface-inset">
                  <div className="h-1.5 rounded-full bg-earth-400" style={{ width: `${Math.min(100, p.sharePercent)}%` }} />
                </div>
              </li>
            ))}
          </ol>
        )}
        <Link to="/intelligence" className="mt-3 inline-block rounded-sm text-sm font-medium text-brand-700 hover:underline">
          Open disease intelligence
        </Link>
      </AsyncBoundary>
    </Panel>
  )
}
