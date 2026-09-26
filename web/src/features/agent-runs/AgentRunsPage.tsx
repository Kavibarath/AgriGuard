import { Link, useNavigate, useSearchParams } from 'react-router'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { DataTable, Pagination, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field } from '@/components/ui/field'
import { SelectField } from '@/components/ui/select'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { caseStatusTone, runStatusTone, severityTone } from '@/components/ui/status-tones'
import { useCaseQueue } from './queries'
import { caseStatusLabels, runStatusLabels, type CaseStatus, type CaseSummary } from './types'

/** Queue views. "Awaiting approval" is the default: it is the agronomist's actual work. */
const views: { value: string; label: string; status?: CaseStatus }[] = [
  { value: 'PendingApproval', label: 'Awaiting approval', status: 'PendingApproval' },
  { value: 'AgentProcessing', label: 'Agent working', status: 'AgentProcessing' },
  { value: 'AwaitingManualReview', label: 'Manual review', status: 'AwaitingManualReview' },
  { value: 'Prescribed', label: 'Prescribed', status: 'Prescribed' },
  { value: 'Rejected', label: 'Rejected', status: 'Rejected' },
  { value: 'all', label: 'All cases' },
]

/**
 * The agronomist's queue (§7 /agent-runs). Built on the case list: each case carries its latest
 * run, so one request fills the table. Filters live in the URL, like the registry's.
 */
export function AgentRunsPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()

  const view = views.find((v) => v.value === params.get('view')) ?? views[0]
  const query = {
    page: Number(params.get('page') ?? 1),
    pageSize: 20,
    status: view.status,
    search: params.get('search') ?? undefined,
    sortBy: params.get('sortBy') ?? undefined,
    desc: params.get('desc') === 'true',
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

  const columns: Column<CaseSummary>[] = [
    {
      key: 'referenceNo',
      header: 'Case',
      sortable: true,
      render: (c) =>
        c.latestRunId ? (
          <Link to={`/agent-runs/${c.latestRunId}`} className="font-medium text-brand-700 hover:underline">
            {c.referenceNo}
          </Link>
        ) : (
          <span className="font-medium text-stone-900">{c.referenceNo}</span>
        ),
    },
    {
      key: 'farmer',
      header: 'Farmer · plot',
      render: (c) => (
        <span>
          {c.farmerName} <span className="text-stone-500">· {c.plotCode}</span>
        </span>
      ),
    },
    { key: 'crop', header: 'Crop', secondary: true, render: (c) => `${c.cropName} (${c.stage})` },
    {
      key: 'severity',
      header: 'Severity',
      sortable: true,
      render: (c) => <StatusBadge label={c.severity} tone={severityTone[c.severity]} />,
    },
    {
      key: 'status',
      header: 'Case status',
      sortable: true,
      secondary: true,
      render: (c) => <StatusBadge label={caseStatusLabels[c.status]} tone={caseStatusTone[c.status]} />,
    },
    {
      key: 'run',
      header: 'Agent run',
      render: (c) =>
        c.latestRunStatus ? (
          <StatusBadge label={runStatusLabels[c.latestRunStatus]} tone={runStatusTone[c.latestRunStatus]} />
        ) : (
          <span className="text-stone-500">None yet</span>
        ),
    },
    {
      key: 'createdAt',
      header: 'Reported',
      sortable: true,
      numeric: true,
      secondary: true,
      render: (c) => c.createdAt.slice(0, 10),
    },
  ]

  return (
    <main className="mx-auto max-w-6xl space-y-6 p-6">
      <header className="space-y-1">
        <Link to="/dashboard" className="text-sm text-stone-500 hover:text-stone-800">
          ← Dashboard
        </Link>
        <h1 className="text-2xl font-semibold text-stone-900">Agent runs</h1>
        <p className="text-sm text-stone-600">
          Crop-health cases in your district, with the agent's latest run. Open a case to review its evidence and decide.
        </p>
      </header>

      <div className="grid gap-3 sm:grid-cols-[14rem_18rem]">
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
          placeholder="Case reference, plot or farmer"
          defaultValue={query.search ?? ''}
          onChange={(event) => updateParams({ search: event.target.value || undefined })}
        />
      </div>

      <AsyncBoundary isPending={cases.isPending} error={cases.error} onRetry={cases.refetch} label="Loading cases">
        {cases.data?.items.length === 0 ? (
          <EmptyState
            title={query.search ? 'No cases match that search' : `Nothing to show under “${view.label}”`}
            description={
              view.status === 'PendingApproval' && !query.search
                ? 'When the agent finishes a proposal that passes the safety rules, it appears here for your decision.'
                : 'Try another view or search.'
            }
          />
        ) : (
          <div className="space-y-3">
            <DataTable
              caption="Crop-health cases and their agent runs"
              columns={columns}
              rows={cases.data?.items ?? []}
              rowKey={(c) => c.id}
              sort={{ sortBy: query.sortBy, desc: query.desc }}
              onSortChange={(next) => updateParams({ sortBy: next.sortBy, desc: next.desc ? 'true' : undefined })}
              onRowClick={(c) => c.latestRunId && navigate(`/agent-runs/${c.latestRunId}`)}
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
