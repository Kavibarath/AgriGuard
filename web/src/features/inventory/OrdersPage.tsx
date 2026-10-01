import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { FilterBar } from '@/components/layout/FilterBar'
import { PageHeader } from '@/components/layout/PageHeader'
import { Receipt } from '@/components/icons'
import { Alert } from '@/components/ui/alert'
import { StatTile } from '@/components/ui/StatTile'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { Button } from '@/components/ui/button'
import { DataTable, Pagination, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field } from '@/components/ui/field'
import { SelectField } from '@/components/ui/select'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { orderStatusTone } from '@/components/ui/status-tones'
import { userMessage } from '@/lib/api'
import { AwaitingCollection } from './DealerPanels'
import { formatDateTime, formatLkr } from './format'
import { HandOverModal } from './HandOverModal'
import { useFulfilOrder, useOrders } from './queries'
import { cn } from '@/lib/utils'
import { fulfilActionLabels, orderStatusLabels, type Order, type OrderStatus } from './types'

/** "To pack" is the default: it is the dealer's actual work. */
const views: { value: string; label: string; status?: OrderStatus }[] = [
  { value: 'Confirmed', label: 'To pack', status: 'Confirmed' },
  { value: 'Packed', label: 'Awaiting collection', status: 'Packed' },
  { value: 'Collected', label: 'Collected', status: 'Collected' },
  { value: 'all', label: 'All orders' },
]

/**
 * The dealer's orders (§7 /orders). Each approved prescription arrives here Confirmed, with its
 * stock already drawn; the dealer packs it, then hands it over. One button per row performs the
 * next step, and the request names the target status, so a double click cannot skip a step.
 */
export function OrdersPage() {
  const [params, setParams] = useSearchParams()
  const fulfil = useFulfilOrder()
  const [acting, setActing] = useState<string | null>(null)
  const [handingOver, setHandingOver] = useState<Order | null>(null)

  const view = views.find((v) => v.value === params.get('view')) ?? views[0]
  const query = {
    page: Number(params.get('page') ?? 1),
    pageSize: 20,
    status: view.status,
    search: params.get('search') ?? undefined,
    sortBy: params.get('sortBy') ?? undefined,
    desc: params.get('desc') === 'true',
  }
  const orders = useOrders(query)

  const updateParams = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(patch)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    if (!('page' in patch)) next.delete('page')
    setParams(next, { replace: true })
  }

  const advance = async (order: Order) => {
    if (!order.nextStatus) return
    fulfil.reset()
    // Handing over needs the farmer's pickup code, asked for in a dialog.
    if (order.nextStatus === 'Collected') {
      setHandingOver(order)
      return
    }
    setActing(order.id)
    try {
      await fulfil.mutateAsync({ id: order.id, status: order.nextStatus })
    } catch {
      // Shown above the table.
    } finally {
      setActing(null)
    }
  }

  const columns: Column<Order>[] = [
    {
      key: 'orderNo',
      header: 'Order',
      sortable: true,
      render: (o) => (
        <div>
          <p className="font-medium whitespace-nowrap text-stone-900">{o.orderNo}</p>
          {o.prescriptionNo && <p className="text-xs whitespace-nowrap text-stone-600">Prescription {o.prescriptionNo}</p>}
        </div>
      ),
    },
    {
      key: 'farmer',
      header: 'Farmer',
      sortable: true,
      render: (o) => (
        <div>
          <p>{o.farmerName}</p>
          {o.farmerPhone && <p className="text-xs text-stone-600">{o.farmerPhone}</p>}
        </div>
      ),
    },
    {
      key: 'items',
      header: 'Items',
      render: (o) => (
        <ul className="space-y-0.5">
          {o.lines.map((l) => (
            <li key={l.productId}>
              {l.packs} × {l.productName}
            </li>
          ))}
        </ul>
      ),
    },
    { key: 'sprayDate', header: 'Spray date', secondary: true, render: (o) => <span className="whitespace-nowrap">{o.sprayDate ?? '—'}</span> },
    { key: 'total', header: 'Total', sortable: true, numeric: true, secondary: true, render: (o) => formatLkr(o.totalAmount) },
    {
      key: 'status',
      header: 'Status',
      sortable: true,
      render: (o) => (
        <div className="space-y-0.5">
          <StatusBadge label={orderStatusLabels[o.status]} tone={orderStatusTone[o.status]} />
          {o.collectedAt && <p className="text-xs text-stone-600">{formatDateTime(o.collectedAt)}</p>}
        </div>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (o) =>
        o.nextStatus && fulfilActionLabels[o.nextStatus] ? (
          <div className="flex justify-end">
            <Button
              variant="secondary"
              className="h-8"
              loading={acting === o.id}
              onClick={() => advance(o)}
              aria-label={`${fulfilActionLabels[o.nextStatus]}: ${o.orderNo}`}
            >
              {fulfilActionLabels[o.nextStatus]}
            </Button>
          </div>
        ) : null,
    },
  ]

  return (
    <div className="space-y-4 pb-4">
      <PageHeader
        title="Orders"
        description="Orders from approved prescriptions. The stock is already set aside: pack each order, then hand it over when the farmer shows the pickup code on their phone."
      />

      <div className="grid items-start gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {tiles.map((tile) => (
          <OrderViewTile key={tile.status} {...tile} current={view.value === tile.status} />
        ))}
        <AwaitingCollection />
      </div>

      <div className="space-y-3">
        <FilterBar className="border-earth-100 bg-earth-50/70 sm:grid-cols-[14rem_18rem]">
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
            placeholder="Order, prescription or farmer"
            defaultValue={query.search ?? ''}
            onChange={(event) => updateParams({ search: event.target.value || undefined })}
          />
        </FilterBar>

        {fulfil.error && <Alert tone="error">{userMessage(fulfil.error)}</Alert>}

        <AsyncBoundary isPending={orders.isPending} error={orders.error} onRetry={orders.refetch} label="Loading orders">
          {orders.data?.items.length === 0 ? (
            <EmptyState
              title={query.search ? 'No orders match that search' : `Nothing under “${view.label}”`}
              description={
                view.status === 'Confirmed' && !query.search
                  ? 'When an agronomist approves a prescription that sources from your shop, the order appears here.'
                  : 'Try another view or search.'
              }
            />
          ) : (
            <div className="space-y-3">
              <DataTable
                tone="earth"
                caption="Input orders"
                columns={columns}
                rows={orders.data?.items ?? []}
                rowKey={(o) => o.id}
                sort={{ sortBy: query.sortBy, desc: query.desc }}
                onSortChange={(next) => updateParams({ sortBy: next.sortBy, desc: next.desc ? 'true' : undefined })}
              />
              <Pagination
                page={orders.data?.page ?? 1}
                totalPages={orders.data?.totalPages ?? 1}
                totalCount={orders.data?.totalCount ?? 0}
                onPageChange={(page) => updateParams({ page: String(page) })}
              />
            </div>
          )}
        </AsyncBoundary>
      </div>
      <HandOverModal order={handingOver} onClose={() => setHandingOver(null)} />
    </div>
  )
}

/** The three stages of an order, counted; each tile opens its view. */
const tiles: { status: OrderStatus; label: string; detail: string }[] = [
  { status: 'Confirmed', label: 'To pack', detail: 'Stock set aside, not yet packed' },
  { status: 'Packed', label: 'Awaiting collection', detail: 'Packed; handed over on the pickup code' },
  { status: 'Collected', label: 'Collected', detail: 'Handed over to the farmer' },
]

function OrderViewTile({ status, label, detail, current }: (typeof tiles)[number] & { current: boolean }) {
  const count = useOrders({ status, pageSize: 1 })
  return (
    <Link to={`?view=${status}`} replace aria-current={current ? 'true' : undefined} className="block rounded-xl">
      <StatTile
        interactive
        label={label}
        value={count.data?.totalCount ?? '—'}
        detail={detail}
        icon={<Receipt className="text-earth-600" />}
        className={cn(current && 'border-earth-400 ring-1 ring-earth-400')}
      />
    </Link>
  )
}
