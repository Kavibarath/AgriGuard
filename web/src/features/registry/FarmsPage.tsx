import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { Alert } from '@/components/ui/alert'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { Button } from '@/components/ui/button'
import { DataTable, Pagination, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field } from '@/components/ui/field'
import { Modal } from '@/components/ui/Modal'
import { ApiError, userMessage } from '@/lib/api'
import { useCurrentUser } from '@/features/auth/auth-store'
import { FarmFormModal } from './FarmFormModal'
import { useDeleteFarm, useFarms } from './queries'
import type { Farm } from './types'

export function FarmsPage() {
  // Filters live in the URL: a filtered view can be bookmarked, shared, and survives a reload.
  const [params, setParams] = useSearchParams()
  const user = useCurrentUser()
  const [editing, setEditing] = useState<Farm | 'new' | null>(null)
  const [deleting, setDeleting] = useState<Farm | null>(null)

  const query = {
    page: Number(params.get('page') ?? 1),
    pageSize: 20,
    search: params.get('search') ?? undefined,
    sortBy: params.get('sortBy') ?? undefined,
    desc: params.get('desc') === 'true',
  }

  const farms = useFarms(query)
  const remove = useDeleteFarm()

  /**
   * Applies every change in one update. Two separate calls would each start from the same
   * stale `params`, so the second would silently undo the first — which is exactly what
   * happened to `sortBy` when sort direction was written separately.
   */
  const updateParams = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(patch)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    // Any filter change returns to page 1 — page 4 of a new result set is usually empty.
    if (!('page' in patch)) next.delete('page')
    setParams(next, { replace: true })
  }

  // Agronomists read the district's registry; only owners and administrators change it.
  const canEdit = user?.role === 'Farmer' || user?.role === 'CoopAdministrator'
  const canEditFarm = (farm: Farm) => user?.role === 'CoopAdministrator' || farm.farmerId === user?.id

  const columns: Column<Farm>[] = [
    {
      key: 'name',
      header: 'Farm',
      sortable: true,
      render: (farm) => (
        <Link to={`/farms/${farm.id}`} className="font-medium text-brand-700 hover:underline">
          {farm.name}
        </Link>
      ),
    },
    { key: 'village', header: 'Village', sortable: true, secondary: true, render: (farm) => farm.village ?? '—' },
    { key: 'district', header: 'District', sortable: true, render: (farm) => farm.districtName },
    { key: 'plots', header: 'Plots', numeric: true, render: (farm) => farm.plotCount },
    {
      key: 'area',
      header: 'Area (ha)',
      numeric: true,
      render: (farm) => farm.totalAreaHectares.toFixed(2),
    },
    {
      key: 'actions',
      header: '',
      render: (farm) =>
        canEditFarm(farm) ? (
          <div className="flex justify-end gap-1">
            <Button variant="ghost" className="h-8 px-2" onClick={() => setEditing(farm)}>
              Edit
            </Button>
            <Button variant="ghost" className="h-8 px-2 text-red-700" onClick={() => setDeleting(farm)}>
              Delete
            </Button>
          </div>
        ) : null,
    },
  ]

  return (
    <main className="mx-auto max-w-5xl space-y-6 p-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold text-stone-900">Farms</h1>
          <p className="text-sm text-stone-600">
            {user?.role === 'FieldAgronomist' ? 'Farms in your district' : 'Your registered farms and their plots'}
          </p>
        </div>
        {canEdit && <Button onClick={() => setEditing('new')}>Register farm</Button>}
      </header>

      <div className="max-w-xs">
        <Field
          label="Search"
          type="search"
          placeholder="Farm or village"
          defaultValue={query.search ?? ''}
          onChange={(event) => updateParams({ search: event.target.value || undefined })}
        />
      </div>

      <AsyncBoundary isPending={farms.isPending} error={farms.error} onRetry={farms.refetch} label="Loading farms">
        {farms.data?.items.length === 0 ? (
          <EmptyState
            title={query.search ? 'No farms match that search' : 'No farms yet'}
            description={
              query.search
                ? 'Try a different name or village.'
                : canEdit
                  ? 'Register your first farm, then add the plots you grow on.'
                  : 'No farms have been registered in your district yet.'
            }
            action={canEdit && !query.search ? <Button onClick={() => setEditing('new')}>Register farm</Button> : undefined}
          />
        ) : (
          <div className="space-y-3">
            <DataTable
              caption="Farms"
              columns={columns}
              rows={farms.data?.items ?? []}
              rowKey={(farm) => farm.id}
              sort={{ sortBy: query.sortBy, desc: query.desc }}
              onSortChange={(next) => updateParams({ sortBy: next.sortBy, desc: next.desc ? 'true' : undefined })}
            />
            <Pagination
              page={farms.data?.page ?? 1}
              totalPages={farms.data?.totalPages ?? 1}
              totalCount={farms.data?.totalCount ?? 0}
              onPageChange={(page) => updateParams({ page: String(page) })}
            />
          </div>
        )}
      </AsyncBoundary>

      <FarmFormModal
        open={editing !== null}
        farm={editing === 'new' ? undefined : (editing ?? undefined)}
        onClose={() => setEditing(null)}
      />

      <Modal
        open={deleting !== null}
        onClose={() => {
          remove.reset()
          setDeleting(null)
        }}
        title="Delete this farm?"
        description={`"${deleting?.name}" will be removed. This cannot be undone.`}
      >
        {remove.error && <Alert tone="error" className="mb-3">{userMessage(remove.error)}</Alert>}
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={() => setDeleting(null)}>
            Cancel
          </Button>
          <Button
            variant="danger"
            loading={remove.isPending}
            onClick={async () => {
              try {
                await remove.mutateAsync(deleting!.id)
                setDeleting(null)
              } catch (error) {
                // A farm with plots is refused with 409; the message says what to do first.
                if (!(error instanceof ApiError)) throw error
              }
            }}
          >
            Delete farm
          </Button>
        </div>
      </Modal>
    </main>
  )
}
