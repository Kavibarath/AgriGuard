// Mirrors Application/Intelligence/IntelligenceContracts.cs. Enums arrive as names.

export type PressureLevel = 'None' | 'Low' | 'Moderate' | 'High' | 'Severe'
export type PressureTrend = 'Rising' | 'Steady' | 'Falling'

export const levelLabels: Record<PressureLevel, string> = {
  None: 'No pressure',
  Low: 'Low',
  Moderate: 'Moderate',
  High: 'High',
  Severe: 'Severe',
}

export const trendLabels: Record<PressureTrend, string> = {
  Rising: '↑ Rising',
  Steady: '→ Steady',
  Falling: '↓ Falling',
}

export interface PathogenPressure {
  code: string
  name: string
  confirmedCases: number
  score: number
  sharePercent: number
  lastReportedOn: string
}

export interface DailyCaseCount {
  date: string
  reportedCases: number
  confirmedCases: number
}

export interface DistrictPressure {
  districtId: string
  districtName: string
  reportedCases: number
  confirmedCases: number
  score: number
  pressureIndex: number
  level: PressureLevel
  topPathogenCode: string | null
  latitude: number
  longitude: number
}

export interface OutbreakSignal {
  cropId: string | null
  cropName: string | null
  districtId: string | null
  districtName: string | null
  from: string
  to: string
  windowDays: number
  reportedCases: number
  confirmedCases: number
  score: number
  pressureIndex: number
  level: PressureLevel
  trend: PressureTrend
  summary: string
  topPathogens: PathogenPressure[]
  daily: DailyCaseCount[]
  districts: DistrictPressure[]
}
