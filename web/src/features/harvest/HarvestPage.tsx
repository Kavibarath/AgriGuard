import { useState } from 'react'
import { useSearchParams } from 'react-router'
import { FilterBar } from '@/components/layout/FilterBar'
import { PageHeader } from '@/components/layout/PageHeader'
import { HBarList } from '@/components/charts/HBarList'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { Button } from '@/components/ui/button'
import { DataTable, Pagination, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { SelectField } from '@/components/ui/select'
import { StatTile } from '@/components/ui/StatTile'
import { isoToday } from '@/features/inventory/format'
import { useCrops, useDistricts } from '@/features/registry/queries'
import { weekdayDay } from '@/lib/dates'
import { formatKg, formatPercent } from './format'
import { useForecasts, useForecastVsActual } from './queries'
import { RecordActualModal } from './RecordActualModal'
import type { HarvestForecast } from './types'

const views = [
  { value: 'upcoming', label: 'Coming harvests' },
  { value: 'past', label: 'Past harvests' },
  { value: 'all', label: 'All forecasts' },
] as const

/**
 * The harvest dashboard (§7 /harvest, Component D): expected harvests coming up, and how good past
 * forecasts turned out to be (GET /api/reports/harvest-forecast-vs-actual). District and crop
 * filters apply to both and live in the URL.
 */
export function HarvestPage() {
  const [params, setParams] = useSearchParams()
  const [recording, setRecording] = useState<HarvestForecast | null>(null)
  const districts = useDistricts()
  const crops = useCrops()

  const view = views.find((v) => v.value === params.get('view')) ?? views[0]
  const today = isoToday()
  const filters = {
    districtId: params.get('districtId') ?? undefined,
    cropId: params.get('cropId') ?? undefined,
  }
  const query = {
    ...filters,
    page: Number(params.get('page') ?? 1),
    pageSize: 20,
    sortBy: params.get('sortBy') ?? undefined,
    desc: params.get('desc') === 'true' || (!params.get('sortBy') && view.value === 'past'),
    from: view.value === 'upcoming' ? today : undefined,
    to: view.value === 'past' ? isoToday(-1) : undefined,
  }
  const forecasts = useForecasts(query)
  const report = useForecastVsActual(filters)

  const updateParams = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(patch)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    if (!('page' in patch)) next.delete('page')
    setParams(next, { replace: true })
  }

  const columns: Column<HarvestForecast>[] = [
    {
      key: 'forecastHarvestDate',
      header: 'Harvest',
      sortable: true,
      render: (f) => <span className="tabular-nums">{weekdayDay(f.forecastHarvestDate)}</span>,
    },
    {
      key: 'crop',
      header: 'Crop · plot',
      render: (f) => (
        <div>
          <p className="font-medium text-stone-900">{f.cropName}</p>
          <p className="text-xs text-stone-600">
            {f.plotCode} · {f.farmerName}
          </p>
        </div>
      ),
    },
    { key: 'estimatedYieldKg', header: 'Forecast', sortable: true, numeric: true, render: (f) => formatKg(f.estimatedYieldKg) },
    { key: 'actual', header: 'Actual', numeric: true, render: (f) => (f.actualYieldKg != null ? formatKg(f.actualYieldKg) : '—') },
    { key: 'source', header: 'By', secondary: true, render: (f) => f.source },
    {
      key: 'actions',
      header: '',
      render: (f) => (
        <div className="flex justify-end">
          <Button variant="ghost" className="h-8 px-2" onClick={() => setRecording(f)} aria-label={`Record the actual harvest of ${f.cropName} on ${f.plotCode}`}>
            {f.actualYieldKg != null ? 'Correct' : 'Record actual'}
          </Button>
        </div>
      ),
    },
  ]

  const r = report.data
  const withinPercent = r && r.overall.forecasts > 0 ? Math.round((100 * r.overall.withinTolerance) / r.overall.forecasts) : null

  return (
    <div className="space-y-4 pb-4">
      <PageHeader title="Harvests" description="What is expected to come in, and how close past forecasts were to the real harvest." />

      <FilterBar className="sm:grid-cols-2 lg:grid-cols-[14rem_14rem]">
        <SelectField label="District" value={filters.districtId ?? ''} onChange={(e) => updateParams({ districtId: e.target.value || undefined })}>
          <option value="">All districts</option>
          {districts.data?.map((d) => (
            <option key={d.id} value={d.id}>
              {d.name}
            </option>
          ))}
        </SelectField>
        <SelectField label="Crop" value={filters.cropId ?? ''} onChange={(e) => updateParams({ cropId: e.target.value || undefined })}>
          <option value="">All crops</option>
          {crops.data?.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </SelectField>
      </FilterBar>

      <div className="grid items-start gap-4 xl:grid-cols-12">
        <section aria-labelledby="forecasts-heading" className="space-y-3 xl:col-span-7">
          <div className="flex flex-wrap items-end justify-between gap-3">
            <h2 id="forecasts-heading" className="font-display text-xl font-semibold text-stone-900">
              Forecasts
            </h2>
            <div className="w-60">
              <SelectField label="Show" value={view.value} onChange={(e) => updateParams({ view: e.target.value === 'upcoming' ? undefined : e.target.value })}>
                {views.map((v) => (
                  <option key={v.value} value={v.value}>
                    {v.label}
                  </option>
                ))}
              </SelectField>
            </div>
          </div>
          <AsyncBoundary isPending={forecasts.isPending} error={forecasts.error} onRetry={forecasts.refetch} label="Loading forecasts">
            {forecasts.data?.items.length === 0 ? (
              <EmptyState
                title={view.value === 'upcoming' ? 'No harvests forecast from today' : 'No forecasts match'}
                description="Farmers record forecasts from the phone app; agronomists can add them for their district."
              />
            ) : (
              <div className="space-y-3">
                <DataTable
                  tone="earth"
                  caption="Harvest forecasts"
                  columns={columns}
                  rows={forecasts.data?.items ?? []}
                  rowKey={(f) => f.id}
                  sort={{ sortBy: query.sortBy, desc: query.desc }}
                  onSortChange={(next) => updateParams({ sortBy: next.sortBy, desc: next.desc ? 'true' : undefined })}
                />
                <Pagination
                  page={forecasts.data?.page ?? 1}
                  totalPages={forecasts.data?.totalPages ?? 1}
                  totalCount={forecasts.data?.totalCount ?? 0}
                  onPageChange={(page) => updateParams({ page: String(page) })}
                />
              </div>
            )}
          </AsyncBoundary>
        </section>

        <section aria-labelledby="accuracy-heading" className="space-y-4 xl:col-span-5">
          <div>
            <h2 id="accuracy-heading" className="font-display text-xl font-semibold text-stone-900">
              Forecast vs actual
            </h2>
            <p className="text-sm text-stone-600">Forecasts whose actual harvest has been recorded.</p>
          </div>
          <AsyncBoundary isPending={report.isPending} error={report.error} onRetry={report.refetch} label="Comparing forecasts">
            {r && r.overall.forecasts === 0 ? (
              <EmptyState
                title="No harvest recorded against a forecast yet"
                description={r.pendingActuals > 0 ? `${r.pendingActuals} forecast(s) are waiting for their actual harvest.` : undefined}
              />
            ) : (
              r && (
                <div className="space-y-6">
                  <div className="grid gap-4 sm:grid-cols-3 xl:grid-cols-1 2xl:grid-cols-3">
                    <StatTile
                      label="Typical miss"
                      value={formatPercent(r.overall.meanAbsolutePercentError, false)}
                      detail="Mean absolute error: how far a forecast usually is, either way."
                    />
                    <StatTile
                      label="Overall bias"
                      value={formatPercent(r.overall.variancePercent)}
                      detail={`${formatKg(r.overall.actualKg)} harvested against ${formatKg(r.overall.forecastKg)} forecast.`}
                    />
                    <StatTile
                      label={`Within ±${r.tolerancePercent}%`}
                      value={withinPercent != null ? `${withinPercent}%` : '—'}
                      detail={`${r.overall.withinTolerance} of ${r.overall.forecasts} forecasts · ${r.pendingActuals} still to record`}
                    />
                  </div>

                  <div className="grid gap-8 rounded-xl border border-border-subtle bg-surface-card p-4 sm:p-5">
                    <HBarList
                      caption="Harvest against forecast, by crop (above or below)"
                      diverging
                      items={r.byCrop.map((g) => ({
                        key: g.key,
                        label: g.label,
                        value: g.accuracy.variancePercent ?? 0,
                        display: formatPercent(g.accuracy.variancePercent),
                        note: `${g.accuracy.forecasts} forecast(s) · typical miss ${formatPercent(g.accuracy.meanAbsolutePercentError, false)}`,
                      }))}
                    />
                    <HBarList
                      caption="Typical miss, by who made the forecast"
                      items={r.bySource.map((g) => ({
                        key: g.key,
                        label: g.label,
                        value: g.accuracy.meanAbsolutePercentError ?? 0,
                        display: formatPercent(g.accuracy.meanAbsolutePercentError, false),
                        note: `${g.accuracy.forecasts} forecast(s) · bias ${formatPercent(g.accuracy.variancePercent)}`,
                      }))}
                    />
                  </div>
                </div>
              )
            )}
          </AsyncBoundary>
        </section>
      </div>

      <RecordActualModal forecast={recording} onClose={() => setRecording(null)} />
    </div>
  )
}
