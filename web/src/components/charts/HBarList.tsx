import { useId } from 'react'

export interface HBarItem {
  key: string
  label: string
  value: number
  /** The value as a person reads it, e.g. "54%" or "−10.4%". Always shown next to the bar. */
  display: string
  /** A note under the label, e.g. "3 confirmed cases". */
  note?: string
}

/**
 * Horizontal bars with direct labels: one row per item, value written beside its bar, so the
 * numbers never depend on colour or hovering.
 *
 * `diverging` draws signed values either side of a centre line (above target in blue, below in
 * red); otherwise every bar grows from the left in one hue.
 */
export function HBarList({ items, caption, diverging = false, max }: { items: HBarItem[]; caption: string; diverging?: boolean; max?: number }) {
  const limit = max ?? Math.max(1e-9, ...items.map((i) => Math.abs(i.value)))
  const captionId = useId()

  return (
    <figure className="space-y-2" aria-labelledby={captionId}>
      <figcaption id={captionId} className="text-sm font-medium text-stone-800">
        {caption}
      </figcaption>
      <ul className="space-y-2.5">
        {items.map((item) => {
          const share = Math.min(1, Math.abs(item.value) / limit)
          const positive = item.value >= 0
          return (
            <li key={item.key} className="grid grid-cols-[minmax(7rem,11rem)_1fr_4.5rem] items-center gap-3 text-sm">
              <div className="min-w-0">
                <p className="truncate text-stone-900">{item.label}</p>
                {item.note && <p className="truncate text-xs text-stone-500">{item.note}</p>}
              </div>
              <div className="relative h-3" aria-hidden="true">
                {diverging ? (
                  <>
                    <span className="absolute inset-y-0 left-1/2 w-px bg-stone-300" />
                    <span
                      className="absolute inset-y-0 rounded-sm"
                      style={{
                        width: `${share * 50}%`,
                        left: positive ? '50%' : `${50 - share * 50}%`,
                        background: positive ? 'var(--color-diverge-up)' : 'var(--color-diverge-down)',
                      }}
                    />
                  </>
                ) : (
                  <>
                    <span className="absolute inset-0 rounded-sm bg-stone-100" />
                    <span className="absolute inset-y-0 left-0 rounded-sm" style={{ width: `${share * 100}%`, background: 'var(--color-series-1)' }} />
                  </>
                )}
              </div>
              <p className="text-right tabular-nums text-stone-900">{item.display}</p>
            </li>
          )
        })}
      </ul>
    </figure>
  )
}
