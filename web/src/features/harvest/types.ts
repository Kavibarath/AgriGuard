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
