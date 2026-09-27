import { api } from '@/lib/api'
import { queryString } from '@/lib/query-string'
import type { PagedResult } from '@/features/registry/types'
import type { CropRule, InventoryBatch, Order, OrderStatus, Product, Reservation, ReservationStatus, RuleLimits } from './types'

interface PageQuery {
  page?: number
  pageSize?: number
  sortBy?: string
  desc?: boolean
}

export interface ProductQuery extends PageQuery {
  search?: string
  cropId?: string
  activeIngredientId?: string
  includeInactive?: boolean
}

export interface InventoryQuery extends PageQuery {
  productId?: string
  expiringBefore?: string
  search?: string
  includeEmpty?: boolean
}

export interface ReservationQuery extends PageQuery {
  status?: ReservationStatus
}

export interface OrderQuery extends PageQuery {
  status?: OrderStatus
  search?: string
}

export interface RuleQuery extends PageQuery {
  search?: string
  productId?: string
  cropId?: string
  isActive?: boolean
}

// ── Catalogue ────────────────────────────────────────────────────────────────

export const listProducts = (query: ProductQuery) => api<PagedResult<Product>>(`/api/products${queryString(query)}`)

// ── Dealer stock ─────────────────────────────────────────────────────────────

export const listBatches = (query: InventoryQuery) => api<PagedResult<InventoryBatch>>(`/api/inventory${queryString(query)}`)

export interface NewBatch {
  productId: string
  batchNo: string
  expiryDate: string
  quantityOnHand: number
  unitPrice: number
}

export const createBatch = (input: NewBatch) => api<InventoryBatch>('/api/inventory/batches', { method: 'POST', json: input })

export interface BatchCorrection {
  expiryDate: string
  quantityOnHand: number
  unitPrice: number
}

export const updateBatch = (id: string, input: BatchCorrection) =>
  api<InventoryBatch>(`/api/inventory/batches/${id}`, { method: 'PUT', json: input })

// ── Holds ────────────────────────────────────────────────────────────────────

export const listReservations = (query: ReservationQuery) =>
  api<PagedResult<Reservation>>(`/api/inventory/reservations${queryString(query)}`)

export const createReservation = (input: { productId: string; quantity: number; usableOn?: string | null; note?: string | null }) =>
  api<Reservation>('/api/inventory/reservations', { method: 'POST', json: input })

export const commitReservation = (id: string) => api<Reservation>(`/api/inventory/reservations/${id}/commit`, { method: 'POST' })
export const releaseReservation = (id: string) => api<Reservation>(`/api/inventory/reservations/${id}/release`, { method: 'POST' })

// ── Orders ───────────────────────────────────────────────────────────────────

export const listOrders = (query: OrderQuery) => api<PagedResult<Order>>(`/api/orders${queryString(query)}`)

/** Names the target status, so a repeated click is harmless on the server. */
export const fulfilOrder = (id: string, status: OrderStatus) =>
  api<Order>(`/api/orders/${id}/fulfil`, { method: 'POST', json: { status } })

// ── Regulatory rules ─────────────────────────────────────────────────────────

export const listRules = (query: RuleQuery) => api<PagedResult<CropRule>>(`/api/product-crop-approvals${queryString(query)}`)

export const createRule = (input: RuleLimits & { productId: string; cropId: string }) =>
  api<CropRule>('/api/product-crop-approvals', { method: 'POST', json: input })

export const updateRule = (id: string, input: RuleLimits) =>
  api<CropRule>(`/api/product-crop-approvals/${id}`, { method: 'PUT', json: input })
