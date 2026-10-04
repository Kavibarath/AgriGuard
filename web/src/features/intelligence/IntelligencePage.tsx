import { useSearchParams } from 'react-router'
import { FilterBar } from '@/components/layout/FilterBar'
import { PageHeader, Panel, PanelHeader } from '@/components/layout/PageHeader'
import { Pulse } from '@/components/icons'
import { TileMap, type PinTone } from '@/components/map/TileMap'
import { BarChart } from '@/components/charts/BarChart'
import { HBarList } from '@/components/charts/HBarList'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { SelectField } from '@/components/ui/select'
import { StatTile } from '@/components/ui/StatTile'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { pressureTone } from '@/components/ui/status-tones'
import { useCrops, useDistricts } from '@/features/registry/queries'
import { shortDay } from '@/lib/dates'
import { cn } from '@/lib/utils'
import { useOutbreakSignal } from './queries'
import { levelLabels, trendLabels, type DistrictPressure, type PressureLevel } from './types'

const windows = [7, 14, 30, 90]

/** "LATE_BLIGHT" → "Late blight", for a district whose pathogen is not among the named ones. */
function humanise(code: string) {
  const words = code.toLowerCase().replaceAll('_', ' ')
  return words.charAt(0).toUpperCase() + words.slice(1)
}

/**
 * District disease pressure (§7 /intelligence, Component D). The same calculation the Diagnosis
 * agent reads through its outbreak tool, so what the agronomist sees here is what the agent weighed.
 * Filters live in the URL.
 */
export function IntelligencePage() {
  const [params, setParams] = useSearchParams()
  const districts = useDistricts()
  const crops = useCrops()

  const days = windows.includes(Number(params.get('days'))) ? Number(params.get('days')) : 14
  const query = {
    cropId: params.get('cropId') ?? undefined,
    districtId: params.get('districtId') ?? undefined,
    days,
  }
  const signal = useOutbreakSignal(query)

  const updateParams = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(patch)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    setParams(next, { replace: true })
  }

  const s = signal.data
  const names = new Map(s?.topPathogens.map((p) => [p.code, p.name]))
  const top = s?.topPathogens[0]

  const columns: Column<DistrictPressure>[] = [
    {
      key: 'district',
      header: 'District',
      render: (d) => <span className="font-medium text-stone-900">{d.districtName}</span>,
    },
    {
      key: 'pressure',
      header: 'Pressure',
      render: (d) => (
        <span className="flex items-center gap-2">
          <StatusBadge label={levelLabels[d.level]} tone={pressureTone[d.level]} />
          <span className="tabular-nums text-stone-600">{d.pressureIndex}/100</span>
        </span>
      ),
    },
    { key: 'confirmed', header: 'Confirmed', numeric: true, render: (d) => d.confirmedCases },
    { key: 'reported', header: 'Reported', numeric: true, secondary: true, render: (d) => d.reportedCases },
    {
      key: 'top',
      header: 'Main threat',
      render: (d) => (d.topPathogenCode ? (names.get(d.topPathogenCode) ?? humanise(d.topPathogenCode)) : '—'),
    },
  ]

  return (
    <div className="space-y-4 pb-4">
      <PageHeader title="Disease intelligence" description="Outbreak pressure from cases agronomists have confirmed, by district and crop." />

      <FilterBar className="sm:grid-cols-3 lg:grid-cols-[14rem_14rem_12rem]">
        <SelectField label="Crop" value={query.cropId ?? ''} onChange={(e) => updateParams({ cropId: e.target.value || undefined })}>
          <option value="">All crops</option>
          {crops.data?.map((c) => (
            <option key={c.id} value={c.id}>
              {c.name}
            </option>
          ))}
        </SelectField>
        <SelectField label="District" value={query.districtId ?? ''} onChange={(e) => updateParams({ districtId: e.target.value || undefined })}>
          <option value="">All districts</option>
          {districts.data?.map((d) => (
            <option key={d.id} value={d.id}>
              {d.name}
            </option>
          ))}
        </SelectField>
        <SelectField label="Window" value={String(days)} onChange={(e) => updateParams({ days: e.target.value === '14' ? undefined : e.target.value })}>
          {windows.map((w) => (
            <option key={w} value={w}>
              Last {w} days
            </option>
          ))}
        </SelectField>
      </FilterBar>

      <AsyncBoundary isPending={signal.isPending} error={signal.error} onRetry={signal.refetch} label="Working out disease pressure">
        {s && s.reportedCases === 0 && s.districts.length === 0 ? (
          <EmptyState title="No cases reported in this window" description="Try a longer window, another crop or all districts." />
        ) : (
          s && (
            <div className="space-y-4">
              <p className="flex items-start gap-2 rounded-xl border border-border-subtle bg-surface-card px-4 py-3 text-sm text-stone-800">
                <Pulse size={18} className="mt-px shrink-0 text-brand-600" />
                {s.summary}
              </p>

              <section aria-label="Headline figures" className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
                <StatTile
                  label="Pressure index"
                  value={
                    <span className="flex items-baseline gap-2">
                      <span>{s.pressureIndex}</span>
                      <span className="font-sans text-sm font-normal text-stone-600">/ 100</span>
                    </span>
                  }
                  detail={
                    <span className="flex flex-wrap items-center gap-2">
                      <StatusBadge label={levelLabels[s.level]} tone={pressureTone[s.level]} />
                      {trendLabels[s.trend]}
                    </span>
                  }
                />
                <StatTile
                  label="Confirmed cases"
                  value={s.confirmedCases}
                  detail={`of ${s.reportedCases} reported, ${shortDay(s.from)} – ${shortDay(s.to)}`}
                />
                <StatTile
                  label="Main threat"
                  value={<span className="text-xl">{top ? top.name : 'None confirmed'}</span>}
                  detail={top ? `${top.sharePercent}% of the pressure · ${top.confirmedCases} confirmed` : 'No diagnosis confirmed in this window.'}
                />
                <StatTile
                  label="Districts reporting"
                  value={s.districts.length}
                  detail={`${s.districts.filter((d) => d.level === 'High' || d.level === 'Severe').length} at high or severe pressure`}
                />
              </section>

              <div className="grid items-start gap-4 xl:grid-cols-12">
                <Panel raised aria-labelledby="map-heading" className="xl:col-span-7">
                  <PanelHeader id="map-heading" title="Outbreak map" description="Each district at its centre, coloured and sized by pressure. Select one to focus on it." />
                  <TileMap
                    label="Map of disease pressure by district"
                    height={380}
                    maxFitZoom={10}
                    pins={s.districts.map((d) => ({
                      id: d.districtId,
                      position: { lat: d.latitude, lng: d.longitude },
                      label: `${d.districtName}: ${levelLabels[d.level].toLowerCase()} pressure, ${d.pressureIndex} of 100, ${d.confirmedCases} confirmed`,
                      tone: ramp[d.level].tone,
                      size: ramp[d.level].size,
                      selected: d.districtId === query.districtId,
                      href: `?${withDistrict(params, d.districtId)}`,
                    }))}
                  />
                  <ul aria-label="Pressure levels" className="mt-3 flex flex-wrap gap-x-4 gap-y-1 text-xs text-stone-700">
                    {(['Low', 'Moderate', 'High', 'Severe'] as PressureLevel[]).map((level) => (
                      <li key={level} className="inline-flex items-center gap-1.5">
                        <span aria-hidden="true" className={cn('inline-block rounded-full ring-2 ring-white', ramp[level].swatch)} />
                        {levelLabels[level]}
                        {level === 'Low' && ' or none'}
                      </li>
                    ))}
                  </ul>
                </Panel>

                <Panel aria-labelledby="pathogens-heading" className="xl:col-span-5">
                  <PanelHeader id="pathogens-heading" title="Pathogens, ranked" description="Share of the pressure in the window, largest first." />
                  {s.topPathogens.length > 0 ? (
                    <HBarList
                      caption="Share of the pressure, by pathogen"
                      max={100}
                      items={s.topPathogens.map((p) => ({
                        key: p.code,
                        label: p.name,
                        value: p.sharePercent,
                        display: `${p.sharePercent}%`,
                        note: `${p.confirmedCases} confirmed · last ${shortDay(p.lastReportedOn)}`,
                      }))}
                    />
                  ) : (
                    <p className="text-sm text-stone-600">No pathogen confirmed yet: the reports are waiting for an agronomist.</p>
                  )}
                </Panel>
              </div>

              <div className="grid items-start gap-4 xl:grid-cols-12">
                <Panel aria-labelledby="daily-heading" className="xl:col-span-5">
                  <PanelHeader id="daily-heading" title="Cases per day" description="Confirmed, and reported but not yet confirmed." />
                  <BarChart
                    caption={`Cases per day, last ${s.windowDays} days`}
                    series={[
                      { key: 'confirmed', label: 'Confirmed', color: 'var(--color-series-1)' },
                      { key: 'unconfirmed', label: 'Reported, not yet confirmed', color: 'var(--color-series-2)' },
                    ]}
                    data={s.daily.map((d) => ({ label: shortDay(d.date), values: [d.confirmedCases, d.reportedCases - d.confirmedCases] }))}
                  />
                </Panel>

                <section aria-labelledby="districts-heading" className="space-y-3 xl:col-span-7">
                  <div>
                    <h2 id="districts-heading" className="font-display text-xl font-semibold text-stone-900">
                      By district
                    </h2>
                    <p className="text-sm text-stone-600">
                      {query.cropId ? 'This crop, every' : 'Every'} district with reports in the window. Choose one to focus on it.
                    </p>
                  </div>
                  <DataTable
                    caption="Disease pressure by district"
                    columns={columns}
                    rows={s.districts}
                    rowKey={(d) => d.districtId}
                    onRowClick={(d) => updateParams({ districtId: d.districtId })}
                  />
                </section>
              </div>

              <details className="rounded-xl border border-border-subtle bg-surface-card px-4 py-3 text-sm text-stone-700">
                <summary className="cursor-pointer font-medium text-stone-900">How the index is worked out</summary>
                <p className="mt-2 max-w-3xl">
                  Each confirmed case counts by severity (low ½, medium 1, high 1½, critical 2) and by age: a case a week old counts
                  half, two weeks old a quarter. The total is mapped onto 0–100, rising steeply for the first few cases and then
                  levelling off. Below 25 is low, below 50 moderate, below 75 high, and above that severe. Reports an agronomist has not
                  yet confirmed are counted on the chart but never raise the index.
                </p>
              </details>
            </div>
          )
        )}
      </AsyncBoundary>
    </div>
  )
}

/**
 * The sequential ramp for pressure: green through amber to deep red, and larger as it rises, so
 * the size carries the order even without the colour. Every pin's label names its level in words.
 */
const ramp: Record<PressureLevel, { tone: PinTone; size: 'sm' | 'md' | 'lg'; swatch: string }> = {
  None: { tone: 'success', size: 'sm', swatch: 'size-2.5 bg-success' },
  Low: { tone: 'success', size: 'sm', swatch: 'size-2.5 bg-success' },
  Moderate: { tone: 'warning', size: 'md', swatch: 'size-3 bg-warning' },
  High: { tone: 'danger', size: 'md', swatch: 'size-3.5 bg-danger' },
  Severe: { tone: 'severe', size: 'lg', swatch: 'size-4 bg-danger-800' },
}

/** The current filters with one district chosen: a pin keeps the crop and window it was drawn for. */
function withDistrict(params: URLSearchParams, districtId: string): string {
  const next = new URLSearchParams(params)
  next.set('districtId', districtId)
  return next.toString()
}
