import { Fields, Sprout } from '@/components/icons'
import { StatTile } from '@/components/ui/StatTile'
import type { Plot } from './types'

/** The registry in four figures: farms, plots, hectares and plots with a crop growing. */
export function RegistryTiles({ farmCount, plots }: { farmCount: number | undefined; plots: Plot[] | undefined }) {
  const growing = plots?.filter((p) => p.activeCycle)
  const hectares = plots?.reduce((sum, p) => sum + p.areaHectares, 0)
  const crops = growing ? new Set(growing.map((p) => p.activeCycle!.cropName)) : undefined
  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <StatTile label="Farms" value={farmCount ?? '—'} detail="Registered in your view" icon={<Fields />} />
      <StatTile label="Plots" value={plots?.length ?? '—'} detail="Each with a centre and a soil type" icon={<Fields />} />
      <StatTile label="Area under plots" value={hectares !== undefined ? `${hectares.toFixed(2)} ha` : '—'} detail="Drives every dose calculation" />
      <StatTile
        label="Growing now"
        value={growing?.length ?? '—'}
        detail={crops && crops.size > 0 ? [...crops].join(', ') : 'No crop sown yet'}
        icon={<Sprout />}
      />
    </div>
  )
}
