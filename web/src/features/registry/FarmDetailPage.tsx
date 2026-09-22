import { useState } from 'react'
import { Link, useParams } from 'react-router'
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
    { key: 'plotCode', header: 'Plot', sortable: true, render: (plot) => <span className="font-medium">{plot.plotCode}</span> },
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
          <span className="text-stone-500">—</span>
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
    <main className="mx-auto max-w-5xl space-y-6 p-6">
      <nav className="text-sm">
        <Link to="/farms" className="text-brand-700 hover:underline">
          ← All farms
        </Link>
      </nav>

      <AsyncBoundary isPending={farm.isPending} error={farm.error} onRetry={farm.refetch} label="Loading farm">
        {farm.data && (
          <header className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <h1 className="text-2xl font-semibold text-stone-900">{farm.data.name}</h1>
              <p className="text-sm text-stone-600">
                {[farm.data.village, farm.data.districtName].filter(Boolean).join(', ')} · {farm.data.plotCount} plot
                {farm.data.plotCount === 1 ? '' : 's'} · {farm.data.totalAreaHectares.toFixed(2)} ha
              </p>
            </div>
            {canEdit && <Button onClick={() => setEditingPlot('new')}>Add plot</Button>}
          </header>
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
          <div className="grid gap-6 lg:grid-cols-[3fr_2fr]">
            <div className="space-y-2">
              <h2 className="text-sm font-medium uppercase tracking-wide text-stone-500">Plots</h2>
              <DataTable
                caption={`Plots on ${farm.data?.name ?? 'this farm'}`}
                columns={columns}
                rows={plots.data?.items ?? []}
                rowKey={(plot) => plot.id}
                onRowClick={(plot) => setSelectedPlotId(plot.id)}
              />
              <p className="text-xs text-stone-500">Select a plot to see its crop cycle.</p>
            </div>

            {selectedPlot && (
              <div className="space-y-2">
                <h2 className="text-sm font-medium uppercase tracking-wide text-stone-500">
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
    </main>
  )
}
