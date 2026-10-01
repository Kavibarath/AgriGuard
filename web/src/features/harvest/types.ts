// Mirrors Application/Harvest/HarvestContracts.cs and the harvest report in Application/Reports.

export type ForecastSource = 'Farmer' | 'Agronomist' | 'Computed'

export interface HarvestForecast {
  id: string
  cropCycleId: string
  plotCode: string
  cropName: string
  farmerName: string
  forecastHarvestDate: string
  estimatedYieldKg: number
  actualYieldKg: number | null
  source: ForecastSource
  notes: string | null
  createdAt: string
}

export interface ForecastAccuracy {
  forecasts: number
  forecastKg: number
  actualKg: number
  variancePercent: number | null
  meanAbsolutePercentError: number | null
  withinTolerance: number
}

export interface ForecastAccuracyGroup {
  key: string
  label: string
  accuracy: ForecastAccuracy
}

export interface ForecastVsActualRow {
  forecastId: string
  cropCycleId: string
  plotCode: string
  cropName: string
  farmerName: string
  districtName: string
  source: ForecastSource
  forecastHarvestDate: string
  actualHarvestDate: string | null
  estimatedYieldKg: number
  actualYieldKg: number | null
  variancePercent: number | null
}

export interface ForecastVsActualReport {
  from: string | null
  to: string | null
  forecastCount: number
  pendingActuals: number
  tolerancePercent: number
  overall: ForecastAccuracy
  byCrop: ForecastAccuracyGroup[]
  bySource: ForecastAccuracyGroup[]
  rows: ForecastVsActualRow[]
}

// ── Collection ───────────────────────────────────────────────────────────────

export interface CollectionCentre {
  id: string
  name: string
  districtId: string
  districtName: string
  latitude: number
  longitude: number
  dailyCapacityKg: number
}

export interface CollectionSlot {
  id: string
  centreId: string
  centreName: string
  slotDate: string
  slotIndex: number
  startTime: string
  endTime: string
  capacityKg: number
  bookedKg: number
  remainingKg: number
}

export type BookingStatus = 'Booked' | 'CheckedIn' | 'Completed' | 'Cancelled' | 'NoShow'

export interface CollectionBooking {
  id: string
  bookingNo: string
  status: BookingStatus
  slotId: string
  centreName: string
  slotDate: string
  startTime: string
  endTime: string
  cropCycleId: string
  plotCode: string
  cropName: string
  farmerName: string
  quantityKg: number
  distanceKm: number
  createdAt: string
}

// ── Spray window (GET /api/weather/spray-window), judged exactly as rule V8 judges a spray day ──

export interface SprayDay {
  date: string
  suitable: boolean
  rainProbabilityPercent: number
  windSpeedKph: number
  temperatureC: number
  precipitationMm: number
  /** Why the day does not suit spraying; empty when it does. */
  problems: string[]
}

export interface SprayWindow {
  plotId: string
  plotCode: string
  /** False when the forecast could not be fetched; `days` is then empty and nothing is guessed. */
  forecastAvailable: boolean
  source: string
  fetchedAt: string | null
  rainfastHours: number
  productName: string | null
  summary: string
  recentRainMm: number | null
  recentHumidityPercent: number | null
  days: SprayDay[]
  thresholds: { maxRainProbabilityPercent: number; maxWindSpeedKph: number; maxTemperatureC: number }
}
