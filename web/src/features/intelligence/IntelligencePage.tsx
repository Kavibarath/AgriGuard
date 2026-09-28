import { Link, useSearchParams } from 'react-router'
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
import { useOutbreakSignal } from './queries'
import { levelLabels, trendLabels, type DistrictPressure } from './types'

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
    <main className="mx-auto max-w-6xl space-y-8 p-6">
      <header className="space-y-1">
        <Link to="/dashboard" className="text-sm text-stone-500 hover:text-stone-800">
          ← Dashboard
        </Link>
        <h1 className="text-2xl font-semibold text-stone-900">Disease intelligence</h1>
        <p className="text-sm text-stone-600">Outbreak pressure from cases agronomists have confirmed, by district and crop.</p>
      </header>

      <div className="grid gap-3 sm:grid-cols-3">
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
      </div>

      <AsyncBoundary isPending={signal.isPending} error={signal.error} onRetry={signal.refetch} label="Working out disease pressure">
        {s && s.reportedCases === 0 && s.districts.length === 0 ? (
          <EmptyState title="No cases reported in this window" description="Try a longer window, another crop or all districts." />
        ) : (
          s && (
            <div className="space-y-8">
              <p className="rounded-md border border-stone-200 bg-white px-4 py-3 text-sm text-stone-800">{s.summary}</p>

              <section aria-label="Headline figures" className="grid gap-4 sm:grid-cols-3">
                <StatTile
                  label="Pressure index"
                  value={
                    <span className="flex items-baseline gap-2">
                      <span>{s.pressureIndex}</span>
                      <span className="text-sm font-normal text-stone-500">/ 100</span>
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
              </section>

              <section className="grid gap-8 rounded-lg border border-stone-200 bg-white p-4 lg:grid-cols-[3fr_2fr]">
                <BarChart
                  caption={`Cases per day, last ${s.windowDays} days`}
                  series={[
                    { key: 'confirmed', label: 'Confirmed', color: 'var(--color-series-1)' },
                    { key: 'unconfirmed', label: 'Reported, not yet confirmed', color: 'var(--color-series-2)' },
                  ]}
                  data={s.daily.map((d) => ({ label: shortDay(d.date), values: [d.confirmedCases, d.reportedCases - d.confirmedCases] }))}
                />
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
              </section>

              <section aria-labelledby="districts-heading" className="space-y-3">
                <div>
                  <h2 id="districts-heading" className="text-lg font-semibold text-stone-900">
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

              <details className="text-sm text-stone-600">
                <summary className="cursor-pointer font-medium text-stone-800">How the index is worked out</summary>
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
    </main>
  )
}
