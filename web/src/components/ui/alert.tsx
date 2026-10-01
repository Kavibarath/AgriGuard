import type { ReactNode } from 'react'
import { AlertTriangle, CheckCircle, InfoCircle, StopOctagon } from '@/components/icons'
import { cn } from '@/lib/utils'

type Tone = 'error' | 'warning' | 'info' | 'success'

const tones: Record<Tone, { box: string; icon: string; Icon: (p: { size?: number; className?: string }) => ReactNode }> = {
  error: { box: 'border-danger-200 bg-danger-50 text-danger-800', icon: 'text-danger', Icon: StopOctagon },
  warning: { box: 'border-warning-200 bg-warning-50 text-warning-800', icon: 'text-warning', Icon: AlertTriangle },
  info: { box: 'border-info-200 bg-info-50 text-info-800', icon: 'text-info', Icon: InfoCircle },
  success: { box: 'border-success-200 bg-success-50 text-success-800', icon: 'text-success', Icon: CheckCircle },
}

/**
 * A message in the flow of the page. The icon's shape says the kind (octagon, triangle, circle,
 * tick) as well as its colour, so a colour-blind reader tells an error from a note.
 */
export function Alert({ tone = 'info', title, children, className }: {
  tone?: Tone
  title?: string
  children?: ReactNode
  className?: string
}) {
  const { box, icon, Icon } = tones[tone]
  return (
    <div role={tone === 'error' ? 'alert' : 'status'} className={cn('flex gap-2.5 rounded-lg border px-3.5 py-2.5 text-sm', box, className)}>
      <Icon size={18} className={cn('mt-px shrink-0', icon)} />
      <div className="min-w-0">
        {title && <p className="font-semibold">{title}</p>}
        {children && <div className={cn(title && 'mt-0.5')}>{children}</div>}
      </div>
    </div>
  )
}
