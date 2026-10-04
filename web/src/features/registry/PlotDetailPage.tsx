import { Link, useParams } from 'react-router'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { StatusBadge } from '@/components/ui/StatusBadge'
import { plotStatusTone } from '@/components/ui/status-tones'
import { useCurrentUser } from '@/features/auth/auth-store'
import { CropCyclePanel } from './CropCyclePanel'
import { useFarm, usePlot } from './queries'
import { SafetyProfilePanel } from './SafetyProfilePanel'
import { TreatmentHistory } from './TreatmentHistory'
import { soilLabels } from './types'

/**
 * One plot (§7 /plots/:id): its crop cycle, what may be sprayed on it today and why, and every
 * spray it has had. The same scoping as the farm page — the API answers 403 for a plot the
 * caller may not see, and only the owning farmer or an administrator gets the edit controls.
 */
export function PlotDetailPage() {
  const { plotId = '' } = useParams()
  const user = useCurrentUser()
  const plot = usePlot(plotId)
  const farm = useFarm(plot.data?.farmId ?? '')

  const canEdit = user?.role === 'CoopAdministrator' || (farm.data ? farm.data.farmerId === user?.id : false)

  return (
    <main className="mx-auto max-w-6xl space-y-6 p-6">
      <nav className="text-sm">
        {plot.data ? (
          <Link to={`/farms/${plot.data.farmId}`} className="text-brand-700 hover:underline">
            ← {plot.data.farmName}
          </Link>
        ) : (
          <Link to="/farms" className="text-brand-700 hover:underline">
            ← All farms
          </Link>
        )}
      </nav>

      <AsyncBoundary isPending={plot.isPending} error={plot.error} onRetry={plot.refetch} label="Loading plot">
        {plot.data && (
          <>
            <header className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h1 className="text-2xl font-semibold text-stone-900">
                  {plot.data.plotCode}
                  {plot.data.name && <span className="font-normal text-stone-600"> · {plot.data.name}</span>}
                </h1>
                <p className="text-sm text-stone-600">
                  {plot.data.farmName}
                  {farm.data && `, ${farm.data.districtName}`} · {plot.data.areaHectares.toFixed(3)} ha ·{' '}
                  {soilLabels[plot.data.soilType]} · {plot.data.latitude.toFixed(4)}, {plot.data.longitude.toFixed(4)}
                </p>
              </div>
              <StatusBadge label={plot.data.status} tone={plotStatusTone[plot.data.status]} />
            </header>

            <div className="grid gap-6 lg:grid-cols-[2fr_3fr]">
              <div className="space-y-2">
                <h2 className="text-sm font-medium uppercase tracking-wide text-stone-500">Crop cycle</h2>
                <CropCyclePanel plot={plot.data} canEdit={canEdit} />
              </div>
              <SafetyProfilePanel plotId={plotId} />
            </div>

            <TreatmentHistory plotId={plotId} />
          </>
        )}
      </AsyncBoundary>
    </main>
  )
}
