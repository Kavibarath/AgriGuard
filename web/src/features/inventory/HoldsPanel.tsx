import { useState } from 'react'
import { Alert } from '@/components/ui/alert'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { Button } from '@/components/ui/button'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { userMessage } from '@/lib/api'
import { formatDateTime, formatQuantity } from './format'
import { useCommitReservation, useReleaseReservation, useReservations } from './queries'
import type { Reservation } from './types'

/**
 * Stock currently held. A counter hold is resolved here: committed once sold (it leaves the shelf)
 * or released (back on sale). A hold for a prescription awaiting approval is shown but not
 * actionable — the agronomist's decision resolves it.
 */
export function HoldsPanel() {
  const holds = useReservations({ status: 'Held', pageSize: 50 })
  const commit = useCommitReservation()
  const release = useReleaseReservation()
  const [acting, setActing] = useState<string | null>(null)
  const error = commit.error ?? release.error

  const act = async (id: string, action: typeof commit | typeof release) => {
    commit.reset()
    release.reset()
    setActing(id)
    try {
      await action.mutateAsync(id)
    } catch {
      // Shown below the table; a 409 means someone resolved it first, and the refetch shows how.
    } finally {
      setActing(null)
    }
  }

  const columns: Column<Reservation>[] = [
    {
      key: 'product',
      header: 'Product',
      render: (r) => (
        <div>
          <p className="font-medium text-stone-900">{r.productName}</p>
          {r.note && <p className="text-xs text-stone-600">{r.note}</p>}
        </div>
      ),
    },
    { key: 'quantity', header: 'Held', numeric: true, render: (r) => `${r.packs} × pack (${formatQuantity(r.totalQuantity, r.unit)})` },
    { key: 'batches', header: 'From batches', secondary: true, render: (r) => r.lines.map((l) => l.batchNo).join(', ') },
    { key: 'expiresAt', header: 'Released at', secondary: true, render: (r) => formatDateTime(r.expiresAt) },
    {
      key: 'actions',
      header: '',
      render: (r) =>
        r.agentRunId ? (
          <span className="text-xs text-stone-600">For a prescription awaiting approval</span>
        ) : (
          <div className="flex justify-end gap-1">
            <Button variant="ghost" className="h-8 px-2" loading={acting === r.id && commit.isPending} onClick={() => act(r.id, commit)}>
              Sold
            </Button>
            <Button variant="ghost" className="h-8 px-2" loading={acting === r.id && release.isPending} onClick={() => act(r.id, release)}>
              Release
            </Button>
          </div>
        ),
    },
  ]

  return (
    <section aria-labelledby="holds-heading" className="space-y-3">
      <div>
        <h2 id="holds-heading" className="font-display text-xl font-semibold text-stone-900">
          Stock on hold
        </h2>
        <p className="text-sm text-stone-600">Held stock stays on the shelf but cannot be sold to anyone else. Unresolved holds are released after 24 hours.</p>
      </div>
      {error && <Alert tone="error">{userMessage(error)}</Alert>}
      <AsyncBoundary isPending={holds.isPending} error={holds.error} onRetry={holds.refetch} label="Loading holds">
        {holds.data?.items.length === 0 ? (
          <p className="rounded-xl border border-dashed border-earth-200 bg-earth-50/50 px-4 py-6 text-center text-sm text-stone-600">Nothing is on hold.</p>
        ) : (
          <DataTable tone="earth" caption="Stock on hold" columns={columns} rows={holds.data?.items ?? []} rowKey={(r) => r.id} />
        )}
      </AsyncBoundary>
    </section>
  )
}
