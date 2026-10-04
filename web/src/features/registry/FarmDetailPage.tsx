import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { PageHeader } from '@/components/layout/PageHeader'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { Button } from '@/components/ui/button'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { plotStatusTone, stageTone } from '@/components/ui/status-tones'
import { useCurrentUser } from '@/features/auth/auth-store'
import { CropCyclePanel } from './CropCyclePanel'
import { PlotFormModal } from './PlotFormModal'
import { useFarm, usePlots } from './queries'
import { soilLabels, stageLabels, type Plot } from './types'

export function FarmDetailPage() {
  const { farmId = '' } = useParams()
  const user = useCurrentUser()
  const farm = useFarm(farmId)
  const plots = usePlots({ farmId, pageSize: 100, sortBy: 'plotCode' }, Boolean(farmId))

  const [editingPlot, setEditingPlot] = useState<Plot | 'new' | null>(null)
  const [selectedPlotId, setSelectedPlotId] = useState<string | null>(null)

  const canEdit = user?.role === 'CoopAdministrator' || (farm.data ? farm.data.farmerId === user?.id : false)
  const selectedPlot = plots.data?.items.find((plot) => plot.id === selectedPlotId) ?? plots.data?.items[0] ?? null

  const columns: Column<Plot>[] = [
    {
      key: 'plotCode',
      header: 'Plot',
      sortable: true,
      render: (plot) => (
        <Link
          to={`/plots/${plot.id}`}
          className="whitespace-nowrap font-medium text-brand-700 hover:underline"
          onClick={(event) => event.stopPropagation()}
        >
          {plot.plotCode}
        </Link>
      ),
    },
    { key: 'name', header: 'Name', secondary: true, render: (plot) => plot.name ?? '—' },
    { key: 'area', header: 'Area (ha)', numeric: true, sortable: true, render: (plot) => plot.areaHectares.toFixed(3) },
    { key: 'soil', header: 'Soil', secondary: true, render: (plot) => soilLabels[plot.soilType] },
    {
      key: 'crop',
      header: 'Growing',
      render: (plot) =>
        plot.activeCycle ? (
          <span className="flex flex-wrap items-center gap-1.5">
            {plot.activeCycle.cropName}
            <StatusBadge label={stageLabels[plot.activeCycle.stage]} tone={stageTone[plot.activeCycle.stage] ?? 'neutral'} />
          </span>
        ) : (
          <span className="text-stone-600">—</span>
        ),
    },
    { key: 'status', header: 'Status', render: (plot) => <StatusBadge label={plot.status} tone={plotStatusTone[plot.status]} /> },
    {
      key: 'actions',
      header: '',
      render: (plot) =>
        canEdit ? (
          <div className="flex justify-end">
            <Button
              variant="ghost"
              className="h-8 px-2"
              onClick={(event) => {
                event.stopPropagation()
                setEditingPlot(plot)
              }}
            >
              Edit
            </Button>
          </div>
        ) : null,
    },
  ]

  return (
    <div className="space-y-4 pb-4">
      <AsyncBoundary isPending={farm.isPending} error={farm.error} onRetry={farm.refetch} label="Loading farm">
        {farm.data && (
          <PageHeader
            title={farm.data.name}
            description={`${[farm.data.village, farm.data.districtName].filter(Boolean).join(', ')} · ${farm.data.plotCount} plot${farm.data.plotCount === 1 ? '' : 's'} · ${farm.data.totalAreaHectares.toFixed(2)} ha`}
            actions={canEdit ? <Button onClick={() => setEditingPlot('new')}>Add plot</Button> : undefined}
          />
        )}
      </AsyncBoundary>

      <AsyncBoundary isPending={plots.isPending} error={plots.error} onRetry={plots.refetch} label="Loading plots">
        {plots.data?.items.length === 0 ? (
          <EmptyState
            title="No plots yet"
            description="Add the fields you grow on. Each plot's area drives treatment quantities, so enter it carefully."
            action={canEdit ? <Button onClick={() => setEditingPlot('new')}>Add plot</Button> : undefined}
          />
        ) : (
          <div className="grid items-start gap-4 xl:grid-cols-12">
            <div className="space-y-2 xl:col-span-7">
              <h2 className="font-display text-xl font-semibold text-stone-900">Plots</h2>
              <DataTable
                caption={`Plots on ${farm.data?.name ?? 'this farm'}`}
                columns={columns}
                rows={plots.data?.items ?? []}
                rowKey={(plot) => plot.id}
                onRowClick={(plot) => setSelectedPlotId(plot.id)}
              />
              <p className="text-xs text-stone-600">
                Select a plot to see its crop cycle; open its code for spray safety and treatment history.
              </p>
            </div>

            {selectedPlot && (
              <div className="space-y-2 xl:sticky xl:top-20 xl:col-span-5">
                <h2 className="font-display text-xl font-semibold text-stone-900">
                  Crop cycle · {selectedPlot.plotCode}
                </h2>
                <CropCyclePanel plot={selectedPlot} canEdit={canEdit} />
              </div>
            )}
          </div>
        )}
      </AsyncBoundary>

      {farmId && (
        <PlotFormModal
          open={editingPlot !== null}
          onClose={() => setEditingPlot(null)}
          farmId={farmId}
          plot={editingPlot === 'new' ? undefined : (editingPlot ?? undefined)}
        />
      )}
    </div>
  )
}
