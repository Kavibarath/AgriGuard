import { unitLabels, type ProductUnit } from '@/features/inventory/types'

/** "0.6 L/ha" — or "0.6 per ha" while the unit is unknown. */
export function formatDose(dose: number, unit: ProductUnit | null | undefined): string {
  return unit ? `${dose} ${unitLabels[unit]}/ha` : `${dose} per ha`
}

/** "0.48 L" — or the bare number while the unit is unknown. */
export function formatAmount(quantity: number, unit: ProductUnit | null | undefined): string {
  return unit ? `${quantity} ${unitLabels[unit]}` : String(quantity)
}

/**
 * yyyy-MM-dd in the viewer's own time zone. The API sends UTC timestamps, and slicing the string
 * gave yesterday's date for anything reported in Sri Lanka before 05:30.
 */
export function localDate(iso: string): string {
  const d = new Date(iso)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/** "850 ms" under a second, "8.3 s" above: step and tool timings in the console. */
export function formatDuration(ms: number | null): string {
  if (ms === null) return ''
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`
}
