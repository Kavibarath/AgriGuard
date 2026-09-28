/**
 * Calendar dates from the API ("2026-09-28", a DateOnly) are farm days, not instants: read them
 * as local dates so no time zone can shift them by one.
 */
function parseDay(day: string): Date {
  const [y, m, d] = day.split('-').map(Number)
  return new Date(y, m - 1, d)
}

/** "28 Sep". */
export function shortDay(day: string): string {
  return parseDay(day).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })
}

/** "Mon 28 Sep". */
export function weekdayDay(day: string): string {
  return parseDay(day).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'short' })
}

/** yyyy-MM-dd, `days` after the given yyyy-MM-dd. */
export function addDays(day: string, days: number): string {
  const d = parseDay(day)
  d.setDate(d.getDate() + days)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}
