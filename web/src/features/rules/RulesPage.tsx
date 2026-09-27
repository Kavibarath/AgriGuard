import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
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
          <p className="text-xs text-stone-500">{r.activeIngredientName}</p>
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
    { key: 'apps', header: 'Per cycle', numeric: true, secondary: true, render: (r) => `${r.maxApplicationsPerCycle}×, ${r.minDaysBetweenApplications} d apart` },
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
    { key: 'updatedAt', header: 'Last changed', sortable: true, secondary: true, render: (r) => formatDateTime(r.updatedAt) },
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
    <main className="mx-auto max-w-6xl space-y-6 p-6">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div className="space-y-1">
          <Link to="/dashboard" className="text-sm text-stone-500 hover:text-stone-800">
            ← Dashboard
          </Link>
          <h1 className="text-2xl font-semibold text-stone-900">Regulatory rules</h1>
          <p className="max-w-2xl text-sm text-stone-600">
            Which products may be used on which crops, and within what limits. Every agent proposal is checked against these rows, and an edit
            applies to the next check. <span className="italic">Academic sample data.</span>
          </p>
        </div>
        <Button onClick={() => setEditing('new')}>Approve a product</Button>
      </header>

      <div className="grid gap-3 sm:grid-cols-[12rem_12rem_18rem]">
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
      </div>

      <AsyncBoundary isPending={rules.isPending} error={rules.error} onRetry={rules.refetch} label="Loading rules">
        {rules.data?.items.length === 0 ? (
          <EmptyState title="No rules match" description="Try another crop, status or search." />
        ) : (
          <div className="space-y-3">
            <DataTable
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

      <RuleFormModal open={editing !== null} rule={editing === 'new' ? undefined : (editing ?? undefined)} onClose={() => setEditing(null)} />
    </main>
  )
}
