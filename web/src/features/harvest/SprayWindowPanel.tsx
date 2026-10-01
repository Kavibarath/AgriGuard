import { Panel, PanelHeader } from '@/components/layout/PageHeader'
import { AlertTriangle, CloudRain, Droplet, Leaf, Sun } from '@/components/icons'
import { Alert } from '@/components/ui/alert'
import { AsyncBoundary } from '@/components/ui/AsyncBoundary'
import { weekdayDay } from '@/lib/dates'
import { cn } from '@/lib/utils'
import { useSprayWindow } from './queries'
import type { SprayDay } from './types'

/**
 * The coming week at one plot, judged exactly as rule V8 judges a spray day: which days suit
 * spraying and, for each that does not, why. The weather glyphs in the corner drift gently unless
 * the reader has asked for reduced motion.
 */
export function SprayWindowPanel({ plotId, context }: { plotId: string | undefined; context?: string }) {
  const forecast = useSprayWindow(plotId)

  return (
    <Panel raised aria-labelledby="spray-heading" className="relative overflow-hidden">
      <FloatingWeather />
      <PanelHeader
        id="spray-heading"
        title="Spray weather"
        description={forecast.data ? `${forecast.data.plotCode}${context ? ` — ${context}` : ''}` : (context ?? 'The coming week at the plot')}
      />
      {!plotId ? (
        <p className="text-sm text-stone-600">No plot to forecast yet: spray weather appears once a crop is growing.</p>
      ) : (
        <AsyncBoundary isPending={forecast.isPending} error={forecast.error} onRetry={forecast.refetch} label="Loading the forecast">
          {forecast.data &&
            (!forecast.data.forecastAvailable ? (
              <Alert tone="warning" title="No forecast right now">
                The weather service did not answer, so no day is judged. Rule V8 is reported as not checked until it does.
              </Alert>
            ) : (
              <div className="space-y-3">
                <SuitableCount days={forecast.data.days} />
                <ol aria-label="Spray suitability by day" className="divide-y divide-border-subtle rounded-lg border border-border-subtle">
                  {forecast.data.days.map((day) => (
                    <DayRow key={day.date} day={day} />
                  ))}
                </ol>
                {(forecast.data.recentRainMm !== null || forecast.data.recentHumidityPercent !== null) && (
                  <p className="text-xs text-stone-600">
                    Last 48 hours at the plot: {forecast.data.recentRainMm ?? 0} mm rain, {forecast.data.recentHumidityPercent ?? '—'}% humidity. Wet, humid spells favour fungal disease.
                  </p>
                )}
                <p className="text-xs text-stone-600">
                  A day suits spraying below {forecast.data.thresholds.maxRainProbabilityPercent}% rain chance, {forecast.data.thresholds.maxWindSpeedKph} km/h wind and{' '}
                  {forecast.data.thresholds.maxTemperatureC} °C, with {forecast.data.rainfastHours} dry hours after.
                </p>
              </div>
            ))}
        </AsyncBoundary>
      )}
    </Panel>
  )
}

function DayRow({ day }: { day: SprayDay }) {
  const rainy = day.problems.some((p) => /rain/i.test(p))
  const Icon = day.suitable ? Sun : rainy ? CloudRain : AlertTriangle
  return (
    <li
      title={day.problems.join('; ') || undefined}
      className={cn('grid grid-cols-[3.75rem_1.125rem_1fr_auto] items-center gap-2 px-3 py-2 text-sm', !day.suitable && 'bg-surface-sunken/70')}
    >
      <span className="font-medium whitespace-nowrap text-stone-900">{compactDay(day.date)}</span>
      <Icon size={18} className={day.suitable ? 'text-warning' : rainy ? 'text-info' : 'text-warning'} />
      <span className={cn('min-w-0 truncate', day.suitable ? 'text-success-800' : 'text-stone-800')}>
        {day.suitable ? (
          <span className="font-medium">Suits spraying</span>
        ) : (
          <>
            {shortReason(day.problems)}
            <span className="sr-only">: {day.problems.join('; ')}</span>
          </>
        )}
      </span>
      <span className="inline-flex items-center justify-end gap-0.5 text-xs whitespace-nowrap text-stone-600 tabular-nums">
        <Droplet size={12} className="text-info" />
        {day.rainProbabilityPercent}%<span className="sr-only"> chance of rain, wind {day.windSpeedKph} km/h</span>
      </span>
    </li>
  )
}

/** "Wed 30": a week of days needs no month. */
function compactDay(day: string): string {
  const [y, m, d] = day.split('-').map(Number)
  const date = new Date(y, m - 1, d)
  return `${date.toLocaleDateString('en-GB', { weekday: 'short' })} ${d}`
}

/** "2 of 7 days suit spraying, first Wed 30 Sep": the answer before the detail. */
function SuitableCount({ days }: { days: SprayDay[] }) {
  const suitable = days.filter((d) => d.suitable)
  return (
    <p className="text-sm text-stone-800">
      <span className="font-display text-2xl font-semibold text-stone-900 tabular-nums">{suitable.length}</span>
      <span className="text-stone-700"> of {days.length} days suit spraying</span>
      {suitable[0] ? <span className="text-stone-700">, first {weekdayDay(suitable[0].date)}.</span> : <span className="text-stone-700">. Hold off: no dry, calm day ahead.</span>}
    </p>
  )
}

/** The rule's reason in two words; the full sentence stays in the tooltip and for screen readers. */
function shortReason(problems: string[]): string {
  const first = problems[0] ?? ''
  if (/rain/i.test(first)) return 'Rain likely'
  if (/wind/i.test(first)) return 'Too windy'
  if (/hot|temperature/i.test(first)) return 'Too hot'
  return first || 'Does not suit spraying'
}

/** Sun, a drop and a leaf drifting in the panel's corner: decoration only, and still under reduced motion. */
function FloatingWeather() {
  return (
    <div aria-hidden="true" className="pointer-events-none absolute top-3 right-4 flex gap-2">
      <span className="drift text-warning/70">
        <Sun size={20} />
      </span>
      <span className="drift-slow text-info/70">
        <Droplet size={18} />
      </span>
      <span className="drift text-brand-400" style={{ animationDelay: '-5s' }}>
        <Leaf size={18} />
      </span>
    </div>
  )
}
