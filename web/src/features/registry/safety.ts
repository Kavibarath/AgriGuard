import { addDays } from '@/lib/dates'

export interface DayRange {
  from: string
  to: string
  days: number
}

/**
 * Collapses the API's list of PHI-blocked days into runs of consecutive days. The list can hold
 * a hundred dates for a long crop; a farmer reads "12–19 Oct" far more easily than eight chips.
 * Dates are yyyy-MM-dd, so string order is date order.
 */
export function toDayRanges(days: string[]): DayRange[] {
  const sorted = [...new Set(days)].sort()
  const ranges: DayRange[] = []

  for (const day of sorted) {
    const last = ranges.at(-1)
    if (last && addDays(last.to, 1) === day) {
      last.to = day
      last.days += 1
    } else {
      ranges.push({ from: day, to: day, days: 1 })
    }
  }

  return ranges
}

/**
 * The server sends a re-entry time only while it is ahead, but a page left open can outlive it,
 * so the check is repeated against the browser's clock when the panel renders.
 */
export function isReEntryActive(reEntryClearAtUtc: string | null, now = Date.now()): boolean {
  return reEntryClearAtUtc !== null && new Date(reEntryClearAtUtc).getTime() > now
}
