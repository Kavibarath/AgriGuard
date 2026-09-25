import { cn } from '@/lib/utils'

export type Tone = 'neutral' | 'active' | 'warning' | 'done' | 'danger'

const tones: Record<Tone, string> = {
  neutral: 'bg-stone-100 text-stone-700 ring-stone-200',
  active: 'bg-brand-50 text-brand-700 ring-brand-100',
  warning: 'bg-amber-50 text-amber-800 ring-amber-200',
  done: 'bg-sky-50 text-sky-800 ring-sky-200',
  danger: 'bg-red-50 text-red-700 ring-red-200',
}

/**
 * Status is never colour alone — the label carries the meaning, the colour reinforces it.
 * Colour-blind users and grayscale printouts read the same information.
 */
export function StatusBadge({ label, tone = 'neutral', className }: { label: string; tone?: Tone; className?: string }) {
  return (
    <span className={cn('inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset', tones[tone], className)}>
      {label}
    </span>
  )
}
