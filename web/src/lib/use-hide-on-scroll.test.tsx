import { act, renderHook } from '@testing-library/react'
import { useHideOnScroll } from './use-hide-on-scroll'

/** Puts the page at `y` and fires the scroll event the browser would. */
function scrollTo(y: number) {
  act(() => {
    window.scrollY = y
    window.dispatchEvent(new Event('scroll'))
  })
}

describe('useHideOnScroll', () => {
  beforeEach(() => {
    // A frame callback runs at once, so each scroll event is read straight away. It returns 0,
    // "nothing pending", because by the time the caller stores the id the frame has already run.
    vi.stubGlobal('requestAnimationFrame', (run: FrameRequestCallback) => {
      run(0)
      return 0
    })
    vi.stubGlobal('cancelAnimationFrame', () => {})
    Object.defineProperty(document.documentElement, 'scrollHeight', { value: 5000, configurable: true })
    window.scrollY = 0
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.useRealTimers()
    window.scrollY = 0
  })

  it('hides while scrolling down and comes back as soon as the page scrolls up', () => {
    const { result } = renderHook(() => useHideOnScroll())
    expect(result.current).toMatchObject({ visible: true, atTop: true })

    scrollTo(200)
    expect(result.current).toMatchObject({ visible: false, atTop: false })

    scrollTo(190)
    expect(result.current.visible).toBe(true)
  })

  it('ignores movements smaller than the threshold, but adds up a slow scroll', () => {
    const { result } = renderHook(() => useHideOnScroll())
    scrollTo(300)
    scrollTo(280)
    expect(result.current.visible).toBe(true)

    // Jitter: up and down by a few pixels changes nothing.
    scrollTo(284)
    scrollTo(279)
    scrollTo(283)
    expect(result.current.visible).toBe(true)

    // A slow scroll down, 3 px an event, still hides it once it has gone far enough.
    for (let y = 283; y <= 300; y += 3) scrollTo(y)
    expect(result.current.visible).toBe(false)
  })

  it('always shows near the top, and ignores the bounce past the top of a touch screen', () => {
    const { result } = renderHook(() => useHideOnScroll())
    scrollTo(400)
    expect(result.current.visible).toBe(false)

    scrollTo(8)
    expect(result.current).toMatchObject({ visible: true, atTop: true })
    scrollTo(-30)
    expect(result.current).toMatchObject({ visible: true, atTop: true })
  })

  it('stays hidden while an in-page link scrolls the page, even upwards, then behaves again', () => {
    // Only the timeouts: the frame stub above must stay in place.
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
    const { result } = renderHook(() => useHideOnScroll())
    scrollTo(3000)

    const link = document.createElement('a')
    link.href = '#how-it-works'
    document.body.append(link)
    act(() => link.click())
    expect(result.current.visible).toBe(false)

    // The link's own scroll goes up to the section: the header must not land over its heading.
    scrollTo(2000)
    scrollTo(1000)
    expect(result.current.visible).toBe(false)

    act(() => vi.advanceTimersByTime(200))
    scrollTo(980)
    expect(result.current.visible).toBe(true)
    link.remove()
  })

  it('lets the link back to the top show it as usual', () => {
    const { result } = renderHook(() => useHideOnScroll())
    scrollTo(3000)
    const link = document.createElement('a')
    link.href = '#top'
    document.body.append(link)

    act(() => link.click())
    scrollTo(1500)
    expect(result.current.visible).toBe(true)
    link.remove()
  })

  it('can be brought back on demand, as when focus moves into it', () => {
    const { result } = renderHook(() => useHideOnScroll())
    scrollTo(500)
    expect(result.current.visible).toBe(false)

    act(() => result.current.reveal())
    expect(result.current.visible).toBe(true)
  })
})
