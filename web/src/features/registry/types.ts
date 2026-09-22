// Mirrors the API's registry DTOs. Enums arrive as names.

export const CropStage = {
  Sown: 'Sown',
  Vegetative: 'Vegetative',
  Flowering: 'Flowering',
  FruitSet: 'FruitSet',
  PreHarvest: 'PreHarvest',
  Harvested: 'Harvested',
} as const
export type CropStage = (typeof CropStage)[keyof typeof CropStage]

/** Display names; the enum values are wire values and not all of them read well raw. */
export const stageLabels: Record<CropStage, string> = {
  Sown: 'Sown',
  Vegetative: 'Vegetative',
  Flowering: 'Flowering',
  FruitSet: 'Fruit set',
  PreHarvest: 'Pre-harvest',
  Harvested: 'Harvested',
}

export type PlotStatus = 'Active' | 'Fallow' | 'Retired'
export type CropCycleStatus = 'Active' | 'Harvested' | 'Abandoned'
export type SoilType = 'Clay' | 'Loam' | 'SandyLoam' | 'SiltLoam' | 'Laterite' | 'Peat'

export const soilLabels: Record<SoilType, string> = {
  Clay: 'Clay',
  Loam: 'Loam',
  SandyLoam: 'Sandy loam',
  SiltLoam: 'Silt loam',
  Laterite: 'Laterite',
  Peat: 'Peat',
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

export interface Farm {
  id: string
  name: string
  village: string | null
  districtId: string
  districtName: string
  farmerId: string
  farmerName: string
  plotCount: number
  totalAreaHectares: number
  createdAt: string
}

export interface CropCycleSummary {
  id: string
  cropId: string
  cropName: string
  sownDate: string
  stage: CropStage
  status: CropCycleStatus
  expectedHarvestDate: string
  plannedHarvestDate: string | null
}

export interface Plot {
  id: string
  farmId: string
  farmName: string
  plotCode: string
  name: string | null
  areaHectares: number
  latitude: number
  longitude: number
  soilType: SoilType
  status: PlotStatus
  activeCycle: CropCycleSummary | null
}

export interface StageTransition {
  fromStage: CropStage
  toStage: CropStage
  transitionedAt: string
  note: string | null
}

export interface CropCycle {
  id: string
  plotId: string
  plotCode: string
  cropId: string
  cropName: string
  cropMaturityDays: number
  sownDate: string
  stage: CropStage
  status: CropCycleStatus
  expectedHarvestDate: string
  plannedHarvestDate: string | null
  actualHarvestDate: string | null
  effectiveHarvestDate: string
  daysToHarvest: number
  allowedNextStages: CropStage[]
  transitions: StageTransition[]
}

export interface District {
  id: string
  code: string
  name: string
  province: string
}

export interface Crop {
  id: string
  code: string
  name: string
  scientificName: string | null
  maturityDays: number
}
