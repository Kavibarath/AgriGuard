import { Panel, PanelHeader } from '@/components/layout/PageHeader'
import { AlertTriangle, Clock, Package, Receipt, StopOctagon } from '@/components/icons'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { StatTile } from '@/components/ui/StatTile'
import { cn } from '@/lib/utils'
import { formatLkr, isoToday } from './format'
import { useBatches, useLowStock, useOrders, useReservations, useStockValuation } from './queries'

/** The shelf in four figures: its value, what is expiring, what is short, and what is on hold. */
export function ShelfTiles({ expiringCount }: { expiringCount: number | undefined }) {
  const valuation = useStockValuation()
  const lowStock = useLowStock()
  const holds = useReservations({ status: 'Held', pageSize: 1 })
  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <StatTile
        label="Stock value"
        value={valuation.data ? formatLkr(valuation.data.totalValue) : '—'}
        detail={valuation.data ? `${formatLkr(valuation.data.expiredValue)} of it expired` : 'Valuing the batches…'}
        icon={<Package className="text-earth-600" />}
      />
      <StatTile
        label="Expiring or expired"
        value={expiringCount ?? '—'}
        detail="Within the 30-day warning"
        icon={<AlertTriangle className="text-warning" />}
      />
      <StatTile
        label="Out of stock or low"
        value={lowStock.data ? lowStock.data.outOfStock + lowStock.data.low : '—'}
        detail={lowStock.data ? `${lowStock.data.outOfStock} out, ${lowStock.data.low} low` : 'Counting sellable packs…'}
        icon={<StopOctagon className="text-danger" />}
      />
      <StatTile label="Held" value={holds.data?.totalCount ?? '—'} detail="Off sale until sold or released" icon={<Clock className="text-earth-600" />} />
    </div>
  )
}

/**
 * The batches nearest their expiry, each with a bar that fills as the date approaches — the
 * whole 30-day warning at a glance. Expired ones are full, struck in red and say how long ago.
 */
export function ExpiryWatch({ warningDays }: { warningDays: number }) {
  const batches = useBatches({ expiringBefore: isoToday(warningDays), pageSize: 8, sortBy: 'expiryDate' })
  return (
    <Panel raised aria-labelledby="expiry-heading" className="border-earth-200">
      <PanelHeader id="expiry-heading" title="Expiry watch" description={`Batches expiring within ${warningDays} days, soonest first.`} />
      <AsyncBoundary isPending={batches.isPending} error={batches.error} onRetry={batches.refetch} label="Checking expiry dates">
        {batches.data?.items.length === 0 ? (
          <p className="text-sm text-stone-600">Nothing on the shelf expires within {warningDays} days.</p>
        ) : (
          <ul className="space-y-3">
            {batches.data?.items.map((b) => {
              const expired = b.daysToExpiry < 0
              const used = expired ? 1 : Math.min(1, Math.max(0.04, (warningDays - b.daysToExpiry) / warningDays))
              return (
                <li key={b.id} className="text-sm">
                  <div className="flex items-baseline justify-between gap-2">
                    <span className="min-w-0 truncate font-medium text-stone-900">{b.productName}</span>
                    <span className={cn('shrink-0 text-xs font-semibold tabular-nums', expired ? 'text-danger-800' : 'text-warning-800')}>
                      {expired ? `Expired ${-b.daysToExpiry} d ago` : b.daysToExpiry === 0 ? 'Expires today' : `${b.daysToExpiry} days left`}
                    </span>
                  </div>
                  <div aria-hidden="true" className="mt-1 h-2 rounded-full bg-earth-100">
                    <div className={cn('h-2 rounded-full', expired ? 'bg-danger' : 'bg-warning')} style={{ width: `${used * 100}%` }} />
                  </div>
                  <p className="mt-0.5 text-xs text-stone-600">
                    Batch {b.batchNo}, expires {b.expiryDate}
                  </p>
                </li>
              )
            })}
          </ul>
        )}
      </AsyncBoundary>
    </Panel>
  )
}

/** Orders packed and waiting for their farmer: who is coming to collect, so the counter is ready. */
export function AwaitingCollection() {
  const orders = useOrders({ status: 'Packed', pageSize: 8 })
  return (
    <Panel raised aria-labelledby="collect-heading" className="border-earth-200">
      <PanelHeader id="collect-heading" icon={<Receipt className="text-earth-600" />} title="Coming to collect" description="Packed orders; hand each over on the farmer's pickup code." />
      <AsyncBoundary isPending={orders.isPending} error={orders.error} onRetry={orders.refetch} label="Loading packed orders">
        {orders.data?.items.length === 0 ? (
          <p className="text-sm text-stone-600">No packed order is waiting.</p>
        ) : (
          <ul className="divide-y divide-border-subtle">
            {orders.data?.items.map((o) => (
              <li key={o.id} className="py-2 text-sm first:pt-0 last:pb-0">
                <p className="font-medium text-stone-900">{o.farmerName}</p>
                <p className="text-xs text-stone-600">
                  {o.orderNo}
                  {o.farmerPhone ? `, ${o.farmerPhone}` : ''}
                </p>
              </li>
            ))}
          </ul>
        )}
      </AsyncBoundary>
    </Panel>
  )
}
