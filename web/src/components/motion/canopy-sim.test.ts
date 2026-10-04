import { createCanopy, isSettled, layoutLeaves, MAX_DRIFTS, pointerAt, pointerLeft, step } from './canopy-sim'

/** The same "random" numbers every run, cycling through a fixed list. */
function sequence(...values: number[]) {
  let i = 0
  return () => values[i++ % values.length]
}

/** Runs the canopy for `frames` frames of 1/60 s. */
function run(canopy: ReturnType<typeof createCanopy>, frames: number, random = sequence(0.9)) {
  for (let i = 0; i < frames; i++) step(canopy, 1, random)
}

describe('canopy', () => {
  it('keeps the resting leaves clear of the name', () => {
    // A narrow window, where the name covers some of the slots.
    const all = layoutLeaves(900, 130, null)
    const name = { left: 330, right: 570, top: 30, bottom: 100 }
    const clear = layoutLeaves(900, 130, name)

    expect(clear.length).toBeGreaterThan(0)
    expect(clear.length).toBeLessThan(all.length)
    for (const leaf of clear) {
      expect(leaf.homeX < name.left - 36 || leaf.homeX > name.right + 36).toBe(true)
    }
  })

  it('eases the glow towards the cursor instead of jumping to it', () => {
    const canopy = createCanopy()
    pointerAt(canopy, 100, 50)
    run(canopy, 1)
    pointerAt(canopy, 300, 50)
    run(canopy, 1)

    expect(canopy.cursor.x).toBeGreaterThan(100)
    expect(canopy.cursor.x).toBeLessThan(300)
    run(canopy, 120)
    expect(canopy.cursor.x).toBeCloseTo(300, 0)
  })

  it('pushes a nearby leaf aside, then lets it spring home once the cursor leaves', () => {
    const canopy = createCanopy()
    canopy.leaves = layoutLeaves(1600, 130, null)
    const leaf = canopy.leaves[0]
    // Just to the right of the first leaf: it should be pushed left.
    pointerAt(canopy, leaf.homeX + 20, leaf.homeY)
    run(canopy, 30)

    expect(leaf.x).toBeLessThan(-1)
    expect(Math.hypot(leaf.x, leaf.y)).toBeLessThanOrEqual(22.0001)

    pointerLeft(canopy)
    run(canopy, 600)
    expect(Math.hypot(leaf.x, leaf.y)).toBeLessThan(0.05)
    expect(isSettled(canopy)).toBe(true)
  })

  it('sheds one to three leaves at a time along the path, never too many, and lets them fade away', () => {
    const canopy = createCanopy()
    pointerAt(canopy, 0, 60)
    // A long sweep back and forth, with every chance to shed taken (random 0 is below the chance).
    let shedMost = 0
    for (let i = 0; i < 200; i++) {
      const before = canopy.drifts.length
      pointerAt(canopy, i % 2 ? 0 : 1600, 60)
      step(canopy, 1, sequence(0))
      shedMost = Math.max(shedMost, canopy.drifts.length - before)
      expect(canopy.drifts.length).toBeLessThanOrEqual(MAX_DRIFTS)
    }
    expect(canopy.drifts.length).toBeGreaterThan(0)
    expect(shedMost).toBeLessThanOrEqual(3)

    pointerLeft(canopy)
    run(canopy, 200)
    expect(canopy.drifts).toHaveLength(0)
  })

  it('comes to rest while the cursor rests, so the frame loop can stop', () => {
    const canopy = createCanopy()
    canopy.leaves = layoutLeaves(1600, 130, null)
    pointerAt(canopy, canopy.leaves[0].homeX + 30, canopy.leaves[0].homeY)
    run(canopy, 600)

    expect(canopy.cursor.inside).toBe(true)
    expect(isSettled(canopy)).toBe(true)
  })
})
