import { act, renderHook } from '@testing-library/react'
import { useSmoothedCursor } from './use-spotlight'

/** A matchMedia that answers the two questions the hook asks. */
function stubMatchMedia({ reduce = false, touch = false }) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query.includes('reduced-motion') ? reduce : query.includes('hover: none') ? touch : false,
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
}

describe('useSmoothedCursor', () => {
  let frames: FrameRequestCallback[] = []
  const runFrame = () =>
    act(() => {
      const due = frames
      frames = []
      for (const callback of due) callback(0)
    })

  beforeEach(() => {
    frames = []
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => frames.push(callback))
    vi.stubGlobal('cancelAnimationFrame', () => {})
  })
  afterEach(() => vi.unstubAllGlobals())

  it('trails the cursor, closing a tenth of the gap each frame', () => {
    stubMatchMedia({})
    const { result } = renderHook(() => useSmoothedCursor({ x: 0.5, y: 0.5 }))
    expect(result.current).toEqual({ x: -999, y: -999 })

    act(() => {
      window.dispatchEvent(new MouseEvent('mousemove', { clientX: 401, clientY: 1 }))
    })
    runFrame()

    expect(result.current.x).toBeCloseTo(-999 + 140)
    expect(result.current.y).toBeCloseTo(-999 + 100)
  })

  it('with reduced motion, sits exactly under the cursor: no trailing', () => {
    stubMatchMedia({ reduce: true })
    const { result } = renderHook(() => useSmoothedCursor({ x: 0.5, y: 0.5 }))

    act(() => {
      window.dispatchEvent(new MouseEvent('mousemove', { clientX: 401, clientY: 1 }))
    })
    runFrame()

    expect(result.current).toEqual({ x: 401, y: 1 })
  })

  it('rests at a fixed point on a touch screen, where there is no cursor', () => {
    stubMatchMedia({ touch: true })
    const { result } = renderHook(() => useSmoothedCursor({ x: 0.5, y: 0.25 }))

    expect(result.current).toEqual({ x: window.innerWidth * 0.5, y: window.innerHeight * 0.25 })
  })

  it('stops listening and animating when the page goes away', () => {
    stubMatchMedia({})
    const cancel = vi.fn()
    vi.stubGlobal('cancelAnimationFrame', cancel)
    const remove = vi.spyOn(window, 'removeEventListener')
    const { unmount } = renderHook(() => useSmoothedCursor({ x: 0.5, y: 0.5 }))

    unmount()

    expect(cancel).toHaveBeenCalled()
    expect(remove).toHaveBeenCalledWith('mousemove', expect.any(Function))
  })
})
