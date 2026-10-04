import { cn } from '@/lib/utils'

/**
 * Furrows: rows of a ploughed field seen from above, as gently curving lines that drift sideways
 * (`.furrow-drift`, stopped under reduced motion). Pure decoration, hidden from assistive tech.
 * The pattern is drawn twice side by side so the drift loops without a seam.
 */
export function Furrows({ tone = 'light', className }: { tone?: 'light' | 'dark'; className?: string }) {
  const stroke = tone === 'light' ? 'var(--color-brand-300)' : 'var(--color-brand-400)'
  const rows = Array.from({ length: 14 }, (_, i) => i)
  const pattern = (
    <svg viewBox="0 0 1200 600" preserveAspectRatio="none" className="h-full w-1/2 shrink-0">
      {rows.map((i) => {
        const y = 20 + i * 42
        // Two whole waves per tile, leaving and arriving at the same height and angle, so tiles
        // join without a kink.
        return <path key={i} d={`M0 ${y} C150 ${y - 22}, 450 ${y + 22}, 600 ${y} C750 ${y - 22}, 1050 ${y + 22}, 1200 ${y}`} fill="none" stroke={stroke} strokeWidth="1.25" strokeLinecap="round" />
      })}
    </svg>
  )
  return (
    <div aria-hidden="true" className={cn('pointer-events-none absolute inset-0 overflow-hidden', className)}>
      <div className="furrow-drift flex h-full w-[200%]">
        {pattern}
        {pattern}
      </div>
    </div>
  )
}

const leaves: { left: string; top: string; size: number; duration: number; delay: number; rotate: number }[] = [
  { left: '3%', top: '90%', size: 28, duration: 15, delay: 0, rotate: -20 },
  { left: '88%', top: '12%', size: 22, duration: 17, delay: -4, rotate: 30 },
  { left: '72%', top: '70%', size: 34, duration: 19, delay: -9, rotate: 160 },
  { left: '16%', top: '78%', size: 20, duration: 13, delay: -6, rotate: 80 },
  { left: '58%', top: '4%', size: 18, duration: 16, delay: -2, rotate: -60 },
  { left: '94%', top: '52%', size: 24, duration: 18, delay: -11, rotate: 210 },
]

/** A few leaves drifting in the margins of a section (`.leaf-float`, still under reduced motion). */
export function FloatingLeaves({ tone = 'light', className }: { tone?: 'light' | 'dark'; className?: string }) {
  const fill = tone === 'light' ? 'var(--color-brand-200)' : 'var(--color-brand-500)'
  const vein = tone === 'light' ? 'var(--color-brand-400)' : 'var(--color-brand-300)'
  return (
    <div aria-hidden="true" className={cn('pointer-events-none absolute inset-0 overflow-hidden', className)}>
      {leaves.map((leaf, i) => (
        <span
          key={i}
          className="leaf-float absolute block"
          style={{ left: leaf.left, top: leaf.top, ['--leaf-duration' as string]: `${leaf.duration}s`, ['--leaf-delay' as string]: `${leaf.delay}s` }}
        >
          <svg width={leaf.size} height={leaf.size} viewBox="0 0 24 24" style={{ transform: `rotate(${leaf.rotate}deg)` }}>
            <path d="M4 20c0-9 6-15 16-16 0 10-6 16-16 16Z" fill={fill} opacity="0.85" />
            <path d="M4 20 15 9" stroke={vein} strokeWidth="1.2" strokeLinecap="round" />
          </svg>
        </span>
      ))}
    </div>
  )
}
