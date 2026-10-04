import { centreOf, distanceMetres, fitZoom, formatDistance, project } from './geo'

describe('project', () => {
  it('puts the equator and the prime meridian in the middle of the world', () => {
    expect(project({ lat: 0, lng: 0 }, 0)).toEqual({ x: 128, y: 128 })
    expect(project({ lat: 0, lng: 0 }, 1)).toEqual({ x: 256, y: 256 })
  })

  it('lands Nuwara Eliya in the OpenStreetMap tile that shows it', () => {
    const { x, y } = project({ lat: 6.9497, lng: 80.7891 }, 12)
    // Tile 12/2967/1968 covers Nuwara Eliya on openstreetmap.org.
    expect([Math.floor(x / 256), Math.floor(y / 256)]).toEqual([2967, 1968])
  })
})

describe('fitZoom', () => {
  const nuwaraEliya = { lat: 6.9497, lng: 80.7891 }
  const anuradhapura = { lat: 8.351, lng: 80.504 }

  it('zooms right in on a single point, up to the cap', () => {
    expect(fitZoom([nuwaraEliya], 600, 300)).toBe(16)
  })

  it('zooms out until two districts both fit', () => {
    const zoom = fitZoom([nuwaraEliya, anuradhapura], 600, 300)
    const [a, b] = [project(nuwaraEliya, zoom), project(anuradhapura, zoom)]
    expect(Math.abs(a.y - b.y)).toBeLessThanOrEqual(300 - 64)
    const tighter = [project(nuwaraEliya, zoom + 1), project(anuradhapura, zoom + 1)]
    expect(Math.abs(tighter[0].y - tighter[1].y)).toBeGreaterThan(300 - 64)
  })

  it('centres between the points', () => {
    const c = centreOf([nuwaraEliya, anuradhapura], 8)
    const [a, b] = [project(nuwaraEliya, 8), project(anuradhapura, 8)]
    expect(c.y).toBeCloseTo((a.y + b.y) / 2)
  })
})

describe('distanceMetres', () => {
  it('measures a degree of latitude as about 111 km', () => {
    expect(distanceMetres({ lat: 7, lng: 80 }, { lat: 8, lng: 80 })).toBeCloseTo(111_195, -2)
  })

  it('is zero for the same point', () => {
    expect(distanceMetres({ lat: 6.95, lng: 80.79 }, { lat: 6.95, lng: 80.79 })).toBe(0)
  })

  it('formats to the precision a phone GPS has', () => {
    expect(formatDistance(43)).toBe('40 m')
    expect(formatDistance(1234)).toBe('1.2 km')
  })
})
