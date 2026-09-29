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

// ── Safety profile and treatment history (GET /api/plots/{id}/safety-profile, /api/reports/plot-treatment-history)

export type ApplicationStatus = 'Scheduled' | 'Applied' | 'Cancelled'

/** Why a product cannot be sprayed today; `None` means it can. Mirrors the domain's SprayBlock. */
export type SprayBlock = 'None' | 'PreHarvestInterval' | 'MaxApplicationsReached' | 'MinimumInterval'

/** Short labels for the table; the API's `blockedExplanation` carries the full sentence. */
export const sprayBlockLabels: Record<SprayBlock, string> = {
  None: 'Can spray today',
  PreHarvestInterval: 'Too close to harvest',
  MaxApplicationsReached: 'Season limit used',
  MinimumInterval: 'Too soon after last spray',
}

/** The rule each block enforces, so the page names the same rule the validator reports. */
export const sprayBlockRule: Partial<Record<SprayBlock, string>> = {
  PreHarvestInterval: 'V5',
  MaxApplicationsReached: 'V6',
  MinimumInterval: 'V7',
}

export interface ProductWindow {
  productId: string
  productName: string
  activeIngredientId: string
  activeIngredientName: string
  resistanceGroup: string | null
  isRestricted: boolean
  preHarvestIntervalDays: number
  applicationsUsed: number
  maxApplicationsPerCycle: number
  applicationsRemaining: number
  lastAppliedOn: string | null
  lastSafeSprayDate: string
  earliestNextApplication: string | null
  canSprayToday: boolean
  blockedReason: SprayBlock
  blockedExplanation: string | null
}

export interface IngredientUsage {
  activeIngredientId: string
  name: string
  resistanceGroup: string | null
  applicationCount: number
  lastAppliedOn: string | null
  daysSinceLastApplication: number | null
}

export interface AppliedTreatment {
  id: string
  productId: string
  productName: string
  activeIngredientName: string
  applicationDate: string
  dosePerHectare: number
  totalQuantity: number
  status: ApplicationStatus
}

/** Cycle fields are null when nothing is growing: there is no harvest date to measure against. */
export interface PlotSafetyProfile {
  plotId: string
  plotCode: string
  farmName: string
  areaHectares: number
  cropCycleId: string | null
  cropName: string | null
  stage: CropStage | null
  sownDate: string | null
  harvestDate: string | null
  daysToHarvest: number | null
  applications: AppliedTreatment[]
  ingredientUsage: IngredientUsage[]
  productWindows: ProductWindow[]
  phiBlockedSprayDates: string[]
  reEntryClearAtUtc: string | null
  generatedAtUtc: string
}

export interface TreatmentRow {
  applicationId: string
  applicationDate: string
  cropName: string
  cycleSownDate: string
  productName: string
  activeIngredient: string
  resistanceGroup: string | null
  unit: 'Litre' | 'Kilogram'
  dosePerHectare: number
  totalQuantity: number
  status: ApplicationStatus
  prescriptionNo: string | null
  preHarvestIntervalDays: number | null
  safeToHarvestFrom: string | null
}

export interface ActiveIngredientUse {
  activeIngredient: string
  resistanceGroup: string | null
  applications: number
  lastApplied: string
}

export interface PlotTreatmentHistory {
  plotId: string
  plotCode: string
  farmName: string
  areaHectares: number
  from: string | null
  to: string | null
  /** Cancelled sprays are listed in `rows` but not counted here or in `byActiveIngredient`. */
  applications: number
  byActiveIngredient: ActiveIngredientUse[]
  /** Newest first. */
  rows: TreatmentRow[]
}
