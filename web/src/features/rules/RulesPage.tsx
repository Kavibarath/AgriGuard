import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { FilterBar } from '@/components/layout/FilterBar'
import { PageHeader, Panel, PanelHeader } from '@/components/layout/PageHeader'
import { Scale } from '@/components/icons'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { Button } from '@/components/ui/button'
import { DataTable, Pagination, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field } from '@/components/ui/field'
import { SelectField } from '@/components/ui/select'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { useCrops } from '@/features/registry/queries'
import { formatDateTime } from '@/features/inventory/format'
import { useRules } from '@/features/inventory/queries'
import { unitLabels, type CropRule } from '@/features/inventory/types'
import { RuleFormModal } from './RuleFormModal'

const statusViews = [
  { value: 'active', label: 'Approved', isActive: true },
  { value: 'inactive', label: 'Withdrawn', isActive: false },
  { value: 'all', label: 'All rules', isActive: undefined },
] as const

/**
 * The regulatory rules editor (§7 /rules) — one row per product-crop pair, holding the limits the
 * deterministic validator (V2–V10) judges every proposal against. Co-op Administrator only.
 * This is the viva's "modify a business rule" screen: an edit here changes the next verdict.
 */
export function RulesPage() {
  const [params, setParams] = useSearchParams()
  const crops = useCrops()
  const [editing, setEditing] = useState<CropRule | 'new' | null>(null)

  const status = statusViews.find((v) => v.value === params.get('status')) ?? statusViews[0]
  const query = {
    page: Number(params.get('page') ?? 1),
    pageSize: 20,
    search: params.get('search') ?? undefined,
    cropId: params.get('cropId') ?? undefined,
    isActive: status.isActive,
    sortBy: params.get('sortBy') ?? undefined,
    desc: params.get('desc') === 'true',
  }
  const rules = useRules(query)

  const updateParams = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(patch)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    if (!('page' in patch)) next.delete('page')
    setParams(next, { replace: true })
  }

  const columns: Column<CropRule>[] = [
    {
      key: 'product',
      header: 'Product',
      sortable: true,
      render: (r) => (
        <div>
          <p className="font-medium text-stone-900">{r.productName}</p>
          <p className="text-xs text-stone-600">{r.activeIngredientName}</p>
        </div>
      ),
    },
    { key: 'crop', header: 'Crop', sortable: true, render: (r) => r.cropName },
    {
      key: 'maxDose',
      header: 'Dose / ha',
      sortable: true,
      numeric: true,
      render: (r) => `${r.minDosePerHectare}–${r.maxDosePerHectare} ${unitLabels[r.unit]}`,
    },
    { key: 'phi', header: 'PHI', sortable: true, numeric: true, render: (r) => `${r.preHarvestIntervalDays} d` },
    { key: 'apps', header: 'Per cycle', numeric: true, secondary: true, render: (r) => <span className="whitespace-nowrap">{`${r.maxApplicationsPerCycle}×, ${r.minDaysBetweenApplications} d apart`}</span> },
    {
      key: 'status',
      header: 'Status',
      render: (r) => (
        <div className="flex flex-wrap gap-1">
          <StatusBadge label={r.isActive ? 'Approved' : 'Withdrawn'} tone={r.isActive ? 'done' : 'danger'} />
          {r.isRestricted && <StatusBadge label="Restricted" tone="warning" />}
        </div>
      ),
    },
    { key: 'updatedAt', header: 'Last changed', sortable: true, secondary: true, render: (r) => <span className="whitespace-nowrap">{formatDateTime(r.updatedAt)}</span> },
    {
      key: 'actions',
      header: '',
      render: (r) => (
        <div className="flex justify-end">
          <Button variant="ghost" className="h-8 px-2" onClick={() => setEditing(r)} aria-label={`Edit ${r.productName} on ${r.cropName}`}>
            Edit
          </Button>
        </div>
      ),
    },
  ]

  return (
    <div className="space-y-4 pb-4">
      <PageHeader
        title="Regulatory rules"
        description={
          <>
            Which products may be used on which crops, and within what limits. Every agent proposal is checked against these rows, and an edit
            applies to the next check. <span className="italic">Academic sample data.</span>
          </>
        }
        actions={<Button onClick={() => setEditing('new')}>Approve a product</Button>}
      />

      <div className="grid items-start gap-4 xl:grid-cols-12">
        <div className="space-y-3 xl:col-span-9">
          <FilterBar className="sm:grid-cols-[12rem_12rem_18rem]">
            <SelectField label="Status" value={status.value} onChange={(event) => updateParams({ status: event.target.value })}>
              {statusViews.map((v) => (
                <option key={v.value} value={v.value}>
                  {v.label}
                </option>
              ))}
            </SelectField>
            <SelectField label="Crop" value={query.cropId ?? ''} onChange={(event) => updateParams({ cropId: event.target.value || undefined })}>
              <option value="">All crops</option>
              {crops.data?.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name}
                </option>
              ))}
            </SelectField>
            <Field
              label="Search"
              type="search"
              placeholder="Product or active ingredient"
              defaultValue={query.search ?? ''}
              onChange={(event) => updateParams({ search: event.target.value || undefined })}
            />
          </FilterBar>

          <AsyncBoundary isPending={rules.isPending} error={rules.error} onRetry={rules.refetch} label="Loading rules">
            {rules.data?.items.length === 0 ? (
              <EmptyState title="No rules match" description="Try another crop, status or search." />
            ) : (
              <div className="space-y-3">
                <DataTable
                  tone="earth"
                  caption="Product approvals by crop"
                  columns={columns}
                  rows={rules.data?.items ?? []}
                  rowKey={(r) => r.id}
                  sort={{ sortBy: query.sortBy, desc: query.desc }}
                  onSortChange={(next) => updateParams({ sortBy: next.sortBy, desc: next.desc ? 'true' : undefined })}
                />
                <Pagination
                  page={rules.data?.page ?? 1}
                  totalPages={rules.data?.totalPages ?? 1}
                  totalCount={rules.data?.totalCount ?? 0}
                  onPageChange={(page) => updateParams({ page: String(page) })}
                />
              </div>
            )}
          </AsyncBoundary>
        </div>

        <aside className="xl:sticky xl:top-20 xl:col-span-3">
          <RuleReference />
        </aside>
      </div>

      <RuleFormModal open={editing !== null} rule={editing === 'new' ? undefined : (editing ?? undefined)} onClose={() => setEditing(null)} />
    </div>
  )
}

/** What each column means to the validator, so a live edit can be explained as it is made. */
const reference: { code: string; limit: string; effect: string }[] = [
  { code: 'V2', limit: 'Approved', effect: 'A product must be approved for the crop at all. Withdrawing it stops every new proposal.' },
  { code: 'V3', limit: 'Dose / ha', effect: 'The proposed dose must sit inside the range.' },
  { code: 'V5', limit: 'PHI', effect: 'The spray date plus these days must fall before the planned harvest. A hard stop.' },
  { code: 'V6', limit: 'Per cycle', effect: 'The count of sprays this season may not pass the limit.' },
  { code: 'V7', limit: 'Days apart', effect: 'The same active ingredient needs this gap since its last spray.' },
  { code: 'V8', limit: 'Rainfast', effect: 'The forecast must stay dry this many hours after spraying.' },
  { code: 'V10', limit: 'Restricted', effect: 'Needs a permit the system cannot check, so proposals are rejected.' },
]

function RuleReference() {
  return (
    <Panel raised aria-labelledby="reference-heading">
      <PanelHeader
        id="reference-heading"
        icon={<Scale />}
        title="How the validator reads a rule"
        description="Saved limits apply to the very next proposal. There is no code change and no restart."
      />
      <dl className="divide-y divide-border-subtle text-sm">
        {reference.map((r) => (
          <div key={r.code} className="grid grid-cols-[2.75rem_1fr] gap-x-2 py-2 first:pt-0 last:pb-0">
            <dt className="self-start rounded bg-surface-inset px-1.5 py-0.5 text-center text-xs font-bold text-stone-900 tabular-nums">{r.code}</dt>
            <dd>
              <span className="font-semibold text-stone-900">{r.limit}.</span> <span className="text-stone-700">{r.effect}</span>
            </dd>
          </div>
        ))}
      </dl>
    </Panel>
  )
}
