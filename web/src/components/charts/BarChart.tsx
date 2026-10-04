import { useId, useState } from 'react'

export interface BarSeries {
  key: string
  label: string
  /** A chart series token, e.g. 'var(--color-series-1)'. Identity only: text never wears it. */
  color: string
}

export interface BarDatum {
  /** Axis label, e.g. "28 Sep". */
  label: string
  /** One value per series, in series order; stacked bottom to top. */
  values: number[]
}

const WIDTH = 640
const HEIGHT = 200
const MARGIN = { top: 12, right: 8, bottom: 24, left: 32 }
const RADIUS = 4

/** A bar whose top corners are rounded and whose base sits square on the baseline. */
function topRoundedBar(x: number, y: number, width: number, height: number) {
  const r = Math.min(RADIUS, width / 2, height)
  return `M${x},${y + height}V${y + r}Q${x},${y} ${x + r},${y}H${x + width - r}Q${x + width},${y} ${x + width},${y + r}V${y + height}Z`
}

/** 0 and up to four whole-number steps to a round top. Counts are never fractional. */
function ticksFor(max: number) {
  const step = Math.max(1, Math.ceil(max / 4))
  const top = Math.max(step, Math.ceil(max / step) * step)
  return Array.from({ length: top / step + 1 }, (_, i) => i * step)
}

/**
 * A stacked column chart of counts over a sequence (days, usually). Hand-rolled SVG: the app
 * needs two charts, not a charting library.
 *
 * Accessible by construction: a legend whenever there are two or more series, a hover and focus
 * tooltip per column, and the same numbers as a table for screen readers.
 */
export function BarChart({ data, series, caption, valueLabel = 'cases' }: { data: BarDatum[]; series: BarSeries[]; caption: string; valueLabel?: string }) {
  const [active, setActive] = useState<number | null>(null)
  const titleId = useId()

  const totals = data.map((d) => d.values.reduce((a, b) => a + b, 0))
  const ticks = ticksFor(Math.max(0, ...totals))
  const top = ticks[ticks.length - 1]
  const plotWidth = WIDTH - MARGIN.left - MARGIN.right
  const plotHeight = HEIGHT - MARGIN.top - MARGIN.bottom
  const band = plotWidth / Math.max(1, data.length)
  const barWidth = Math.max(2, Math.min(28, band - 2))
  const y = (value: number) => MARGIN.top + plotHeight - (value / top) * plotHeight
  // Label every column when there is room, otherwise about seven, counted back from the newest
  // so the latest day is always labelled.
  const labelEvery = Math.max(1, Math.ceil(data.length / 7))
  const labelled = (i: number) => (data.length - 1 - i) % labelEvery === 0

  return (
    <figure className="space-y-2" aria-labelledby={titleId}>
      <figcaption id={titleId} className="text-sm font-medium text-stone-800">
        {caption}
      </figcaption>
      {series.length > 1 && (
        <ul className="flex flex-wrap gap-4 text-xs text-stone-600" aria-label="Legend">
          {series.map((s) => (
            <li key={s.key} className="flex items-center gap-1.5">
              <span aria-hidden="true" className="inline-block size-2.5 rounded-sm" style={{ background: s.color }} />
              {s.label}
            </li>
          ))}
        </ul>
      )}
      <div className="relative">
        <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} className="h-auto w-full" role="img" aria-labelledby={titleId}>
          {ticks.map((t) => (
            <g key={t}>
              <line
                x1={MARGIN.left}
                x2={WIDTH - MARGIN.right}
                y1={y(t)}
                y2={y(t)}
                stroke={t === 0 ? 'var(--color-chart-axis)' : 'var(--color-chart-grid)'}
                strokeWidth={1}
              />
              <text x={MARGIN.left - 6} y={y(t)} dy="0.32em" textAnchor="end" className="fill-stone-600 text-xs tabular-nums">
                {t}
              </text>
            </g>
          ))}

          {data.map((d, i) => {
            const x = MARGIN.left + i * band + (band - barWidth) / 2
            let base = 0
            // The topmost non-zero segment gets the rounded end; the others stack square with a 2px gap.
            const topIndex = d.values.reduce((last, v, k) => (v > 0 ? k : last), -1)
            return (
              <g key={i}>
                {d.values.map((value, k) => {
                  if (value <= 0) return null
                  const y0 = y(base)
                  base += value
                  const y1 = y(base)
                  const height = Math.max(0, y0 - y1 - (k === topIndex ? 0 : 2))
                  return k === topIndex ? (
                    <path key={k} d={topRoundedBar(x, y1, barWidth, height)} fill={series[k].color} opacity={active === null || active === i ? 1 : 0.55} />
                  ) : (
                    <rect key={k} x={x} y={y1 + 2} width={barWidth} height={height} fill={series[k].color} opacity={active === null || active === i ? 1 : 0.55} />
                  )
                })}
                {labelled(i) && (
                  <text x={x + barWidth / 2} y={HEIGHT - 6} textAnchor="middle" className="fill-stone-600 text-xs">
                    {d.label}
                  </text>
                )}
                {/* The hit target is the whole column, bigger than the mark. */}
                <rect
                  x={MARGIN.left + i * band}
                  y={MARGIN.top}
                  width={band}
                  height={plotHeight}
                  fill="transparent"
                  onMouseEnter={() => setActive(i)}
                  onMouseLeave={() => setActive(null)}
                />
              </g>
            )
          })}
        </svg>

        {active !== null && (
          <div
            role="tooltip"
            className="pointer-events-none absolute top-0 z-10 -translate-x-1/2 rounded-md border border-border-subtle bg-surface-card px-2.5 py-1 shadow-lifted.5 text-xs shadow-md"
            style={{ left: `${((MARGIN.left + active * band + band / 2) / WIDTH) * 100}%` }}
          >
            <p className="font-medium text-stone-900">{data[active].label}</p>
            {series.map((s, k) => (
              <p key={s.key} className="flex items-center gap-1.5 text-stone-600">
                <span aria-hidden="true" className="inline-block size-2 rounded-sm" style={{ background: s.color }} />
                {s.label}: <span className="tabular-nums text-stone-900">{data[active].values[k]}</span>
              </p>
            ))}
          </div>
        )}
      </div>

      <table className="sr-only">
        <caption>{caption}</caption>
        <thead>
          <tr>
            <th scope="col">Day</th>
            {series.map((s) => (
              <th key={s.key} scope="col">
                {s.label} ({valueLabel})
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {data.map((d) => (
            <tr key={d.label}>
              <th scope="row">{d.label}</th>
              {d.values.map((v, k) => (
                <td key={series[k].key}>{v}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </figure>
  )
}
