import { unitLabels, type Order, type ProductUnit } from './types'

/** "LKR 4,800.00". Prices are shown to the cent, the way the dealer's till prints them. */
export function formatLkr(amount: number): string {
  return `LKR ${amount.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

/** "2.5 kg", "0.25 L": up to three decimals, no trailing zeros. */
export function formatQuantity(quantity: number, unit: ProductUnit): string {
  return `${Number(quantity.toFixed(3))} ${unitLabels[unit]}`
}

const cardBrands: Record<string, string> = { visa: 'Visa', mastercard: 'Mastercard', amex: 'American Express', unionpay: 'UnionPay' }

/**
 * "Card · Visa •••• 4242", "Cash at the counter": how an order was paid, for the dealer's list.
 * A paid order with no method was handed over before payments were recorded.
 */
export function formatPaidBy(order: Pick<Order, 'paymentStatus' | 'paidBy' | 'cardBrand' | 'cardLast4'>): string | null {
  if (order.paidBy === 'Cash') return 'Cash at the counter'
  if (order.paidBy !== 'Card') return order.paymentStatus === 'Paid' ? 'Settled before payments were recorded' : null
  const brand = order.cardBrand ? (cardBrands[order.cardBrand] ?? order.cardBrand) : 'Card'
  return order.cardLast4 ? `Card · ${brand} •••• ${order.cardLast4}` : `Card · ${brand}`
}

/** Today as yyyy-MM-dd in the browser's time zone — what a date input shows. */
export function isoToday(offsetDays = 0): string {
  const date = new Date()
  date.setDate(date.getDate() + offsetDays)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

// Shared by every feature now; kept importable from here for the inventory screens.
export { formatDateTime } from '@/lib/dates'
