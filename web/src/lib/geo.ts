/**
 * The little map maths the case map needs: Web Mercator projection (the projection OpenStreetMap's
 * tiles are cut in), a zoom that fits a set of points, and great-circle distance.
 */

export const TILE_SIZE = 256

export interface LatLng {
  lat: number
  lng: number
}

export interface Point {
  x: number
  y: number
}

/**
 * World pixel coordinates at `zoom`: at zoom z the whole world is 256·2^z pixels square, and tile
 * (x, y) covers pixels [256x, 256x + 256). Latitude is clamped just short of the poles, where
 * Mercator runs to infinity.
 */
export function project({ lat, lng }: LatLng, zoom: number): Point {
  const scale = TILE_SIZE * 2 ** zoom
  const sin = Math.min(Math.max(Math.sin((lat * Math.PI) / 180), -0.9999), 0.9999)
  return {
    x: ((lng + 180) / 360) * scale,
    y: (0.5 - Math.log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * scale,
  }
}

/** The closest zoom (highest number) at which every point fits inside the box, less a margin. */
export function fitZoom(points: LatLng[], width: number, height: number, { padding = 32, minZoom = 3, maxZoom = 16 } = {}): number {
  if (points.length === 0) return minZoom
  for (let zoom = maxZoom; zoom > minZoom; zoom--) {
    const projected = points.map((p) => project(p, zoom))
    const spanX = Math.max(...projected.map((p) => p.x)) - Math.min(...projected.map((p) => p.x))
    const spanY = Math.max(...projected.map((p) => p.y)) - Math.min(...projected.map((p) => p.y))
    if (spanX <= width - 2 * padding && spanY <= height - 2 * padding) return zoom
  }
  return minZoom
}

/** The middle of the points' bounding box, in world pixels at `zoom`. */
export function centreOf(points: LatLng[], zoom: number): Point {
  const projected = points.map((p) => project(p, zoom))
  const xs = projected.map((p) => p.x)
  const ys = projected.map((p) => p.y)
  return { x: (Math.min(...xs) + Math.max(...xs)) / 2, y: (Math.min(...ys) + Math.max(...ys)) / 2 }
}

const EARTH_RADIUS_M = 6_371_008.8

/** Great-circle (haversine) distance in metres. */
export function distanceMetres(a: LatLng, b: LatLng): number {
  const rad = (deg: number) => (deg * Math.PI) / 180
  const dLat = rad(b.lat - a.lat)
  const dLng = rad(b.lng - a.lng)
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a.lat)) * Math.cos(rad(b.lat)) * Math.sin(dLng / 2) ** 2
  return 2 * EARTH_RADIUS_M * Math.asin(Math.min(1, Math.sqrt(h)))
}

/** "40 m", "1.2 km": a phone's GPS is good to tens of metres, so no finer. */
export function formatDistance(metres: number): string {
  return metres < 1000 ? `${Math.round(metres / 10) * 10} m` : `${(metres / 1000).toFixed(1)} km`
}
