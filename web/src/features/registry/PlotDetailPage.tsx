import { Link, useParams } from 'react-router'
import { PageHeader } from '@/components/layout/PageHeader'
import { TileMap } from '@/components/map/TileMap'
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
    <div className="space-y-4 pb-4">
      <nav className="pt-4 text-sm">
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
            <PageHeader
              className="pt-1"
              title={
                <>
                  {plot.data.plotCode}
                  {plot.data.name && <span className="font-normal text-stone-600"> · {plot.data.name}</span>}
                </>
              }
              meta={<StatusBadge label={plot.data.status} tone={plotStatusTone[plot.data.status]} className="text-sm" />}
              description={
                <>
                  {plot.data.farmName}
                  {farm.data && `, ${farm.data.districtName}`} · {plot.data.areaHectares.toFixed(3)} ha · {soilLabels[plot.data.soilType]} ·{' '}
                  {plot.data.latitude.toFixed(4)}, {plot.data.longitude.toFixed(4)}
                </>
              }
            />

            <div className="grid items-start gap-4 xl:grid-cols-12">
              <div className="space-y-4 xl:col-span-4">
                <div className="space-y-2">
                  <h2 className="font-display text-xl font-semibold text-stone-900">Crop cycle</h2>
                  <CropCyclePanel plot={plot.data} canEdit={canEdit} />
                </div>
                <TileMap
                  label={`Map: plot ${plot.data.plotCode}`}
                  height={220}
                  pins={[{ id: plot.data.id, position: { lat: plot.data.latitude, lng: plot.data.longitude }, label: `Plot ${plot.data.plotCode}, registered centre`, tone: 'active' }]}
                />
              </div>
              <div className="xl:col-span-8">
                <SafetyProfilePanel plotId={plotId} />
              </div>
            </div>

            <TreatmentHistory plotId={plotId} />
          </>
        )}
      </AsyncBoundary>
    </div>
  )
}
