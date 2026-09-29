import type { PinTone } from '@/components/map/TileMap'
import type { CaseSeverity } from '@/features/agent-runs/types'

/** Pin colours follow the severity badges: the warm colours are the alarm. */
export const severityPinTone: Record<CaseSeverity, PinTone> = {
  Low: 'neutral',
  Medium: 'active',
  High: 'warning',
  Critical: 'danger',
}

/** "3 h", "2 days": how long a report waited on the phone before it reached the server. */
export function formatWait(fromIso: string, toIso: string): string {
  const minutes = Math.max(0, Math.round((new Date(toIso).getTime() - new Date(fromIso).getTime()) / 60_000))
  if (minutes < 60) return `${minutes} min`
  const hours = Math.round(minutes / 60)
  if (hours < 48) return `${hours} h`
  return `${Math.round(hours / 24)} days`
}
