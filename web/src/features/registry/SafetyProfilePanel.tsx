import { useSearchParams } from 'react-router'
import { Alert } from '@/components/ui/alert'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { DataTable, type Column } from '@/components/ui/DataTable'
import { EmptyState } from '@/components/ui/EmptyState'
import { SelectField } from '@/components/ui/select'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { stageTone } from '@/components/ui/status-tones'
import { formatDateTime, shortDay } from '@/lib/dates'
import { useSafetyProfile } from './queries'
import { isReEntryActive, toDayRanges } from './safety'
import { sprayBlockLabels, sprayBlockRule, stageLabels, type PlotSafetyProfile, type ProductWindow } from './types'

const productViews = [
  { value: 'all', label: 'All approved products' },
  { value: 'sprayable', label: 'Can spray today' },
  { value: 'blocked', label: 'Blocked today' },
] as const

/**
 * Component A's safety profile on the web (GET /api/plots/{id}/safety-profile): the same numbers
 * the Validation agent's rules V5–V7 are checked against, shown before anyone drafts a
 * prescription. Nothing is calculated here; the page only groups and words what the server
 * decided, so the console and the validator cannot disagree.
 */
export function SafetyProfilePanel({ plotId }: { plotId: string }) {
  const profile = useSafetyProfile(plotId)

  return (
    <section aria-labelledby="safety-heading" className="space-y-3">
      <h2 id="safety-heading" className="text-sm font-medium uppercase tracking-wide text-stone-500">
        Spray safety
      </h2>
      <AsyncBoundary isPending={profile.isPending} error={profile.error} onRetry={profile.refetch} label="Loading safety profile">
        {profile.data && <ProfileBody profile={profile.data} />}
      </AsyncBoundary>
    </section>
  )
}

function ProfileBody({ profile }: { profile: PlotSafetyProfile }) {
  const [params, setParams] = useSearchParams()

  if (!profile.cropCycleId || !profile.harvestDate) {
    return (
      <EmptyState
        title="Nothing growing"
        description="Pre-harvest intervals are counted back from a harvest date, so a plot has a spray profile only while a crop is growing."
      />
    )
  }

  const view = productViews.find((v) => v.value === params.get('products')) ?? productViews[0]
  const windows = profile.productWindows.filter((w) =>
    view.value === 'sprayable' ? w.canSprayToday : view.value === 'blocked' ? !w.canSprayToday : true,
  )
  const sprayable = profile.productWindows.filter((w) => w.canSprayToday).length
  const blockedRanges = toDayRanges(profile.phiBlockedSprayDates)
  const reEntryActive = isReEntryActive(profile.reEntryClearAtUtc)

  const setView = (value: string) => {
    const next = new URLSearchParams(params)
    if (value === 'all') next.delete('products')
    else next.set('products', value)
    setParams(next, { replace: true })
  }

  const columns: Column<ProductWindow>[] = [
    {
      key: 'product',
      header: 'Product',
      render: (w) => (
        <div>
          <p className="flex flex-wrap items-center gap-1.5 font-medium text-stone-900">
            {w.productName}
            {w.isRestricted && <StatusBadge label="Restricted" tone="warning" />}
          </p>
          <p className="text-xs text-stone-500">
            {w.activeIngredientName}
            {w.resistanceGroup && ` · group ${w.resistanceGroup}`}
          </p>
        </div>
      ),
    },
    {
      key: 'today',
      header: 'Today',
      render: (w) => (
        <div className="space-y-1">
          <StatusBadge label={sprayBlockLabels[w.blockedReason]} tone={w.canSprayToday ? 'done' : 'danger'} className="whitespace-nowrap" />
          {w.blockedExplanation && (
            <p className="max-w-sm text-xs text-stone-600">
              {sprayBlockRule[w.blockedReason] && <span className="font-medium text-stone-800">Rule {sprayBlockRule[w.blockedReason]}. </span>}
              {w.blockedExplanation}
            </p>
          )}
        </div>
      ),
    },
    {
      key: 'uses',
      header: 'Uses',
      numeric: true,
      render: (w) => <span className="whitespace-nowrap">{`${w.applicationsUsed} of ${w.maxApplicationsPerCycle}`}</span>,
    },
    { key: 'phi', header: 'PHI', numeric: true, secondary: true, render: (w) => <span className="whitespace-nowrap">{w.preHarvestIntervalDays} d</span> },
    { key: 'lastSafe', header: 'Last safe spray', secondary: true, render: (w) => <span className="whitespace-nowrap">{shortDay(w.lastSafeSprayDate)}</span> },
    {
      key: 'next',
      header: 'Next allowed',
      secondary: true,
      render: (w) => (w.earliestNextApplication ? shortDay(w.earliestNextApplication) : '—'),
    },
  ]

  return (
    <div className="space-y-4">
      <dl className="grid grid-cols-2 gap-3 rounded-lg border border-stone-200 bg-white p-4 text-sm sm:grid-cols-4">
        <div>
          <dt className="text-stone-500">Crop</dt>
          <dd className="flex flex-wrap items-center gap-1.5 font-medium text-stone-900">
            {profile.cropName}
            {profile.stage && <StatusBadge label={stageLabels[profile.stage]} tone={stageTone[profile.stage] ?? 'neutral'} />}
          </dd>
        </div>
        <div>
          <dt className="text-stone-500">Harvest</dt>
          <dd className="font-medium text-stone-900">{shortDay(profile.harvestDate)}</dd>
        </div>
        <div>
          <dt className="text-stone-500">Days to harvest</dt>
          <dd className="font-medium text-stone-900">
            {profile.daysToHarvest !== null && profile.daysToHarvest < 0 ? `${Math.abs(profile.daysToHarvest)} overdue` : profile.daysToHarvest}
          </dd>
        </div>
        <div>
          <dt className="text-stone-500">Sprayable today</dt>
          <dd className="font-medium text-stone-900">
            {sprayable} of {profile.productWindows.length}
          </dd>
        </div>
      </dl>

      {reEntryActive ? (
        <Alert tone="warning" title="Keep out of the field">
          A recent spray is still inside its re-entry interval. Do not enter without protective clothing until{' '}
          <strong>{formatDateTime(profile.reEntryClearAtUtc!)}</strong>.
        </Alert>
      ) : (
        <Alert tone="success">No re-entry restriction: the field is safe to work in.</Alert>
      )}

      <div className="rounded-lg border border-stone-200 bg-white p-4">
        <h3 className="font-medium text-stone-900">Days no product may be sprayed</h3>
        {blockedRanges.length === 0 ? (
          <p className="mt-1 text-sm text-stone-600">
            Every day until harvest leaves at least one approved product's pre-harvest interval clear.
          </p>
        ) : (
          <>
            <p className="mt-1 text-sm text-stone-600">
              Spraying on these days would leave less than the shortest pre-harvest interval before picking on{' '}
              {shortDay(profile.harvestDate)}.
            </p>
            <ul className="mt-2 flex flex-wrap gap-2" aria-label="PHI-blocked days">
              {blockedRanges.map((range) => (
                <li key={range.from} className="rounded-md bg-red-50 px-2 py-1 text-sm text-red-800 ring-1 ring-inset ring-red-200">
                  {range.from === range.to ? shortDay(range.from) : `${shortDay(range.from)} – ${shortDay(range.to)}`}
                  <span className="text-red-600"> · {range.days} day{range.days === 1 ? '' : 's'}</span>
                </li>
              ))}
            </ul>
          </>
        )}
      </div>

      <div className="space-y-2">
        <div className="max-w-xs">
          <SelectField label="Products" value={view.value} onChange={(event) => setView(event.target.value)}>
            {productViews.map((v) => (
              <option key={v.value} value={v.value}>
                {v.label}
              </option>
            ))}
          </SelectField>
        </div>
        {profile.productWindows.length === 0 ? (
          <EmptyState
            title={`No products approved for ${profile.cropName}`}
            description="The co-op administrator approves products per crop in the rules table; until then nothing may be prescribed here."
          />
        ) : windows.length === 0 ? (
          <EmptyState title={`Nothing under “${view.label}”`} description="Try another view." />
        ) : (
          <DataTable caption={`Approved products on ${profile.plotCode} today`} columns={columns} rows={windows} rowKey={(w) => w.productId} />
        )}
      </div>

      <p className="text-xs text-stone-500">
        From the rules table and this crop's spray record, as of {formatDateTime(profile.generatedAtUtc)}.
      </p>
    </div>
  )
}
