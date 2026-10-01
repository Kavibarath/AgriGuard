import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { FilterBar } from '@/components/layout/FilterBar'
import { Panel, PageHeader, PanelHeader } from '@/components/layout/PageHeader'
import { TileMap } from '@/components/map/TileMap'
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
import { useDeleteFarm, useFarms, usePlots } from './queries'
import { RegistryTiles } from './RegistryTiles'
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
  // Every plot the caller can see, for the map and the totals; a registry this size fits one page.
  const plots = usePlots({ pageSize: 100 })
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
            <Button variant="ghost" className="h-8 px-2 text-danger-800 hover:bg-danger-50" onClick={() => setDeleting(farm)}>
              Delete
            </Button>
          </div>
        ) : null,
    },
  ]

  return (
    <div className="space-y-6">
      <PageHeader
        title="Farms"
        description={user?.role === 'FieldAgronomist' ? 'Farms in your district' : 'Your registered farms and their plots'}
        actions={canEdit ? <Button onClick={() => setEditing('new')}>Register farm</Button> : undefined}
      />

      <RegistryTiles farmCount={farms.data?.totalCount} plots={plots.data?.items} />

      <div className="grid items-start gap-4 xl:grid-cols-12">
        <div className="space-y-3 xl:col-span-8">
          <FilterBar className="sm:grid-cols-[20rem]">
            <Field
              label="Search"
              type="search"
              placeholder="Farm or village"
              defaultValue={query.search ?? ''}
              onChange={(event) => updateParams({ search: event.target.value || undefined })}
            />
          </FilterBar>

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
        </div>

        <aside className="xl:sticky xl:top-20 xl:col-span-4">
          <Panel aria-labelledby="plots-map-heading">
            <PanelHeader id="plots-map-heading" title="Plots on the map" description="Each plot's registered centre; open one for its spray safety." />
            {plots.data && plots.data.items.length > 0 ? (
              <TileMap
                label={`Map of ${plots.data.items.length} plot${plots.data.items.length === 1 ? '' : 's'}`}
                height={360}
                pins={plots.data.items.map((plot) => ({
                  id: plot.id,
                  position: { lat: plot.latitude, lng: plot.longitude },
                  label: `Plot ${plot.plotCode} on ${plot.farmName}${plot.activeCycle ? `, growing ${plot.activeCycle.cropName}` : ', nothing growing'}`,
                  tone: plot.activeCycle ? 'active' : 'neutral',
                  variant: plot.activeCycle ? 'dot' : 'ring',
                  href: `/plots/${plot.id}`,
                }))}
              />
            ) : (
              <p className="text-sm text-stone-600">{plots.isPending ? 'Loading plots…' : 'No plots registered yet.'}</p>
            )}
            <p className="mt-2 text-xs text-stone-600">Filled dot: a crop growing. Ring: nothing sown.</p>
          </Panel>
        </aside>
      </div>

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
    </div>
  )
}
