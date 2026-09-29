import { isReEntryActive, toDayRanges } from './safety'

describe('toDayRanges', () => {
  it('joins consecutive days and splits at a gap, across a month end', () => {
    expect(toDayRanges(['2026-10-01', '2026-09-30', '2026-10-05', '2026-09-29'])).toEqual([
      { from: '2026-09-29', to: '2026-10-01', days: 3 },
      { from: '2026-10-05', to: '2026-10-05', days: 1 },
    ])
  })

  it('is empty for no blocked days', () => {
    expect(toDayRanges([])).toEqual([])
  })
})

describe('isReEntryActive', () => {
  const now = Date.parse('2026-09-29T06:00:00Z')

  it('is active only while the clear time is ahead', () => {
    expect(isReEntryActive('2026-09-29T08:00:00Z', now)).toBe(true)
    expect(isReEntryActive('2026-09-29T05:00:00Z', now)).toBe(false)
    expect(isReEntryActive(null, now)).toBe(false)
  })
})
