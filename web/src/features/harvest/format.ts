/** "2,400 kg". */
export function formatKg(kg: number): string {
  return `${kg.toLocaleString('en-US', { maximumFractionDigits: 1 })} kg`
}

/**
 * "+4.2%", "−10.4%" (a true minus sign), or "10.2%" when the sign means nothing (an error size).
 * An em dash when there is nothing to compare.
 */
export function formatPercent(value: number | null | undefined, signed = true): string {
  if (value == null) return '—'
  const magnitude = `${Math.abs(value).toLocaleString('en-US', { maximumFractionDigits: 1 })}%`
  if (!signed || value === 0) return magnitude
  return value > 0 ? `+${magnitude}` : `−${magnitude}`
}
