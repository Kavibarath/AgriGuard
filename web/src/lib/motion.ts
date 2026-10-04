import { useCallback, useSyncExternalStore } from 'react'

const REDUCED_MOTION = '(prefers-reduced-motion: reduce)'
/** A mouse or trackpad: something that can hover and point precisely, unlike a finger. */
const FINE_POINTER = '(hover: hover) and (pointer: fine)'

/** Whether a media query matches, kept up to date if it changes while the page is open. */
export function useMediaQuery(query: string): boolean {
  const subscribe = useCallback(
    (onChange: () => void) => {
      const list = window.matchMedia?.(query)
      list?.addEventListener('change', onChange)
      return () => list?.removeEventListener('change', onChange)
    },
    [query],
  )
  return useSyncExternalStore(
    subscribe,
    () => window.matchMedia?.(query).matches ?? false,
    () => false,
  )
}

/**
 * True when the person has asked their system for less motion, and kept up to date if they change
 * the setting while the page is open. For what CSS cannot switch off by itself: a video, a
 * cursor-following effect. Everything CSS can do is already gated in index.css.
 */
export function usePrefersReducedMotion(): boolean {
  return useMediaQuery(REDUCED_MOTION)
}

/** True with a mouse or trackpad; false on touch screens, where there is no cursor to follow. */
export function useHasFinePointer(): boolean {
  return useMediaQuery(FINE_POINTER)
}
