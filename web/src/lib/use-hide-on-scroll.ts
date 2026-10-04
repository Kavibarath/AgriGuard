import { useCallback, useEffect, useRef, useState } from 'react'

/** After an in-page link's scroll, this long without a scroll event means it has arrived (ms). */
const ARRIVED_AFTER = 150

/**
 * A "smart sticky" header's visibility: hidden while the page scrolls down, back as soon as it
 * scrolls up, and always shown near the top.
 *
 * - Scroll events are read at most once a frame, and the previous position lives in a ref, so a
 *   fast scroll does not re-render on every event.
 * - The reference point moves only when a direction is decided. Moves under `threshold` px add up,
 *   so a slow scroll still hides the header, while a trackpad's jitter or a phone's bounce at either
 *   end does not.
 * - Following an in-page link (#how-it-works…) hides the header for that scroll, even when it goes
 *   up, so the header does not land over the heading the link was for. `#top` is the exception.
 */
export function useHideOnScroll({ threshold = 8, topZone = 10 }: { threshold?: number; topZone?: number } = {}) {
  const [visible, setVisible] = useState(true)
  const [atTop, setAtTop] = useState(true)
  const lastY = useRef(0)
  const followingLink = useRef(false)

  useEffect(() => {
    let frame = 0
    let arrived: ReturnType<typeof setTimeout> | undefined

    /** The scroll position, ignoring the rubber-band overscroll past either end on touch screens. */
    const position = () => {
      const max = document.documentElement.scrollHeight - window.innerHeight
      return Math.max(0, max > 0 ? Math.min(window.scrollY, max) : window.scrollY)
    }

    const update = () => {
      frame = 0
      const y = position()
      setAtTop(y <= topZone)

      if (y <= topZone) {
        setVisible(true)
        lastY.current = y
        return
      }
      if (followingLink.current) {
        lastY.current = y
        clearTimeout(arrived)
        arrived = setTimeout(() => (followingLink.current = false), ARRIVED_AFTER)
        return
      }

      const moved = y - lastY.current
      if (Math.abs(moved) < threshold) return
      setVisible(moved < 0)
      lastY.current = y
    }

    const onScroll = () => {
      if (!frame) frame = requestAnimationFrame(update)
    }

    const onClick = (e: MouseEvent) => {
      const link = (e.target as Element | null)?.closest?.('a[href^="#"]')
      const href = link?.getAttribute('href')
      if (!href || href === '#' || href === '#top') return
      followingLink.current = true
      setVisible(false)
      // If the page is already there, no scroll comes to end it.
      clearTimeout(arrived)
      arrived = setTimeout(() => (followingLink.current = false), 1000)
    }

    lastY.current = position()
    window.addEventListener('scroll', onScroll, { passive: true })
    document.addEventListener('click', onClick)
    return () => {
      cancelAnimationFrame(frame)
      clearTimeout(arrived)
      window.removeEventListener('scroll', onScroll)
      document.removeEventListener('click', onClick)
    }
  }, [threshold, topZone])

  /** Bring the header back, e.g. when the keyboard moves focus into it while it is hidden. */
  const reveal = useCallback(() => setVisible(true), [])

  return { visible, atTop, reveal }
}
