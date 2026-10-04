// Mirrors the API's Component C DTOs (InventoryContracts.cs). Enums arrive as names.

export type ProductUnit = 'Litre' | 'Kilogram'
export type Formulation = 'EC' | 'SC' | 'WP' | 'WG' | 'SL' | 'GR'
export type ExpiryState = 'InDate' | 'ExpiringSoon' | 'Expired'
export type ReservationStatus = 'Held' | 'Committed' | 'Released' | 'Expired'
export type OrderStatus = 'Draft' | 'Confirmed' | 'Packed' | 'Collected' | 'Cancelled'

export const unitLabels: Record<ProductUnit, string> = { Litre: 'L', Kilogram: 'kg' }

export const expiryLabels: Record<ExpiryState, string> = {
  InDate: 'In date',
  ExpiringSoon: 'Expiring soon',
  Expired: 'Expired',
}

export const orderStatusLabels: Record<OrderStatus, string> = {
  Draft: 'Draft',
  Confirmed: 'Ready to pack',
  Packed: 'Packed — awaiting collection',
  Collected: 'Collected',
  Cancelled: 'Cancelled',
}

/** The dealer's one button per order: what it says for the step it performs. */
export const fulfilActionLabels: Partial<Record<OrderStatus, string>> = {
  Packed: 'Mark packed',
  Collected: 'Mark collected',
}

export interface Product {
  id: string
  name: string
  manufacturer: string | null
  activeIngredientId: string
  activeIngredientName: string
  resistanceGroup: string | null
  formulation: Formulation
  unit: ProductUnit
  packSize: number
  unitPrice: number
  isActive: boolean
  approvedCropCount: number
}

export interface InventoryBatch {
  id: string
  dealerId: string
  shopName: string
  productId: string
  productName: string
  unit: ProductUnit
  packSize: number
  batchNo: string
  expiryDate: string
  quantityOnHand: number
  quantityReserved: number
  quantityAvailable: number
  unitPrice: number
  daysToExpiry: number
  expiryState: ExpiryState
}

export interface ReservationLine {
  batchId: string
  batchNo: string
  expiryDate: string
  quantity: number
}

export interface Reservation {
  id: string
  dealerId: string
  shopName: string
  productId: string
  productName: string
  unit: ProductUnit
  totalQuantity: number
  packs: number
  status: ReservationStatus
  createdAt: string
  expiresAt: string
  resolvedAt: string | null
  agentRunId: string | null
  note: string | null
  lines: ReservationLine[]
}

export interface OrderLine {
  productId: string
  productName: string
  unit: ProductUnit
  packs: number
  quantity: number
  unitPrice: number
  lineTotal: number
}

export interface Order {
  id: string
  orderNo: string
  status: OrderStatus
  nextStatus: OrderStatus | null
  dealerId: string
  shopName: string
  farmerId: string
  farmerName: string
  farmerPhone: string | null
  prescriptionNo: string | null
  sprayDate: string | null
  totalAmount: number
  createdAt: string
  confirmedAt: string | null
  packedAt: string | null
  collectedAt: string | null
  lines: OrderLine[]
}

/** One row of the regulatory rules table. */
export interface CropRule {
  id: string
  productId: string
  productName: string
  activeIngredientName: string
  unit: ProductUnit
  cropId: string
  cropName: string
  minDosePerHectare: number
  maxDosePerHectare: number
  preHarvestIntervalDays: number
  reEntryIntervalHours: number
  maxApplicationsPerCycle: number
  minDaysBetweenApplications: number
  rainfastHours: number
  isRestricted: boolean
  isActive: boolean
  updatedAt: string
}

export type RuleLimits = Omit<
  CropRule,
  'id' | 'productId' | 'productName' | 'activeIngredientName' | 'unit' | 'cropId' | 'cropName' | 'updatedAt'
>
