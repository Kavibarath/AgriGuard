import { http, HttpResponse } from 'msw'
import { API_BASE_URL } from '@/lib/api'
import type { CropCycle, Farm, PagedResult, Plot } from '@/features/registry/types'

export const districts = [
  { id: 'd-nuw', code: 'NUW', name: 'Nuwara Eliya', province: 'Central' },
  { id: 'd-mtl', code: 'MTL', name: 'Matale', province: 'Central' },
]

export const crops = [
  { id: 'c-tom', code: 'TOM', name: 'Tomato', scientificName: 'Solanum lycopersicum', maturityDays: 110 },
  { id: 'c-chi', code: 'CHI', name: 'Chilli', scientificName: 'Capsicum annuum', maturityDays: 150 },
]

export const farmerId = '01a0aaaf-a87a-736a-a322-4f4782a9b595' // matches the `farmer` fixture

export function makeFarm(overrides: Partial<Farm> = {}): Farm {
  return {
    id: 'farm-1',
    name: 'Green Acres',
    village: 'Hatton',
    districtId: 'd-nuw',
    districtName: 'Nuwara Eliya',
    farmerId,
    farmerName: 'Sunil Perera',
    plotCount: 2,
    totalAreaHectares: 2.05,
    createdAt: '2026-09-01T00:00:00Z',
    ...overrides,
  }
}

export function makePlot(overrides: Partial<Plot> = {}): Plot {
  return {
    id: 'plot-1',
    farmId: 'farm-1',
    farmName: 'Green Acres',
    plotCode: 'P-01',
    name: 'North field',
    areaHectares: 0.8,
    latitude: 6.9497,
    longitude: 80.7891,
    soilType: 'Loam',
    status: 'Active',
    activeCycle: null,
    ...overrides,
  }
}

export function makeCycle(overrides: Partial<CropCycle> = {}): CropCycle {
  return {
    id: 'cycle-1',
    plotId: 'plot-1',
    plotCode: 'P-01',
    cropId: 'c-tom',
    cropName: 'Tomato',
    cropMaturityDays: 110,
    sownDate: '2026-07-01',
    stage: 'Vegetative',
    status: 'Active',
    expectedHarvestDate: '2026-10-19',
    plannedHarvestDate: null,
    actualHarvestDate: null,
    effectiveHarvestDate: '2026-10-19',
    daysToHarvest: 28,
    allowedNextStages: ['Flowering'],
    transitions: [
      { fromStage: 'Sown', toStage: 'Vegetative', transitionedAt: '2026-07-25T00:00:00Z', note: 'First true leaves' },
    ],
    ...overrides,
  }
}

export function paged<T>(items: T[], overrides: Partial<PagedResult<T>> = {}): PagedResult<T> {
  const pageSize = overrides.pageSize ?? 20
  const totalCount = overrides.totalCount ?? items.length
  return {
    items,
    page: overrides.page ?? 1,
    pageSize,
    totalCount,
    totalPages: Math.ceil(totalCount / pageSize),
    hasPreviousPage: (overrides.page ?? 1) > 1,
    hasNextPage: (overrides.page ?? 1) < Math.ceil(totalCount / pageSize),
    ...overrides,
  }
}

/** Default registry handlers: one farm with two plots, one of them growing tomato. */
export const registryHandlers = [
  http.get(`${API_BASE_URL}/api/districts`, () => HttpResponse.json(districts)),
  http.get(`${API_BASE_URL}/api/crops`, () => HttpResponse.json(crops)),
  http.get(`${API_BASE_URL}/api/farms`, () => HttpResponse.json(paged([makeFarm()]))),
  http.get(`${API_BASE_URL}/api/farms/:id`, () => HttpResponse.json(makeFarm())),
  http.get(`${API_BASE_URL}/api/plots`, () =>
    HttpResponse.json(
      paged([
        makePlot({ activeCycle: { id: 'cycle-1', cropId: 'c-tom', cropName: 'Tomato', sownDate: '2026-07-01', stage: 'Vegetative', status: 'Active', expectedHarvestDate: '2026-10-19', plannedHarvestDate: null } }),
        makePlot({ id: 'plot-2', plotCode: 'P-02', name: 'River field', areaHectares: 1.25 }),
      ]),
    ),
  ),
  http.get(`${API_BASE_URL}/api/crop-cycles/:id`, () => HttpResponse.json(makeCycle())),
]
