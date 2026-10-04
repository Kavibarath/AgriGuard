import { useEffect, useRef, type RefObject } from 'react'
import { useHasFinePointer, usePrefersReducedMotion } from '@/lib/motion'
import { createCanopy, driftOpacity, isSettled, layoutLeaves, pointerAt, pointerLeft, step, type Box, type Canopy } from './canopy-sim'

/** The glow's radius, px: wide and faint, a presence rather than a spotlight. */
const GLOW_R = 190

type Rgb = [number, number, number]

interface Palette {
  /** Sage, deeper sage, lightest green: the three leaf tones. */
  tones: [Rgb, Rgb, Rgb]
  /** Deep forest green, for the midribs. */
  vein: Rgb
  glow: Rgb
}

/**
 * Leaves that notice the cursor, in the white band behind the AgriGuard name (canopy-sim.ts has the
 * behaviour). Drawn on a canvas that ignores the pointer and is hidden from assistive technology.
 *
 * Only with a mouse or trackpad: on a touch screen there is no cursor, so the band stays plain
 * white. With reduced motion the leaves are drawn once, at rest, and nothing follows the cursor.
 * The frame loop runs only while something is moving, and stops when all is still.
 */
export function LivingCanopy({ avoid }: { avoid?: RefObject<HTMLElement | null> }) {
  const finePointer = useHasFinePointer()
  const reducedMotion = usePrefersReducedMotion()
  const canvasRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const canvas = canvasRef.current
    const host = canvas?.parentElement
    const ctx = canvas?.getContext('2d')
    if (!canvas || !host || !ctx) return

    const palette = readPalette()
    const canopy = createCanopy()
    let width = 0
    let height = 0
    let ratio = 1

    const draw = () => {
      ctx.setTransform(ratio, 0, 0, ratio, 0, 0)
      ctx.clearRect(0, 0, width, height)
      paint(ctx, canopy, palette)
    }

    const layout = () => {
      const band = host.getBoundingClientRect()
      width = band.width
      height = band.height
      // Sharp on high-density screens, never below 1:1 (a zoomed-out window reports less).
      ratio = Math.min(Math.max(window.devicePixelRatio || 1, 1), 2)
      canvas.width = Math.round(width * ratio)
      canvas.height = Math.round(height * ratio)
      const name = avoid?.current?.getBoundingClientRect()
      const box: Box | null = name ? { left: name.left - band.left, right: name.right - band.left, top: name.top - band.top, bottom: name.bottom - band.top } : null
      canopy.leaves = layoutLeaves(width, height, box)
      draw()
    }

    layout()
    // The band changes with the window; the name's width changes once its web font arrives.
    const resize = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(layout)
    resize?.observe(host)
    if (avoid?.current) resize?.observe(avoid.current)

    if (reducedMotion) return () => resize?.disconnect()

    let frame = 0
    let last = 0
    const tick = (now: number) => {
      // Frames of 1/60 s; after a stall (a hidden tab) take a short step rather than a leap.
      const dt = last ? Math.min((now - last) / (1000 / 60), 3) : 1
      last = now
      step(canopy, dt, Math.random)
      draw()
      if (isSettled(canopy)) {
        frame = 0
        last = 0
        return
      }
      frame = requestAnimationFrame(tick)
    }
    const wake = () => {
      if (!frame) frame = requestAnimationFrame(tick)
    }

    const onMove = (e: PointerEvent) => {
      if (e.pointerType === 'touch') return
      const band = host.getBoundingClientRect()
      pointerAt(canopy, e.clientX - band.left, e.clientY - band.top)
      wake()
    }
    const onLeave = () => {
      pointerLeft(canopy)
      wake()
    }
    host.addEventListener('pointermove', onMove)
    host.addEventListener('pointerleave', onLeave)

    return () => {
      cancelAnimationFrame(frame)
      resize?.disconnect()
      host.removeEventListener('pointermove', onMove)
      host.removeEventListener('pointerleave', onLeave)
    }
  }, [finePointer, reducedMotion, avoid])

  if (!finePointer) return null
  return <canvas ref={canvasRef} aria-hidden="true" className="pointer-events-none absolute inset-0 h-full w-full" />
}

function paint(ctx: CanvasRenderingContext2D, canopy: Canopy, palette: Palette) {
  const c = canopy.cursor
  if (c.presence > 0.001) {
    const glow = ctx.createRadialGradient(c.x, c.y, 0, c.x, c.y, GLOW_R)
    glow.addColorStop(0, rgba(palette.glow, 0.16 * c.presence))
    glow.addColorStop(0.5, rgba(palette.glow, 0.06 * c.presence))
    glow.addColorStop(1, rgba(palette.glow, 0))
    ctx.fillStyle = glow
    ctx.fillRect(c.x - GLOW_R, c.y - GLOW_R, GLOW_R * 2, GLOW_R * 2)
  }

  // Resting leaves are faint, and a little clearer while the cursor is near.
  for (const leaf of canopy.leaves) {
    drawLeaf(ctx, leaf.homeX + leaf.x, leaf.homeY + leaf.y, leaf.angle + leaf.tilt, leaf.size, rgba(palette.tones[leaf.tone], 0.4 + 0.3 * leaf.near), rgba(palette.vein, 0.2 + 0.15 * leaf.near))
  }
  for (const d of canopy.drifts) {
    const opacity = driftOpacity(d)
    drawLeaf(ctx, d.x, d.y, d.angle, d.size, rgba(palette.tones[d.tone], 0.6 * opacity), rgba(palette.vein, 0.25 * opacity))
  }
}

/** A small pointed leaf with a midrib that runs on into a stalk. */
function drawLeaf(ctx: CanvasRenderingContext2D, x: number, y: number, angle: number, size: number, fill: string, vein: string) {
  ctx.save()
  ctx.translate(x, y)
  ctx.rotate(angle)
  ctx.beginPath()
  ctx.moveTo(-size, 0)
  ctx.bezierCurveTo(-size * 0.4, -size * 0.62, size * 0.5, -size * 0.5, size, 0)
  ctx.bezierCurveTo(size * 0.5, size * 0.5, -size * 0.4, size * 0.62, -size, 0)
  ctx.fillStyle = fill
  ctx.fill()
  ctx.beginPath()
  ctx.moveTo(-size * 1.3, 0)
  ctx.lineTo(size * 0.75, 0)
  ctx.strokeStyle = vein
  ctx.lineWidth = 0.8
  ctx.stroke()
  ctx.restore()
}

/** The leaf colours, from the design tokens in index.css. */
function readPalette(): Palette {
  const style = getComputedStyle(document.documentElement)
  const token = (name: string, fallback: string) => hexToRgb(style.getPropertyValue(`--color-${name}`).trim() || fallback)
  return {
    tones: [token('brand-300', '#8fcca0'), token('brand-400', '#58a97c'), token('brand-200', '#bce3c2')],
    vein: token('brand-700', '#22543d'),
    glow: token('brand-300', '#8fcca0'),
  }
}

function hexToRgb(hex: string): Rgb {
  const n = Number.parseInt(hex.replace('#', ''), 16)
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
}

function rgba([r, g, b]: Rgb, alpha: number) {
  return `rgba(${r}, ${g}, ${b}, ${alpha.toFixed(3)})`
}
