import { Link, useSearchParams } from 'react-router'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { DataTable, Pagination, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field } from '@/components/ui/field'
import { SelectField } from '@/components/ui/select'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { caseStatusTone, runStatusTone, severityTone } from '@/components/ui/status-tones'
import { TileMap, type MapPin } from '@/components/map/TileMap'
import { formatDateTime } from '@/lib/dates'
import { useCaseQueue } from '@/features/agent-runs/queries'
import { caseStatusLabels, runStatusLabels, type CaseSeverity, type CaseStatus, type CaseSummary } from '@/features/agent-runs/types'
import { useCrops } from '@/features/registry/queries'
import { severityPinTone } from './format'

const statuses = Object.keys(caseStatusLabels) as CaseStatus[]
const severities: CaseSeverity[] = ['Low', 'Medium', 'High', 'Critical']

/**
 * Every reported crop problem the caller may see (§7 /cases): a farmer's own, an agronomist's
 * district, or all of them for an administrator — the API decides. The page's cases are also
 * drawn on a map, so a cluster of blight on neighbouring plots is visible at a glance. Filters,
 * sort and page live in the URL, so a view can be shared or bookmarked.
 */
export function CasesPage() {
  const [params, setParams] = useSearchParams()
  const crops = useCrops()

  const status = statuses.find((s) => s === params.get('status'))
  const severity = severities.find((s) => s === params.get('severity'))
  const query = {
    page: Number(params.get('page') ?? 1),
    pageSize: 20,
    status,
    severity,
    cropId: params.get('cropId') ?? undefined,
    search: params.get('search') ?? undefined,
    // Newest first unless the viewer chose otherwise: this is a record, not the approval queue.
    sortBy: params.get('sortBy') ?? 'createdAt',
    desc: params.has('sortBy') ? params.get('desc') === 'true' : true,
  }
  const cases = useCaseQueue(query)

  const updateParams = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(patch)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    if (!('page' in patch)) next.delete('page')
    setParams(next, { replace: true })
  }

  const rows = cases.data?.items ?? []
  const pins: MapPin[] = rows.map((c) => ({
    id: c.id,
    position: { lat: c.reportedLatitude, lng: c.reportedLongitude },
    label: `${c.referenceNo}: ${c.cropName} on ${c.plotCode}, ${c.severity.toLowerCase()} severity, ${caseStatusLabels[c.status].toLowerCase()}`,
    tone: severityPinTone[c.severity],
    href: `/cases/${c.id}`,
  }))
  const filtered = Boolean(status || severity || query.cropId || query.search)

  const columns: Column<CaseSummary>[] = [
    {
      key: 'referenceNo',
      header: 'Case',
      sortable: true,
      render: (c) => (
        <Link to={`/cases/${c.id}`} className="whitespace-nowrap font-medium text-brand-700 hover:underline">
          {c.referenceNo}
        </Link>
      ),
    },
    { key: 'createdAt', header: 'Reported', sortable: true, secondary: true, render: (c) => <span className="whitespace-nowrap">{formatDateTime(c.createdAt)}</span> },
    {
      key: 'farmer',
      header: 'Farmer · plot',
      render: (c) => (
        <span>
          {c.farmerName} <span className="text-stone-500">· {c.plotCode}</span>
        </span>
      ),
    },
    {
      key: 'crop',
      header: 'Crop',
      secondary: true,
      render: (c) => (
        <div>
          <p>{c.cropName}</p>
          <p className="text-xs text-stone-500">{c.districtName}</p>
        </div>
      ),
    },
    { key: 'severity', header: 'Severity', sortable: true, render: (c) => <StatusBadge label={c.severity} tone={severityTone[c.severity]} /> },
    {
      key: 'status',
      header: 'Status',
      sortable: true,
      render: (c) => (
        <div className="space-y-0.5">
          <StatusBadge label={caseStatusLabels[c.status]} tone={caseStatusTone[c.status]} />
          {c.latestRunStatus && c.latestRunStatus !== c.status && (
            <p className="text-xs text-stone-500">
              Agent: <StatusBadge label={runStatusLabels[c.latestRunStatus]} tone={runStatusTone[c.latestRunStatus]} className="px-1.5 py-0" />
            </p>
          )}
        </div>
      ),
    },
  ]

  return (
    <main className="mx-auto max-w-6xl space-y-6 p-6">
      <header className="space-y-1">
        <Link to="/dashboard" className="text-sm text-stone-500 hover:text-stone-800">
          ← Dashboard
        </Link>
        <h1 className="text-2xl font-semibold text-stone-900">Crop cases</h1>
        <p className="text-sm text-stone-600">Problems farmers reported from the field, where they were standing when they did.</p>
      </header>

      <div className="grid gap-3 sm:grid-cols-4">
        <SelectField label="Status" value={status ?? ''} onChange={(event) => updateParams({ status: event.target.value || undefined })}>
          <option value="">Any status</option>
          {statuses.map((s) => (
            <option key={s} value={s}>
              {caseStatusLabels[s]}
            </option>
          ))}
        </SelectField>
        <SelectField label="Severity" value={severity ?? ''} onChange={(event) => updateParams({ severity: event.target.value || undefined })}>
          <option value="">Any severity</option>
          {severities.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </SelectField>
        <SelectField label="Crop" value={query.cropId ?? ''} onChange={(event) => updateParams({ cropId: event.target.value || undefined })}>
          <option value="">Any crop</option>
          {crops.data?.map((crop) => (
            <option key={crop.id} value={crop.id}>
              {crop.name}
            </option>
          ))}
        </SelectField>
        <Field
          label="Search"
          type="search"
          placeholder="Case, plot or farmer"
          defaultValue={query.search ?? ''}
          onChange={(event) => updateParams({ search: event.target.value || undefined })}
        />
      </div>

      <AsyncBoundary isPending={cases.isPending} error={cases.error} onRetry={cases.refetch} label="Loading cases">
        {rows.length === 0 ? (
          <EmptyState
            title={filtered ? 'No cases match these filters' : 'No cases reported yet'}
            description={filtered ? 'Try another status, severity or search.' : 'Farmers report crop problems from the AgriGuard phone app; each one appears here.'}
          />
        ) : (
          <div className="space-y-4">
            <TileMap pins={pins} label={`Map of the ${rows.length} case${rows.length === 1 ? '' : 's'} on this page`} height={300} />
            <p className="text-xs text-stone-500">Each dot is where the phone was when the problem was reported; its colour is the severity. Select one to open the case.</p>
            <DataTable
              caption="Crop cases"
              columns={columns}
              rows={rows}
              rowKey={(c) => c.id}
              sort={{ sortBy: query.sortBy, desc: query.desc }}
              onSortChange={(next) => updateParams({ sortBy: next.sortBy, desc: next.desc ? 'true' : 'false' })}
            />
            <Pagination
              page={cases.data?.page ?? 1}
              totalPages={cases.data?.totalPages ?? 1}
              totalCount={cases.data?.totalCount ?? 0}
              onPageChange={(page) => updateParams({ page: String(page) })}
            />
          </div>
        )}
      </AsyncBoundary>
    </main>
  )
}
