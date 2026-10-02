import { useEffect, useRef, useState, type CSSProperties, type ElementType, type ReactNode } from 'react'
import { cn } from '@/lib/utils'

/**
 * Rises and fades its children in the first time they scroll into view (`.reveal` in index.css).
 * The motion lives only in CSS under `prefers-reduced-motion: no-preference`; with reduced motion,
 * or where IntersectionObserver is missing (old browsers, tests), the content is simply there.
 */
export function Reveal({
  as: Tag = 'div',
  delay = 0,
  className,
  style,
  children,
}: {
  as?: ElementType
  /** Milliseconds, for staggering siblings. */
  delay?: number
  className?: string
  style?: CSSProperties
  children: ReactNode
}) {
  const ref = useRef<HTMLElement>(null)
  const [visible, setVisible] = useState(() => typeof IntersectionObserver === 'undefined')

  useEffect(() => {
    const el = ref.current
    if (!el || visible) return
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) {
          setVisible(true)
          observer.disconnect()
        }
      },
      // A little before it is fully on screen, so the motion is done by the time it is read.
      { rootMargin: '0px 0px -8% 0px', threshold: 0.08 },
    )
    observer.observe(el)
    return () => observer.disconnect()
  }, [visible])

  return (
    <Tag ref={ref} className={cn('reveal', visible && 'is-visible', className)} style={{ ...style, '--reveal-delay': `${delay}ms` } as CSSProperties}>
      {children}
    </Tag>
  )
}
