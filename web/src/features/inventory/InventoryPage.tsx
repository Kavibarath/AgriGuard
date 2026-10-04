import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { Alert } from '@/components/ui/alert'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { Button } from '@/components/ui/button'
import { DataTable, Pagination, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field } from '@/components/ui/field'
import { SelectField } from '@/components/ui/select'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { expiryTone } from '@/components/ui/status-tones'
import { BatchFormModal } from './BatchFormModal'
import { formatLkr, formatQuantity, isoToday } from './format'
import { HoldsPanel } from './HoldsPanel'
import { HoldStockModal } from './HoldStockModal'
import { useBatches } from './queries'
import { expiryLabels, type InventoryBatch } from './types'

/** Matches BatchExpiry.WarningDays on the server. */
const WARNING_DAYS = 30

const views = [
  { value: 'stock', label: 'Stock on the shelf' },
  { value: 'expiring', label: `Expiring within ${WARNING_DAYS} days` },
  { value: 'all', label: 'Everything, incl. sold out' },
] as const

function expiryNote(batch: InventoryBatch): string {
  if (batch.daysToExpiry < 0) return `${-batch.daysToExpiry} day(s) ago`
  if (batch.daysToExpiry === 0) return 'today'
  return `in ${batch.daysToExpiry} day(s)`
}

/**
 * The dealer's shelf (§7 /inventory): batches soonest-expiry first with warnings, deliveries and
 * recounts, and the stock on hold. Filters live in the URL, like every other list.
 */
export function InventoryPage() {
  const [params, setParams] = useSearchParams()
  const [editing, setEditing] = useState<InventoryBatch | 'new' | null>(null)
  const [holding, setHolding] = useState(false)

  const view = views.find((v) => v.value === params.get('view')) ?? views[0]
  const query = {
    page: Number(params.get('page') ?? 1),
    pageSize: 20,
    search: params.get('search') ?? undefined,
    sortBy: params.get('sortBy') ?? undefined,
    desc: params.get('desc') === 'true',
    expiringBefore: view.value === 'expiring' ? isoToday(WARNING_DAYS) : undefined,
    includeEmpty: view.value === 'all' ? true : undefined,
  }
  const batches = useBatches(query)
  // One row is enough: the total says how many batches need attention across every page.
  const expiring = useBatches({ expiringBefore: isoToday(WARNING_DAYS), pageSize: 1 })
  const expiringCount = expiring.data?.totalCount ?? 0

  const updateParams = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(patch)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    if (!('page' in patch)) next.delete('page')
    setParams(next, { replace: true })
  }

  const columns: Column<InventoryBatch>[] = [
    {
      key: 'product',
      header: 'Product',
      sortable: true,
      render: (b) => (
        <div>
          <p className="font-medium text-stone-900">{b.productName}</p>
          <p className="text-xs text-stone-500">Batch {b.batchNo}</p>
        </div>
      ),
    },
    {
      key: 'expiryDate',
      header: 'Expires',
      sortable: true,
      render: (b) => (
        <div className="space-y-0.5">
          <p className="tabular-nums">{b.expiryDate}</p>
          <p className="flex items-center gap-1.5 text-xs text-stone-500">
            <StatusBadge label={expiryLabels[b.expiryState]} tone={expiryTone[b.expiryState]} />
            {b.expiryState !== 'InDate' && expiryNote(b)}
          </p>
        </div>
      ),
    },
    { key: 'onHand', header: 'On hand', sortable: true, numeric: true, render: (b) => formatQuantity(b.quantityOnHand, b.unit) },
    { key: 'held', header: 'Held', numeric: true, secondary: true, render: (b) => (b.quantityReserved > 0 ? formatQuantity(b.quantityReserved, b.unit) : '—') },
    {
      key: 'available',
      header: 'Available',
      sortable: true,
      numeric: true,
      render: (b) => <span className="font-medium">{formatQuantity(b.quantityAvailable, b.unit)}</span>,
    },
    { key: 'price', header: 'Pack price', numeric: true, secondary: true, render: (b) => formatLkr(b.unitPrice) },
    {
      key: 'actions',
      header: '',
      render: (b) => (
        <div className="flex justify-end">
          <Button variant="ghost" className="h-8 px-2" onClick={() => setEditing(b)} aria-label={`Correct batch ${b.batchNo}`}>
            Correct
          </Button>
        </div>
      ),
    },
  ]

  const shopName = batches.data?.items[0]?.shopName

  return (
    <main className="mx-auto max-w-6xl space-y-8 p-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div className="space-y-1">
          <Link to="/dashboard" className="text-sm text-stone-500 hover:text-stone-800">
            ← Dashboard
          </Link>
          <h1 className="text-2xl font-semibold text-stone-900">Inventory</h1>
          <p className="text-sm text-stone-600">{shopName ? `${shopName} — ` : ''}batches on your shelf, soonest expiry first.</p>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => setHolding(true)}>
            Hold stock
          </Button>
          <Button onClick={() => setEditing('new')}>Receive delivery</Button>
        </div>
      </header>

      {expiringCount > 0 && view.value !== 'expiring' && (
        <Alert tone="warning" title={`${expiringCount} batch(es) expire within ${WARNING_DAYS} days or have expired`}>
          <p>Sell these first or return them to the supplier. Expired stock is never offered to a prescription.</p>
          <Button variant="secondary" className="mt-2 h-8" onClick={() => updateParams({ view: 'expiring' })}>
            Show them
          </Button>
        </Alert>
      )}

      <section aria-labelledby="batches-heading" className="space-y-3">
        <h2 id="batches-heading" className="sr-only">
          Batches
        </h2>
        <div className="grid gap-3 sm:grid-cols-[16rem_18rem]">
          <SelectField label="Show" value={view.value} onChange={(event) => updateParams({ view: event.target.value })}>
            {views.map((v) => (
              <option key={v.value} value={v.value}>
                {v.label}
              </option>
            ))}
          </SelectField>
          <Field
            label="Search"
            type="search"
            placeholder="Product or batch number"
            defaultValue={query.search ?? ''}
            onChange={(event) => updateParams({ search: event.target.value || undefined })}
          />
        </div>

        <AsyncBoundary isPending={batches.isPending} error={batches.error} onRetry={batches.refetch} label="Loading stock">
          {batches.data?.items.length === 0 ? (
            <EmptyState
              title={query.search ? 'No batches match that search' : view.value === 'expiring' ? 'Nothing is close to expiry' : 'Your shelf is empty'}
              description={
                query.search || view.value === 'expiring' ? 'Try another view or search.' : 'Add a delivery to start selling through AgriGuard.'
              }
              action={!query.search && view.value === 'stock' ? <Button onClick={() => setEditing('new')}>Receive delivery</Button> : undefined}
            />
          ) : (
            <div className="space-y-3">
              <DataTable
                caption="Batches on the shelf"
                columns={columns}
                rows={batches.data?.items ?? []}
                rowKey={(b) => b.id}
                sort={{ sortBy: query.sortBy, desc: query.desc }}
                onSortChange={(next) => updateParams({ sortBy: next.sortBy, desc: next.desc ? 'true' : undefined })}
              />
              <Pagination
                page={batches.data?.page ?? 1}
                totalPages={batches.data?.totalPages ?? 1}
                totalCount={batches.data?.totalCount ?? 0}
                onPageChange={(page) => updateParams({ page: String(page) })}
              />
            </div>
          )}
        </AsyncBoundary>
      </section>

      <HoldsPanel />

      <BatchFormModal open={editing !== null} batch={editing === 'new' ? undefined : (editing ?? undefined)} onClose={() => setEditing(null)} />
      <HoldStockModal open={holding} onClose={() => setHolding(false)} />
    </main>
  )
}
