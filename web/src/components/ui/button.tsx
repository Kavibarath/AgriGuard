import type { ButtonHTMLAttributes } from 'react'
import { cn } from '@/lib/utils'

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'
type Size = 'sm' | 'md' | 'lg'

/** A hairline of light along the top edge, and a canopy-tinted drop: lit from above. */
const lit = 'shadow-[inset_0_1px_0_rgb(255_255_255/0.22),0_1px_2px_rgb(19_48_37/0.18),0_4px_12px_rgb(19_48_37/0.12)]'

const variants: Record<Variant, string> = {
  // A canopy gradient from brand-600 down: white on its lightest point is 6.7:1, where 500 only
  // just reaches 4.5:1.
  primary: `bg-gradient-to-b from-brand-600 to-brand-700 text-white ${lit} hover:from-brand-700 hover:to-brand-800 active:from-brand-800 active:to-brand-800`,
  secondary:
    'border border-border-strong bg-gradient-to-b from-surface-card to-surface-sunken text-stone-900 shadow-[inset_0_1px_0_rgb(255_255_255/1),0_1px_2px_rgb(19_48_37/0.06)] hover:border-stone-400 hover:to-surface-inset',
  ghost: 'text-stone-700 hover:bg-surface-inset hover:text-stone-900',
  danger: `bg-gradient-to-b from-danger to-danger-800 text-white ${lit} hover:from-danger-800`,
}

const sizes: Record<Size, string> = {
  sm: 'h-8 gap-1.5 px-3 text-sm',
  md: 'h-9 gap-2 px-3.5 text-sm',
  lg: 'h-11 gap-2 px-5 text-[15px]',
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
  /** Shows a spinner and disables the button; `children` stays visible so the width does not jump. */
  loading?: boolean
}

/**
 * The one button. Pressing it moves it down a pixel and drops its shadow (80ms), unless the
 * person has asked for reduced motion. Focus shows the global 2px brand outline.
 */
export function Button({ variant = 'primary', size = 'md', loading = false, className, disabled, children, ...props }: ButtonProps) {
  return (
    <button
      type="button"
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={cn(
        'press inline-flex shrink-0 items-center justify-center whitespace-nowrap rounded-md font-medium',
        'disabled:cursor-not-allowed disabled:opacity-55 disabled:shadow-none',
        sizes[size],
        variants[variant],
        className,
      )}
      {...props}
    >
      {loading && (
        <span aria-hidden="true" className="motion-essential size-4 animate-spin rounded-full border-2 border-current border-t-transparent" />
      )}
      {children}
    </button>
  )
}
