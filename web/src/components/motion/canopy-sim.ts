/**
 * The living canopy in the home page's white header band, as plain arithmetic with no drawing or
 * timers, so it can be tested. Time is in frames (1 = 1/60 s), so a slow frame takes a longer step.
 *
 * - Resting leaves sit to either side of the name. The cursor pushes them aside and they sway in the
 *   direction it moves, then spring back home.
 * - The glow follows an eased copy of the cursor, so it trails with a little inertia.
 * - Every so often along the cursor's path, 1–3 tiny leaves come loose, drift down and fade.
 */

export interface Leaf {
  homeX: number
  homeY: number
  /** Offset from home, and its velocity. */
  x: number
  y: number
  vx: number
  vy: number
  angle: number
  /** Extra rotation from being pushed, eased back to 0 with the offset. */
  tilt: number
  size: number
  /** 0 sage, 1 a deeper sage, 2 the lightest green. */
  tone: 0 | 1 | 2
  /** How close the cursor is (0–1), to brighten the leaf a little while it is near. */
  near: number
}

export interface Drift {
  x: number
  y: number
  vx: number
  vy: number
  angle: number
  spin: number
  size: number
  tone: 0 | 1 | 2
  age: number
  life: number
}

export interface Canopy {
  leaves: Leaf[]
  drifts: Drift[]
  cursor: {
    /** The eased position the glow is drawn at, and where the pointer really is. */
    x: number
    y: number
    targetX: number
    targetY: number
    vx: number
    vy: number
    inside: boolean
    /** 0 when the cursor is away, 1 when it has been in the band a moment: fades the glow. */
    presence: number
    /** Distance moved since the last chance to shed leaves. */
    travelled: number
  }
}

export interface Box {
  left: number
  top: number
  right: number
  bottom: number
}

/** How far the cursor reaches, in px. */
export const REACH = 150
/** Never more falling leaves than this at once. */
export const MAX_DRIFTS = 14
/** Px of cursor travel between chances to shed leaves; half the chances shed any. */
const SHED_EVERY = 80
const SHED_CHANCE = 0.5
/** Each frame the glow closes this share of the gap to the cursor: inertia, not a snap. */
const CURSOR_EASE = 0.14
const SPRING = 0.018
const DAMPING = 0.9
const MAX_OFFSET = 22

/**
 * Where the resting leaves may sit, as fractions of the band, with their size, angle and tone. It is
 * a fixed design, not random, so the header looks the same on every visit. Slots behind the name
 * are dropped when the leaves are laid out.
 */
const SLOTS: { fx: number; fy: number; size: number; angle: number; tone: 0 | 1 | 2 }[] = [
  { fx: 0.04, fy: 0.32, size: 6, angle: -0.6, tone: 0 },
  { fx: 0.09, fy: 0.72, size: 8, angle: 0.5, tone: 2 },
  { fx: 0.14, fy: 0.4, size: 5, angle: 2.4, tone: 1 },
  { fx: 0.19, fy: 0.8, size: 7, angle: -2.1, tone: 0 },
  { fx: 0.24, fy: 0.26, size: 9, angle: 0.9, tone: 2 },
  { fx: 0.29, fy: 0.62, size: 5, angle: -0.3, tone: 0 },
  { fx: 0.34, fy: 0.34, size: 6, angle: 1.8, tone: 1 },
  { fx: 0.66, fy: 0.7, size: 6, angle: -1.2, tone: 0 },
  { fx: 0.71, fy: 0.3, size: 5, angle: 0.4, tone: 1 },
  { fx: 0.76, fy: 0.64, size: 9, angle: 2.8, tone: 2 },
  { fx: 0.81, fy: 0.22, size: 6, angle: -0.8, tone: 0 },
  { fx: 0.86, fy: 0.76, size: 7, angle: 1.1, tone: 1 },
  { fx: 0.91, fy: 0.4, size: 5, angle: -2.6, tone: 0 },
  { fx: 0.96, fy: 0.68, size: 8, angle: 0.2, tone: 2 },
]

export function createCanopy(): Canopy {
  return {
    leaves: [],
    drifts: [],
    cursor: { x: 0, y: 0, targetX: 0, targetY: 0, vx: 0, vy: 0, inside: false, presence: 0, travelled: 0 },
  }
}

/** The resting leaves for a band of this size, clear of `avoid` (the name) by a margin. */
export function layoutLeaves(width: number, height: number, avoid: Box | null, margin = 36): Leaf[] {
  return SLOTS.map((s) => ({ homeX: s.fx * width, homeY: s.fy * height, s }))
    .filter(({ homeX, homeY }) => !avoid || homeX < avoid.left - margin || homeX > avoid.right + margin || homeY < avoid.top - margin || homeY > avoid.bottom + margin)
    .map(({ homeX, homeY, s }) => ({ homeX, homeY, x: 0, y: 0, vx: 0, vy: 0, angle: s.angle, tilt: 0, size: s.size, tone: s.tone, near: 0 }))
}

export function pointerAt(canopy: Canopy, x: number, y: number) {
  const c = canopy.cursor
  // Arriving from outside: start the glow where the cursor is, not sweeping in from where it left.
  if (c.presence < 0.02) {
    c.x = x
    c.y = y
  }
  c.targetX = x
  c.targetY = y
  c.inside = true
}

export function pointerLeft(canopy: Canopy) {
  canopy.cursor.inside = false
}

/** One step of `dt` frames. `random` is Math.random in the page and a fixed sequence in tests. */
export function step(canopy: Canopy, dt: number, random: () => number) {
  const c = canopy.cursor
  const ease = 1 - (1 - CURSOR_EASE) ** dt
  const nextX = c.x + (c.targetX - c.x) * ease
  const nextY = c.y + (c.targetY - c.y) * ease
  c.vx = (nextX - c.x) / dt
  c.vy = (nextY - c.y) / dt
  c.x = nextX
  c.y = nextY
  c.presence = approach(c.presence, c.inside ? 1 : 0, 0.06 * dt)

  if (c.inside) {
    c.travelled += Math.hypot(c.vx, c.vy) * dt
    if (c.travelled >= SHED_EVERY) {
      c.travelled = 0
      if (random() < SHED_CHANCE) shed(canopy, random)
    }
  }

  const damping = DAMPING ** dt
  for (const leaf of canopy.leaves) {
    const dx = leaf.homeX + leaf.x - c.x
    const dy = leaf.homeY + leaf.y - c.y
    const distance = Math.hypot(dx, dy) || 1
    const reach = distance < REACH ? (1 - distance / REACH) ** 2 * c.presence : 0
    leaf.near = reach
    if (reach > 0) {
      // Pushed gently away from the cursor, and carried a little in the direction it moves.
      leaf.vx += ((dx / distance) * 0.55 + c.vx * 0.02) * reach * dt
      leaf.vy += ((dy / distance) * 0.55 + c.vy * 0.02) * reach * dt
    }
    leaf.vx = (leaf.vx - leaf.x * SPRING * dt) * damping
    leaf.vy = (leaf.vy - leaf.y * SPRING * dt) * damping
    leaf.x += leaf.vx * dt
    leaf.y += leaf.vy * dt
    const offset = Math.hypot(leaf.x, leaf.y)
    if (offset > MAX_OFFSET) {
      leaf.x *= MAX_OFFSET / offset
      leaf.y *= MAX_OFFSET / offset
    }
    leaf.tilt = clamp(leaf.x * 0.03 + leaf.vx * 0.05, -0.6, 0.6)
  }

  for (const d of canopy.drifts) {
    d.age += dt
    d.vy += 0.012 * dt
    d.vx *= 0.975 ** dt
    d.vy *= 0.985 ** dt
    d.x += d.vx * dt
    d.y += d.vy * dt
    d.angle += d.spin * dt
  }
  canopy.drifts = canopy.drifts.filter((d) => d.age < d.life)
}

/** 1–3 leaves come loose at the cursor, usually just one. */
function shed(canopy: Canopy, random: () => number) {
  const c = canopy.cursor
  const roll = random()
  const count = roll < 0.6 ? 1 : roll < 0.88 ? 2 : 3
  for (let i = 0; i < count && canopy.drifts.length < MAX_DRIFTS; i++) {
    canopy.drifts.push({
      x: c.x + (random() - 0.5) * 12,
      y: c.y + (random() - 0.5) * 12,
      vx: c.vx * 0.15 + (random() - 0.5) * 0.8,
      vy: c.vy * 0.15 + (random() - 0.5) * 0.5,
      angle: random() * Math.PI * 2,
      spin: (random() - 0.5) * 0.06,
      size: 3.5 + random() * 2.5,
      tone: random() < 0.5 ? 1 : random() < 0.5 ? 0 : 2,
      age: 0,
      life: 70 + random() * 40,
    })
  }
}

/** A falling leaf's opacity: a quick fade in, then a long fade out. */
export function driftOpacity(d: Drift): number {
  const t = d.age / d.life
  return Math.min(1, d.age / 10) * (1 - t) ** 1.4
}

/** Nothing is moving and nothing will until the pointer moves again: the frame loop can stop. */
export function isSettled(canopy: Canopy): boolean {
  const c = canopy.cursor
  const glowStill = Math.abs(c.presence - (c.inside ? 1 : 0)) < 0.005 && Math.hypot(c.targetX - c.x, c.targetY - c.y) < 0.1
  // A leaf under a resting cursor stays pushed aside, still; out of reach it must be home.
  return glowStill && canopy.drifts.length === 0 && canopy.leaves.every((l) => (l.near > 0 ? speed(l) < 0.01 : Math.hypot(l.x, l.y) < 0.05 && speed(l) < 0.02))
}

function speed(leaf: Leaf) {
  return Math.hypot(leaf.vx, leaf.vy)
}

function approach(value: number, target: number, by: number) {
  return value < target ? Math.min(target, value + by) : Math.max(target, value - by)
}

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value))
}
