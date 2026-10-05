import { http, HttpResponse } from 'msw'
import { API_BASE_URL } from '@/lib/api'
import type { UserSummary } from '@/features/auth/types'
import type { CropRule, InventoryBatch, Order, Product, Reservation } from '@/features/inventory/types'
import { paged } from './registry-handlers'

export const dealer: UserSummary = {
  id: '01a0aaaf-a87a-736a-a322-4f4782a9b596',
  email: 'dealer@agriguard.demo',
  fullName: 'Kamal Silva',
  role: 'AgroDealer',
  districtId: '0199e7c2-3b1e-7f0a-9c1d-0d5f5a1c2b3e',
}

export const admin: UserSummary = {
  id: '01a0aaaf-a87a-736a-a322-4f4782a9b597',
  email: 'admin@agriguard.demo',
  fullName: 'Co-op Admin',
  role: 'CoopAdministrator',
  districtId: null,
}

export function makeProduct(overrides: Partial<Product> = {}): Product {
  return {
    id: 'p-mancozeb',
    name: 'Mancozeb 80 WP',
    manufacturer: 'Agro Lanka',
    activeIngredientId: 'ai-mancozeb',
    activeIngredientName: 'Mancozeb',
    resistanceGroup: 'M3',
    formulation: 'WP',
    unit: 'Kilogram',
    packSize: 1,
    unitPrice: 2400,
    isActive: true,
    approvedCropCount: 4,
    ...overrides,
  }
}

export function makeBatch(overrides: Partial<InventoryBatch> = {}): InventoryBatch {
  return {
    id: 'batch-1',
    dealerId: 'dealer-1',
    shopName: 'Kandy Agro Supplies',
    productId: 'p-mancozeb',
    productName: 'Mancozeb 80 WP',
    unit: 'Kilogram',
    packSize: 1,
    batchNo: 'MZ-2601',
    expiryDate: '2027-11-27',
    quantityOnHand: 40,
    quantityReserved: 3,
    quantityAvailable: 37,
    unitPrice: 2400,
    daysToExpiry: 426,
    expiryState: 'InDate',
    ...overrides,
  }
}

export function makeReservation(overrides: Partial<Reservation> = {}): Reservation {
  return {
    id: 'hold-1',
    dealerId: 'dealer-1',
    shopName: 'Kandy Agro Supplies',
    productId: 'p-mancozeb',
    productName: 'Mancozeb 80 WP',
    unit: 'Kilogram',
    totalQuantity: 3,
    packs: 3,
    status: 'Held',
    createdAt: '2026-09-27T08:00:00Z',
    expiresAt: '2026-09-28T08:00:00Z',
    resolvedAt: null,
    agentRunId: null,
    note: 'Phone order, Mr Silva',
    lines: [{ batchId: 'batch-1', batchNo: 'MZ-2601', expiryDate: '2027-11-27', quantity: 3 }],
    ...overrides,
  }
}

export function makeOrder(overrides: Partial<Order> = {}): Order {
  return {
    id: 'order-1',
    orderNo: 'ORD-2026-000001',
    status: 'Confirmed',
    nextStatus: 'Packed',
    dealerId: 'dealer-1',
    shopName: 'Kandy Agro Supplies',
    farmerId: 'farmer-1',
    farmerName: 'Sunil Perera',
    farmerPhone: '+94 77 123 4567',
    prescriptionNo: 'RX-2026-000001',
    sprayDate: '2026-09-28',
    totalAmount: 4800,
    paymentStatus: 'Unpaid',
    paidAt: null,
    paidBy: null,
    cardBrand: null,
    cardLast4: null,
    createdAt: '2026-09-27T09:00:00Z',
    confirmedAt: '2026-09-27T09:00:00Z',
    packedAt: null,
    collectedAt: null,
    lines: [{ productId: 'p-mancozeb', productName: 'Mancozeb 80 WP', unit: 'Kilogram', packs: 2, quantity: 2, unitPrice: 2400, lineTotal: 4800 }],
    ...overrides,
  }
}

export function makeRule(overrides: Partial<CropRule> = {}): CropRule {
  return {
    id: 'rule-1',
    productId: 'p-mancozeb',
    productName: 'Mancozeb 80 WP',
    activeIngredientName: 'Mancozeb',
    unit: 'Kilogram',
    cropId: 'c-tom',
    cropName: 'Tomato',
    minDosePerHectare: 1.5,
    maxDosePerHectare: 2.5,
    preHarvestIntervalDays: 7,
    reEntryIntervalHours: 24,
    maxApplicationsPerCycle: 4,
    minDaysBetweenApplications: 7,
    rainfastHours: 4,
    isRestricted: false,
    isActive: true,
    updatedAt: '2026-09-15T09:20:00Z',
    ...overrides,
  }
}

/** Default Component C handlers: one shop with two batches (one close to expiry), one hold, one order, one rule. */
export const inventoryHandlers = [
  http.get(`${API_BASE_URL}/api/products`, () => HttpResponse.json(paged([makeProduct(), makeProduct({ id: 'p-chloro', name: 'Chlorothalonil 75 WP' })]))),
  http.get(`${API_BASE_URL}/api/inventory`, ({ request }) => {
    const soon = makeBatch({ id: 'batch-2', batchNo: 'MZ-2512', quantityOnHand: 3, quantityReserved: 0, quantityAvailable: 3, expiryDate: '2026-10-07', daysToExpiry: 10, expiryState: 'ExpiringSoon' })
    return new URL(request.url).searchParams.has('expiringBefore')
      ? HttpResponse.json(paged([soon]))
      : HttpResponse.json(paged([soon, makeBatch()]))
  }),
  http.get(`${API_BASE_URL}/api/inventory/reservations`, () => HttpResponse.json(paged([makeReservation()]))),
  http.get(`${API_BASE_URL}/api/orders`, () => HttpResponse.json(paged([makeOrder()]))),
  http.get(`${API_BASE_URL}/api/product-crop-approvals`, () => HttpResponse.json(paged([makeRule()]))),
]
