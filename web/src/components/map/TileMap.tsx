import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import { Link } from 'react-router'
import { centreOf, fitZoom, project, TILE_SIZE, type LatLng, type Point } from '@/lib/geo'
import { cn } from '@/lib/utils'

export type PinTone = 'danger' | 'warning' | 'active' | 'neutral' | 'success' | 'severe'

export interface MapPin {
  id: string
  position: LatLng
  /** Read by screen readers and shown on hover; the pin itself is only a dot. */
  label: string
  tone: PinTone
  /** A filled dot for a report; a hollow ring for a reference point such as a plot's centre. */
  variant?: 'dot' | 'ring'
  /** Larger pins for heavier weight, such as a district under severe pressure. */
  size?: 'sm' | 'md' | 'lg'
  /** The one in focus: lifted above the others with a canopy ring and a deeper shadow. */
  selected?: boolean
  href?: string
}

const toneClasses: Record<PinTone, string> = {
  danger: 'bg-danger ring-danger',
  warning: 'bg-warning ring-warning',
  active: 'bg-brand-600 ring-brand-600',
  neutral: 'bg-stone-500 ring-stone-500',
  success: 'bg-success ring-success',
  severe: 'bg-danger-800 ring-danger-800',
}

const pinSizes = { sm: 'size-3', md: 'size-4', lg: 'size-6' } as const

const MIN_ZOOM = 3
const MAX_ZOOM = 18
// Used until the container has been measured, and in tests, where jsdom lays nothing out.
const FALLBACK_WIDTH = 640

/**
 * A small OpenStreetMap map: tiles laid out by hand in Web Mercator, pins on top, zoom buttons and
 * drag to pan. It fits its pins on first render, but no closer than `maxFitZoom`: closer than about
 * 13, rural tiles in the hill country are often plain land with nothing to orient by. Tiles come from tile.openstreetmap.org under
 * their usage policy (attribution shown, light use); nothing else leaves the browser.
 */
export function TileMap({ pins, height = 320, label, maxFitZoom = 13 }: { pins: MapPin[]; height?: number; label: string; maxFitZoom?: number }) {
  const containerRef = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(FALLBACK_WIDTH)
  const [zoomOffset, setZoomOffset] = useState(0)
  const [pan, setPan] = useState<Point>({ x: 0, y: 0 })
  const drag = useRef<{ start: Point; pan: Point } | null>(null)

  useEffect(() => {
    const element = containerRef.current
    if (!element || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(([entry]) => {
      if (entry.contentRect.width > 0) setWidth(entry.contentRect.width)
    })
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  const positions = pins.map((p) => p.position)
  const fitted = fitZoom(positions, width, height, { minZoom: MIN_ZOOM, maxZoom: maxFitZoom })
  const zoom = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, fitted + zoomOffset))
  const centre = positions.length > 0 ? centreOf(positions, zoom) : project({ lat: 7.87, lng: 80.77 }, zoom) // Sri Lanka
  // World pixel at the container's top-left corner.
  const origin = { x: centre.x - width / 2 + pan.x, y: centre.y - height / 2 + pan.y }

  const tiles: { key: string; src: string; left: number; top: number }[] = []
  const worldTiles = 2 ** zoom
  for (let tx = Math.floor(origin.x / TILE_SIZE); tx <= Math.floor((origin.x + width) / TILE_SIZE); tx++) {
    for (let ty = Math.floor(origin.y / TILE_SIZE); ty <= Math.floor((origin.y + height) / TILE_SIZE); ty++) {
      if (ty < 0 || ty >= worldTiles) continue
      const wrappedX = ((tx % worldTiles) + worldTiles) % worldTiles
      tiles.push({
        key: `${zoom}/${tx}/${ty}`,
        src: `https://tile.openstreetmap.org/${zoom}/${wrappedX}/${ty}.png`,
        left: tx * TILE_SIZE - origin.x,
        top: ty * TILE_SIZE - origin.y,
      })
    }
  }

  const changeZoom = (delta: number) => {
    const next = Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, zoom + delta))
    if (next === zoom) return
    // The pan is in pixels at the old zoom; scale it so the same place stays in the middle.
    const factor = 2 ** (next - zoom)
    setPan((p) => ({ x: p.x * factor, y: p.y * factor }))
    setZoomOffset(next - fitted)
  }

  const onPointerDown = (event: ReactPointerEvent<HTMLDivElement>) => {
    // Pins are links: let a click on one through instead of starting a drag.
    if ((event.target as HTMLElement).closest('a, button')) return
    drag.current = { start: { x: event.clientX, y: event.clientY }, pan }
    event.currentTarget.setPointerCapture?.(event.pointerId)
  }
  const onPointerMove = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (!drag.current) return
    const { start, pan: from } = drag.current
    setPan({ x: from.x - (event.clientX - start.x), y: from.y - (event.clientY - start.y) })
  }
  const endDrag = () => {
    drag.current = null
  }

  return (
    <div
      ref={containerRef}
      role="region"
      aria-label={label}
      className="relative touch-none select-none overflow-hidden rounded-xl border border-border-subtle bg-surface-inset"
      style={{ height }}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
    >
      {tiles.map((tile) => (
        <img
          key={tile.key}
          src={tile.src}
          alt=""
          draggable={false}
          loading="lazy"
          width={TILE_SIZE}
          height={TILE_SIZE}
          className="pointer-events-none absolute max-w-none"
          style={{ left: tile.left, top: tile.top }}
        />
      ))}

      <ul aria-label="Map pins">
        {pins.map((pin) => {
          const at = project(pin.position, zoom)
          const style = { left: at.x - origin.x, top: at.y - origin.y }
          const dot = cn(
            'absolute block -translate-x-1/2 -translate-y-1/2 rounded-full shadow ring-2',
            pinSizes[pin.size ?? 'md'],
            pin.selected && 'z-10 shadow-lifted outline-4 outline-offset-2 outline-brand-600/70',
            pin.variant === 'ring' ? 'ring-[3px]' : 'border-2 border-white',
            toneClasses[pin.tone],
            // Last, so tailwind-merge lets it replace the tone's fill: a ring is hollow.
            pin.variant === 'ring' && 'bg-white/70',
          )
          return (
            <li key={pin.id}>
              {pin.href ? (
                <Link to={pin.href} aria-label={pin.label} title={pin.label} className={cn(dot, 'hover:scale-110')} style={style} />
              ) : (
                <span role="img" aria-label={pin.label} title={pin.label} className={dot} style={style} />
              )}
            </li>
          )
        })}
      </ul>

      <div className="absolute top-2 right-2 flex flex-col overflow-hidden rounded-md border border-border-strong bg-surface-card shadow-raised">
        <button type="button" aria-label="Zoom in" className="size-8 text-lg leading-none hover:bg-surface-sunken disabled:text-stone-400" disabled={zoom >= MAX_ZOOM} onClick={() => changeZoom(1)}>
          +
        </button>
        <button type="button" aria-label="Zoom out" className="size-8 border-t border-border-strong text-lg leading-none hover:bg-surface-sunken disabled:text-stone-400" disabled={zoom <= MIN_ZOOM} onClick={() => changeZoom(-1)}>
          −
        </button>
      </div>

      <p className="absolute right-0 bottom-0 rounded-tl-md bg-surface-card/85 px-1.5 text-xs text-stone-700">
        ©{' '}
        <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer" className="underline">
          OpenStreetMap
        </a>{' '}
        contributors
      </p>
    </div>
  )
}
