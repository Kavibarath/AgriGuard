import type { ReactNode } from 'react'
import { AlertTriangle, CheckCircle, DashedCircle, Dot, XCircle } from '@/components/icons'
import { cn } from '@/lib/utils'

export type Tone = 'neutral' | 'active' | 'warning' | 'done' | 'danger'

const tones: Record<Tone, { chip: string; Icon: (p: { size?: number; className?: string }) => ReactNode }> = {
  neutral: { chip: 'bg-surface-inset text-stone-700 ring-border-strong', Icon: DashedCircle },
  active: { chip: 'bg-info-50 text-info-800 ring-info-200', Icon: Dot },
  warning: { chip: 'bg-warning-50 text-warning-800 ring-warning-200', Icon: AlertTriangle },
  done: { chip: 'bg-success-50 text-success-800 ring-success-200', Icon: CheckCircle },
  danger: { chip: 'bg-danger-50 text-danger-800 ring-danger-200', Icon: XCircle },
}

/**
 * Status is never colour alone: the label carries the meaning, the icon's shape repeats it
 * (tick, cross, triangle, clock, dashed ring) and the colour only reinforces it. Colour-blind
 * readers and grayscale printouts read the same information.
 */
export function StatusBadge({ label, tone = 'neutral', className }: { label: string; tone?: Tone; className?: string }) {
  const { chip, Icon } = tones[tone]
  return (
    <span className={cn('inline-flex items-center gap-1 whitespace-nowrap rounded-full py-0.5 pr-2 pl-1.5 font-sans text-xs font-medium ring-1 ring-inset', chip, className)}>
      <Icon size={14} className="shrink-0" />
      {label}
    </span>
  )
}
