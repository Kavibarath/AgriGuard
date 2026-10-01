import { http, HttpResponse } from 'msw'
import { API_BASE_URL } from '@/lib/api'
import type { CollectionBooking, CollectionCentre, CollectionSlot, ForecastVsActualReport, HarvestForecast, SprayWindow } from '@/features/harvest/types'
import type { OutbreakSignal } from '@/features/intelligence/types'
import type { LowStockReport, StockValuation } from '@/features/inventory/types'
import { paged } from './registry-handlers'

/** Fourteen days ending 2026-09-28, oldest first. */
const days = Array.from({ length: 14 }, (_, i) => `2026-09-${String(15 + i).padStart(2, '0')}`)

export function makeSignal(overrides: Partial<OutbreakSignal> = {}): OutbreakSignal {
  return {
    cropId: null,
    cropName: null,
    districtId: 'd-nuw',
    districtName: 'Nuwara Eliya',
    from: days[0],
    to: days[13],
    windowDays: 14,
    reportedCases: 12,
    confirmedCases: 10,
    score: 6.1,
    pressureIndex: 78,
    level: 'Severe',
    trend: 'Rising',
    summary: 'Late blight pressure is severe in Nuwara Eliya (index 78/100, rising): 10 confirmed of 12 reported in 14 days.',
    topPathogens: [
      { code: 'LATE_BLIGHT', name: 'Late blight', confirmedCases: 8, score: 5.2, sharePercent: 85, lastReportedOn: days[12] },
      { code: 'EARLY_BLIGHT', name: 'Early blight', confirmedCases: 2, score: 0.9, sharePercent: 15, lastReportedOn: days[3] },
    ],
    daily: days.map((date, i) => ({ date, reportedCases: i % 3 === 0 ? 2 : 1, confirmedCases: i % 3 === 0 ? 1 : 1 })),
    districts: [
      { districtId: 'd-nuw', districtName: 'Nuwara Eliya', reportedCases: 12, confirmedCases: 10, score: 6.1, pressureIndex: 78, level: 'Severe', topPathogenCode: 'LATE_BLIGHT', latitude: 7, longitude: 80.8 },
      { districtId: 'd-mtl', districtName: 'Matale', reportedCases: 3, confirmedCases: 2, score: 0.8, pressureIndex: 18, level: 'Low', topPathogenCode: 'THRIPS', latitude: 7.9, longitude: 80.6 },
    ],
    ...overrides,
  }
}

export function makeForecast(overrides: Partial<HarvestForecast> = {}): HarvestForecast {
  return {
    id: 'fc-1',
    cropCycleId: 'cycle-1',
    plotCode: 'P-01',
    cropName: 'Tomato',
    farmerName: 'Sunil Perera',
    forecastHarvestDate: '2026-10-06',
    estimatedYieldKg: 2400,
    actualYieldKg: null,
    source: 'Farmer',
    notes: null,
    createdAt: '2026-09-20T08:00:00Z',
    ...overrides,
  }
}

export function makeReport(overrides: Partial<ForecastVsActualReport> = {}): ForecastVsActualReport {
  return {
    from: null,
    to: null,
    forecastCount: 5,
    pendingActuals: 1,
    tolerancePercent: 10,
    overall: { forecasts: 4, forecastKg: 11_400, actualKg: 10_920, variancePercent: -4.2, meanAbsolutePercentError: 8.9, withinTolerance: 3 },
    byCrop: [
      { key: 'Big Onion', label: 'Big Onion', accuracy: { forecasts: 1, forecastKg: 4200, actualKg: 3600, variancePercent: -14.3, meanAbsolutePercentError: 14.3, withinTolerance: 0 } },
      { key: 'Tomato', label: 'Tomato', accuracy: { forecasts: 3, forecastKg: 7200, actualKg: 7320, variancePercent: 1.7, meanAbsolutePercentError: 7.1, withinTolerance: 3 } },
    ],
    bySource: [
      { key: 'Farmer', label: 'Farmer forecasts', accuracy: { forecasts: 2, forecastKg: 6600, actualKg: 5750, variancePercent: -12.9, meanAbsolutePercentError: 12.3, withinTolerance: 1 } },
      { key: 'Agronomist', label: 'Agronomist forecasts', accuracy: { forecasts: 2, forecastKg: 4800, actualKg: 5170, variancePercent: 7.7, meanAbsolutePercentError: 5.4, withinTolerance: 2 } },
    ],
    rows: [],
    ...overrides,
  }
}

export const centres: CollectionCentre[] = [
  { id: 'cc-nuw', name: 'Nuwara Eliya Economic Centre', districtId: 'd-nuw', districtName: 'Nuwara Eliya', latitude: 6.966, longitude: 80.77, dailyCapacityKg: 4500 },
  { id: 'cc-wel', name: 'Welimada Collection Point', districtId: 'd-nuw', districtName: 'Nuwara Eliya', latitude: 6.905, longitude: 80.913, dailyCapacityKg: 3000 },
]

export function makeSlot(overrides: Partial<CollectionSlot> = {}): CollectionSlot {
  return {
    id: 'slot-1',
    centreId: 'cc-nuw',
    centreName: 'Nuwara Eliya Economic Centre',
    slotDate: '2026-09-28',
    slotIndex: 1,
    startTime: '07:00:00',
    endTime: '09:00:00',
    capacityKg: 1500,
    bookedKg: 400,
    remainingKg: 1100,
    ...overrides,
  }
}

export function makeBooking(overrides: Partial<CollectionBooking> = {}): CollectionBooking {
  return {
    id: 'bk-1',
    bookingNo: 'BK-2026-000001',
    status: 'Booked',
    slotId: 'slot-1',
    centreName: 'Nuwara Eliya Economic Centre',
    slotDate: '2026-09-28',
    startTime: '07:00:00',
    endTime: '09:00:00',
    cropCycleId: 'cycle-1',
    plotCode: 'P-01',
    cropName: 'Tomato',
    farmerName: 'Sunil Perera',
    quantityKg: 400,
    distanceKm: 2.3,
    createdAt: '2026-09-26T08:00:00Z',
    ...overrides,
  }
}

export const stockValuation: StockValuation = {
  asOf: '2026-09-28',
  batches: 2,
  totalValue: 103_200,
  heldValue: 7200,
  expiredValue: 0,
  expiringSoonValue: 7200,
  byDealer: [{ dealerId: 'dealer-1', shopName: 'Kandy Agro Supplies', value: 103_200, expiredValue: 0 }],
  byProduct: [],
}

export const lowStock: LowStockReport = {
  asOf: '2026-09-28',
  minPacks: 3,
  outOfStock: 1,
  low: 2,
  rows: [],
}

/** Default Component D and report handlers: a severe late-blight signal, one forecast, one centre's slot and booking. */
/** Two dry days, then rain: what the spray-weather panel shows. */
export function makeSprayWindow(overrides: Partial<SprayWindow> = {}): SprayWindow {
  return {
    plotId: 'plot-1',
    plotCode: 'P-01',
    forecastAvailable: true,
    source: 'open-meteo',
    fetchedAt: '2026-09-29T02:00:00Z',
    rainfastHours: 4,
    productName: null,
    summary: 'Suitable on 29 Sep, 30 Sep; rain likely on 1 Oct.',
    recentRainMm: 12.5,
    recentHumidityPercent: 88,
    days: [
      { date: '2026-09-29', suitable: true, rainProbabilityPercent: 10, windSpeedKph: 8, temperatureC: 24, precipitationMm: 0, problems: [] },
      { date: '2026-09-30', suitable: true, rainProbabilityPercent: 20, windSpeedKph: 11, temperatureC: 25, precipitationMm: 0.1, problems: [] },
      { date: '2026-10-01', suitable: false, rainProbabilityPercent: 80, windSpeedKph: 14, temperatureC: 22, precipitationMm: 9.4, problems: ['Rain likely (80%, 9.4 mm)'] },
    ],
    thresholds: { maxRainProbabilityPercent: 40, maxWindSpeedKph: 15, maxTemperatureC: 32 },
    ...overrides,
  }
}

export const harvestHandlers = [
  http.get(`${API_BASE_URL}/api/weather/spray-window`, () => HttpResponse.json(makeSprayWindow())),
  http.get(`${API_BASE_URL}/api/intelligence/outbreak-signal`, () => HttpResponse.json(makeSignal())),
  http.get(`${API_BASE_URL}/api/harvest-forecasts`, () => HttpResponse.json(paged([makeForecast()]))),
  http.get(`${API_BASE_URL}/api/reports/harvest-forecast-vs-actual`, () => HttpResponse.json(makeReport())),
  http.get(`${API_BASE_URL}/api/collection-centres`, () => HttpResponse.json(centres)),
  http.get(`${API_BASE_URL}/api/collection-slots`, () => HttpResponse.json(paged([makeSlot()]))),
  http.get(`${API_BASE_URL}/api/collection-bookings`, () => HttpResponse.json(paged([makeBooking()]))),
  http.get(`${API_BASE_URL}/api/reports/stock-valuation`, () => HttpResponse.json(stockValuation)),
  http.get(`${API_BASE_URL}/api/reports/low-stock`, () => HttpResponse.json(lowStock)),
]
