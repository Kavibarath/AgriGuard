/** "850 ms" under a second, "8.3 s" above: step and tool timings in the console. */
export function formatDuration(ms: number | null): string {
  if (ms === null) return ''
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`
}
