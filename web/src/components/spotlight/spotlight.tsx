import { useEffect, useRef } from 'react'
import { SPOTLIGHT_R } from './use-spotlight'

/**
 * The mask is drawn at a quarter of the layer's size and stretched back by `maskSize: 100% 100%`.
 * Its edge is a soft gradient, so the result looks the same, while encoding the canvas on every
 * frame costs a sixteenth of what a full-size canvas would.
 */
const MASK_SCALE = 0.25

/** Hides the reveal layer completely. */
const NOTHING = 'linear-gradient(transparent, transparent)'

/**
 * A second image shown only inside a soft circle around the cursor. A hidden canvas draws a radial
 * gradient at the cursor; the canvas, as a data URL, becomes the layer's mask. Fill it into a
 * positioned parent: the layer measures itself, so it works full-screen or inside a panel.
 */
export function RevealLayer({
  image,
  cursorX,
  cursorY,
  position = 'center',
}: {
  image: string
  cursorX: number
  cursorY: number
  /** The photo's `background-position`; give the base the same one so the two line up. */
  position?: string
}) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const layerRef = useRef<HTMLDivElement>(null)

  // The canvas matches the layer (scaled), on mount and whenever the window changes size.
  useEffect(() => {
    const fit = () => {
      const canvas = canvasRef.current
      const layer = layerRef.current
      if (!canvas || !layer) return
      canvas.width = Math.max(1, Math.round(layer.clientWidth * MASK_SCALE))
      canvas.height = Math.max(1, Math.round(layer.clientHeight * MASK_SCALE))
    }
    fit()
    window.addEventListener('resize', fit)
    return () => window.removeEventListener('resize', fit)
  }, [])

  // Every render: redraw the gradient where the cursor is now, and mask the layer with it.
  useEffect(() => {
    const canvas = canvasRef.current
    const layer = layerRef.current
    if (!canvas || !layer) return
    const rect = layer.getBoundingClientRect()
    // The layer can change size without the window doing so (fonts arriving, content reflowing);
    // a canvas of the wrong shape would stretch the circle into an oval.
    const width = Math.max(1, Math.round(rect.width * MASK_SCALE))
    const height = Math.max(1, Math.round(rect.height * MASK_SCALE))
    if (canvas.width !== width || canvas.height !== height) {
      canvas.width = width
      canvas.height = height
    }
    const x = cursorX - rect.left
    const y = cursorY - rect.top
    const outside = x < -SPOTLIGHT_R || y < -SPOTLIGHT_R || x > rect.width + SPOTLIGHT_R || y > rect.height + SPOTLIGHT_R
    const ctx = outside ? null : canvas.getContext('2d')
    if (!ctx) {
      layer.style.maskImage = NOTHING
      layer.style.webkitMaskImage = NOTHING
      return
    }

    const cx = x * MASK_SCALE
    const cy = y * MASK_SCALE
    const r = SPOTLIGHT_R * MASK_SCALE
    ctx.clearRect(0, 0, canvas.width, canvas.height)
    const gradient = ctx.createRadialGradient(cx, cy, 0, cx, cy, r)
    gradient.addColorStop(0, 'rgba(255,255,255,1)')
    gradient.addColorStop(0.4, 'rgba(255,255,255,1)')
    gradient.addColorStop(0.6, 'rgba(255,255,255,0.75)')
    gradient.addColorStop(0.75, 'rgba(255,255,255,0.4)')
    gradient.addColorStop(0.88, 'rgba(255,255,255,0.12)')
    gradient.addColorStop(1, 'rgba(255,255,255,0)')
    ctx.fillStyle = gradient
    ctx.beginPath()
    ctx.arc(cx, cy, r, 0, Math.PI * 2)
    ctx.fill()

    const mask = `url(${canvas.toDataURL()})`
    layer.style.maskImage = mask
    layer.style.webkitMaskImage = mask
  })

  return (
    <>
      <canvas ref={canvasRef} aria-hidden="true" className="pointer-events-none absolute inset-0" style={{ display: 'none' }} />
      <div
        ref={layerRef}
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 z-30 bg-cover bg-center bg-no-repeat"
        style={{
          backgroundImage: `url(${image})`,
          backgroundPosition: position,
          maskImage: NOTHING,
          WebkitMaskImage: NOTHING,
          maskSize: '100% 100%',
          WebkitMaskSize: '100% 100%',
          maskRepeat: 'no-repeat',
          WebkitMaskRepeat: 'no-repeat',
        }}
      />
    </>
  )
}

/**
 * The photo under the spotlight, in canopy monochrome: the reveal brings back its true colour.
 * Give it the reveal layer's `position` so the two line up exactly.
 */
export function DuotoneBase({ image, position = 'center' }: { image: string; position?: string }) {
  return (
    <div aria-hidden="true" className="absolute inset-0">
      <div
        className="absolute inset-0 bg-cover bg-center bg-no-repeat grayscale contrast-110 brightness-75"
        style={{ backgroundImage: `url(${image})`, backgroundPosition: position }}
      />
      <div className="absolute inset-0 bg-brand-800 opacity-60 mix-blend-multiply" />
    </div>
  )
}
