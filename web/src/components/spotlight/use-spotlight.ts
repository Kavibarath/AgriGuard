import { useEffect, useRef, useState } from 'react'
import { usePrefersReducedMotion } from '@/lib/motion'

/** The spotlight's radius in CSS pixels. */
export const SPOTLIGHT_R = 260

/** How far the eased position moves towards the cursor each frame. */
const EASING = 0.1

/**
 * The cursor, eased. The raw position comes from `mousemove`; a `requestAnimationFrame` loop moves
 * the eased position a tenth of the way towards it each frame, so the spotlight trails the cursor
 * softly. With reduced motion there is no trailing: the spotlight sits exactly under the cursor.
 *
 * Touch screens have no cursor, so there the spotlight rests at `restAt` (fractions of the window),
 * and the reveal is still visible.
 */
export function useSmoothedCursor(restAt: { x: number; y: number }) {
  // Off-screen until the cursor first moves; on a touch screen, at rest from the start.
  const [cursorPos, setCursorPos] = useState(() =>
    window.matchMedia?.('(hover: none)').matches
      ? { x: window.innerWidth * restAt.x, y: window.innerHeight * restAt.y }
      : { x: -999, y: -999 },
  )
  // Copies: the loop mutates `smooth`, and state must never be mutated.
  const mouse = useRef({ ...cursorPos })
  const smooth = useRef({ ...cursorPos })
  const rafRef = useRef<number | null>(null)
  const reducedMotion = usePrefersReducedMotion()

  useEffect(() => {
    const onMove = (e: MouseEvent) => {
      mouse.current = { x: e.clientX, y: e.clientY }
    }
    window.addEventListener('mousemove', onMove)

    const easing = reducedMotion ? 1 : EASING
    const tick = () => {
      const s = smooth.current
      const dx = mouse.current.x - s.x
      const dy = mouse.current.y - s.y
      // Settled: skip the render until the cursor moves again.
      if (Math.abs(dx) > 0.1 || Math.abs(dy) > 0.1) {
        s.x += dx * easing
        s.y += dy * easing
        setCursorPos({ x: s.x, y: s.y })
      }
      rafRef.current = requestAnimationFrame(tick)
    }
    rafRef.current = requestAnimationFrame(tick)

    return () => {
      window.removeEventListener('mousemove', onMove)
      if (rafRef.current !== null) cancelAnimationFrame(rafRef.current)
    }
  }, [reducedMotion])

  return cursorPos
}
