import { Basket, XCircle } from '@/components/icons'
import { addDays } from '@/lib/dates'
import { cn } from '@/lib/utils'

const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']

/** Local date from "yyyy-MM-dd", so no time zone moves a farm day. */
function parse(day: string): Date {
  const [y, m, d] = day.split('-').map(Number)
  return new Date(y, m - 1, d)
}

/** Monday on or before the day. */
function mondayOf(day: string): string {
  const offset = (parse(day).getDay() + 6) % 7
  return addDays(day, -offset)
}

/**
 * The run-up to harvest as a calendar: days on which no approved product may be sprayed are
 * struck through in red with a cross, the harvest day carries a basket, today is outlined. It
 * repeats the ranges listed above it for sighted readers, so it is hidden from screen readers.
 */
export function PhiCalendar({ blockedDays, harvestDate, today }: { blockedDays: string[]; harvestDate: string; today: string }) {
  const blocked = new Set(blockedDays)
  const firstBlocked = [...blocked].sort()[0] ?? harvestDate
  // Four weeks before harvest, earlier if the blocked run starts sooner, never more than eight weeks.
  let start = mondayOf([addDays(harvestDate, -27), firstBlocked].sort()[0])
  const latestStart = mondayOf(addDays(harvestDate, -55))
  if (start < latestStart) start = latestStart
  const end = addDays(mondayOf(harvestDate), 6)

  const days: string[] = []
  for (let d = start; d <= end; d = addDays(d, 1)) days.push(d)

  return (
    <div aria-hidden="true" className="mt-3">
      <div className="grid grid-cols-7 gap-1 text-center text-xs font-medium text-stone-600">
        {weekdays.map((w) => (
          <span key={w}>{w}</span>
        ))}
      </div>
      <div className="mt-1 grid grid-cols-7 gap-1">
        {days.map((day) => {
          const isBlocked = blocked.has(day)
          const isHarvest = day === harvestDate
          const isToday = day === today
          const isPast = day < today
          const date = parse(day)
          return (
            <div
              key={day}
              className={cn(
                'relative flex h-10 flex-col items-center justify-center rounded-md text-xs tabular-nums',
                isBlocked && 'bg-[repeating-linear-gradient(135deg,var(--color-danger-50)_0_6px,#fbe3e0_6px_9px)] font-semibold text-danger-800 ring-1 ring-danger-200 ring-inset',
                isHarvest && 'bg-earth-100 font-semibold text-earth-800 ring-1 ring-earth-300 ring-inset',
                !isBlocked && !isHarvest && (isPast ? 'text-stone-500' : 'bg-surface-sunken text-stone-700'),
                isToday && 'outline-2 outline-offset-1 outline-brand-600',
              )}
            >
              <span>{date.getDate() === 1 ? date.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' }) : date.getDate()}</span>
              {isBlocked && <XCircle size={12} className="text-danger" />}
              {isHarvest && <Basket size={12} className="text-earth-600" />}
            </div>
          )
        })}
      </div>
      <p className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-stone-700">
        <span className="inline-flex items-center gap-1">
          <XCircle size={14} className="text-danger" /> No product may be sprayed
        </span>
        <span className="inline-flex items-center gap-1">
          <Basket size={14} className="text-earth-600" /> Planned harvest
        </span>
        <span className="inline-flex items-center gap-1">
          <span className="inline-block size-3 rounded-sm outline-2 outline-brand-600" /> Today
        </span>
      </p>
    </div>
  )
}
