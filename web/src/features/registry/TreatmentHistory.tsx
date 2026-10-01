import { useSearchParams } from 'react-router'
import { FilterBar } from '@/components/layout/FilterBar'
import { Alert } from '@/components/ui/alert'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field } from '@/components/ui/field'
import { SelectField } from '@/components/ui/select'
import { StatusBadge, type Tone } from '@/components/ui/StatusBadge'
import { fullDay, shortDay } from '@/lib/dates'
import { formatQuantity } from '@/features/inventory/format'
import { useTreatmentHistory } from './queries'
import type { ApplicationStatus, TreatmentRow } from './types'

const applicationStatusTone: Record<ApplicationStatus, Tone> = {
  Scheduled: 'warning',
  Applied: 'done',
  Cancelled: 'neutral',
}

const statusViews: { value: string; label: string; status?: ApplicationStatus }[] = [
  { value: 'all', label: 'All sprays' },
  { value: 'Applied', label: 'Applied', status: 'Applied' },
  { value: 'Scheduled', label: 'Scheduled', status: 'Scheduled' },
  { value: 'Cancelled', label: 'Cancelled', status: 'Cancelled' },
]

/** yyyy-MM-dd or nothing: a half-typed date must not reach the API as a 400. */
const validDay = (value: string | null) => (value && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : undefined)

/**
 * Every spray on the plot across its crop cycles (GET /api/reports/plot-treatment-history), with
 * the pre-harvest interval each one set and how often each active ingredient was used — the
 * resistance-management view behind V6 and V7. The date range is sent to the API; the status
 * filter narrows the rows on the page, because the report is small and never paged.
 */
export function TreatmentHistory({ plotId }: { plotId: string }) {
  const [params, setParams] = useSearchParams()
  const from = validDay(params.get('from'))
  const to = validDay(params.get('to'))
  const view = statusViews.find((v) => v.value === params.get('status')) ?? statusViews[0]
  // An inverted range would only earn a 400; say so on the page instead of asking.
  const rangeInvalid = from !== undefined && to !== undefined && from > to
  const history = useTreatmentHistory({ plotId, from, to }, !rangeInvalid)

  const updateParams = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(patch)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    setParams(next, { replace: true })
  }

  const rows = history.data?.rows.filter((row) => !view.status || row.status === view.status) ?? []
  const filtered = Boolean(from || to || view.status)

  const columns: Column<TreatmentRow>[] = [
    { key: 'date', header: 'Date', render: (r) => <span className="whitespace-nowrap tabular-nums">{fullDay(r.applicationDate)}</span> },
    {
      key: 'product',
      header: 'Product',
      render: (r) => (
        <div>
          <p className="font-medium text-stone-900">{r.productName}</p>
          <p className="text-xs text-stone-600">
            {r.activeIngredient}
            {r.resistanceGroup && ` · group ${r.resistanceGroup}`}
          </p>
        </div>
      ),
    },
    {
      key: 'crop',
      header: 'Crop',
      secondary: true,
      render: (r) => (
        <div>
          <p>{r.cropName}</p>
          <p className="text-xs text-stone-600">sown {shortDay(r.cycleSownDate)}</p>
        </div>
      ),
    },
    {
      key: 'dose',
      header: 'Dose',
      numeric: true,
      secondary: true,
      render: (r) => (
        <div>
          <p>{formatQuantity(r.dosePerHectare, r.unit)}/ha</p>
          <p className="text-xs text-stone-600">{formatQuantity(r.totalQuantity, r.unit)} total</p>
        </div>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (r) => (
        <div className="space-y-0.5">
          <StatusBadge label={r.status} tone={applicationStatusTone[r.status]} />
          {r.prescriptionNo && <p className="text-xs text-stone-600">{r.prescriptionNo}</p>}
        </div>
      ),
    },
    {
      key: 'phi',
      header: 'Safe to harvest from',
      render: (r) =>
        r.safeToHarvestFrom ? (
          <div>
            <p>{shortDay(r.safeToHarvestFrom)}</p>
            <p className="text-xs text-stone-600">PHI {r.preHarvestIntervalDays} d</p>
          </div>
        ) : (
          <span className="text-stone-600">{r.status === 'Cancelled' ? 'Not sprayed' : 'No rule on file'}</span>
        ),
    },
  ]

  return (
    <section aria-labelledby="history-heading" className="space-y-3">
      <h2 id="history-heading" className="font-display text-xl font-semibold text-stone-900">
        Treatment history
      </h2>

      <FilterBar className="sm:grid-cols-3 lg:grid-cols-[12rem_12rem_14rem]">
        <Field label="From" type="date" value={from ?? ''} max={to} onChange={(event) => updateParams({ from: event.target.value || undefined })} />
        <Field label="To" type="date" value={to ?? ''} min={from} onChange={(event) => updateParams({ to: event.target.value || undefined })} />
        <SelectField label="Status" value={view.value} onChange={(event) => updateParams({ status: event.target.value === 'all' ? undefined : event.target.value })}>
          {statusViews.map((v) => (
            <option key={v.value} value={v.value}>
              {v.label}
            </option>
          ))}
        </SelectField>
      </FilterBar>

      {rangeInvalid ? (
        <Alert tone="warning">The start date must be on or before the end date.</Alert>
      ) : (
        <AsyncBoundary isPending={history.isPending} error={history.error} onRetry={history.refetch} label="Loading treatment history">
          {history.data && (
            <div className="space-y-4">
              {history.data.byActiveIngredient.length > 0 && (
                <div className="rounded-xl border border-border-subtle bg-surface-card p-4">
                  <h3 className="font-medium text-stone-900">
                    Sprays by active ingredient
                    <span className="font-normal text-stone-600"> · {history.data.applications} counted, cancelled ones excluded</span>
                  </h3>
                  <p className="mt-1 text-sm text-stone-600">
                    Repeating one resistance group breeds resistant strains; rotate between groups.
                  </p>
                  <ul className="mt-2 flex flex-wrap gap-2" aria-label="Sprays by active ingredient">
                    {history.data.byActiveIngredient.map((use) => (
                      <li key={use.activeIngredient} className="rounded-md bg-earth-50 px-2 py-1 text-sm ring-1 ring-earth-200 ring-inset">
                        <span className="font-medium">{use.activeIngredient}</span>
                        {use.resistanceGroup && <span className="text-stone-600"> · group {use.resistanceGroup}</span>}
                        <span className="text-stone-600">
                          {' '}
                          · {use.applications}× · last {shortDay(use.lastApplied)}
                        </span>
                      </li>
                    ))}
                  </ul>
                </div>
              )}

              {rows.length === 0 ? (
                <EmptyState
                  title={filtered ? 'No sprays match these filters' : 'No sprays recorded'}
                  description={
                    filtered
                      ? 'Widen the dates or choose another status.'
                      : 'Treatments appear here once an agronomist approves a prescription for this plot.'
                  }
                />
              ) : (
                <DataTable caption={`Treatment history of ${history.data.plotCode}`} columns={columns} rows={rows} rowKey={(r) => r.applicationId} />
              )}
            </div>
          )}
        </AsyncBoundary>
      )}
    </section>
  )
}
