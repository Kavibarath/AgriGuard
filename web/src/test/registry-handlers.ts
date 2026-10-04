import { http, HttpResponse } from 'msw'
import { API_BASE_URL } from '@/lib/api'
import type {
  CropCycle,
  Farm,
  PagedResult,
  Plot,
  PlotSafetyProfile,
  PlotTreatmentHistory,
} from '@/features/registry/types'

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

/** Tomato on P-01, harvest 19 Oct: Mancozeb blocked by its PHI, Azoxystrobin still sprayable. */
export function makeSafetyProfile(overrides: Partial<PlotSafetyProfile> = {}): PlotSafetyProfile {
  return {
    plotId: 'plot-1',
    plotCode: 'P-01',
    farmName: 'Green Acres',
    areaHectares: 0.8,
    cropCycleId: 'cycle-1',
    cropName: 'Tomato',
    stage: 'Vegetative',
    sownDate: '2026-07-01',
    harvestDate: '2026-10-19',
    daysToHarvest: 20,
    applications: [],
    ingredientUsage: [],
    productWindows: [
      {
        productId: 'p-azo',
        productName: 'Amistar 25 SC',
        activeIngredientId: 'ai-azo',
        activeIngredientName: 'Azoxystrobin',
        resistanceGroup: '11',
        isRestricted: false,
        preHarvestIntervalDays: 3,
        applicationsUsed: 1,
        maxApplicationsPerCycle: 3,
        applicationsRemaining: 2,
        lastAppliedOn: '2026-09-10',
        lastSafeSprayDate: '2026-10-16',
        earliestNextApplication: '2026-09-24',
        canSprayToday: true,
        blockedReason: 'None',
        blockedExplanation: null,
      },
      {
        productId: 'p-man',
        productName: 'Dithane M-45',
        activeIngredientId: 'ai-man',
        activeIngredientName: 'Mancozeb',
        resistanceGroup: 'M3',
        isRestricted: true,
        preHarvestIntervalDays: 21,
        applicationsUsed: 0,
        maxApplicationsPerCycle: 4,
        applicationsRemaining: 4,
        lastAppliedOn: null,
        lastSafeSprayDate: '2026-09-28',
        earliestNextApplication: null,
        canSprayToday: false,
        blockedReason: 'PreHarvestInterval',
        blockedExplanation: 'Too close to harvest: Dithane M-45 needs 21 days before picking, so the last safe spray was 2026-09-28.',
      },
    ],
    phiBlockedSprayDates: ['2026-10-17', '2026-10-18', '2026-10-19'],
    reEntryClearAtUtc: null,
    generatedAtUtc: '2026-09-29T04:00:00Z',
    ...overrides,
  }
}

export function makeTreatmentHistory(overrides: Partial<PlotTreatmentHistory> = {}): PlotTreatmentHistory {
  return {
    plotId: 'plot-1',
    plotCode: 'P-01',
    farmName: 'Green Acres',
    areaHectares: 0.8,
    from: null,
    to: null,
    applications: 2,
    byActiveIngredient: [{ activeIngredient: 'Azoxystrobin', resistanceGroup: '11', applications: 2, lastApplied: '2026-09-10' }],
    rows: [
      {
        applicationId: 'app-2',
        applicationDate: '2026-09-10',
        cropName: 'Tomato',
        cycleSownDate: '2026-07-01',
        productName: 'Amistar 25 SC',
        activeIngredient: 'Azoxystrobin',
        resistanceGroup: '11',
        unit: 'Litre',
        dosePerHectare: 0.5,
        totalQuantity: 0.4,
        status: 'Applied',
        prescriptionNo: 'RX-2026-000002',
        preHarvestIntervalDays: 3,
        safeToHarvestFrom: '2026-09-13',
      },
      {
        applicationId: 'app-1',
        applicationDate: '2026-08-20',
        cropName: 'Tomato',
        cycleSownDate: '2026-07-01',
        productName: 'Amistar 25 SC',
        activeIngredient: 'Azoxystrobin',
        resistanceGroup: '11',
        unit: 'Litre',
        dosePerHectare: 0.5,
        totalQuantity: 0.4,
        status: 'Cancelled',
        prescriptionNo: null,
        preHarvestIntervalDays: 3,
        safeToHarvestFrom: null,
      },
      {
        applicationId: 'app-0',
        applicationDate: '2026-08-01',
        cropName: 'Tomato',
        cycleSownDate: '2026-07-01',
        productName: 'Amistar 25 SC',
        activeIngredient: 'Azoxystrobin',
        resistanceGroup: '11',
        unit: 'Litre',
        dosePerHectare: 0.5,
        totalQuantity: 0.4,
        status: 'Applied',
        prescriptionNo: 'RX-2026-000001',
        preHarvestIntervalDays: 3,
        safeToHarvestFrom: '2026-08-04',
      },
    ],
    ...overrides,
  }
}

const tomatoCycle = {
  id: 'cycle-1',
  cropId: 'c-tom',
  cropName: 'Tomato',
  sownDate: '2026-07-01',
  stage: 'Vegetative',
  status: 'Active',
  expectedHarvestDate: '2026-10-19',
  plannedHarvestDate: null,
} as const

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
        makePlot({ activeCycle: tomatoCycle }),
        makePlot({ id: 'plot-2', plotCode: 'P-02', name: 'River field', areaHectares: 1.25 }),
      ]),
    ),
  ),
  http.get(`${API_BASE_URL}/api/plots/:id`, () => HttpResponse.json(makePlot({ activeCycle: tomatoCycle }))),
  http.get(`${API_BASE_URL}/api/plots/:id/safety-profile`, () => HttpResponse.json(makeSafetyProfile())),
  http.get(`${API_BASE_URL}/api/reports/plot-treatment-history`, () => HttpResponse.json(makeTreatmentHistory())),
  http.get(`${API_BASE_URL}/api/crop-cycles/:id`, () => HttpResponse.json(makeCycle())),
]
