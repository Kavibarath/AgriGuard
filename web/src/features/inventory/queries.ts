import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as inventory from './api'
import type { OrderStatus, RuleLimits } from './types'

export const inventoryKeys = {
  all: ['inventory'] as const,
  products: (query: inventory.ProductQuery) => ['inventory', 'products', query] as const,
  batches: (query: inventory.InventoryQuery) => ['inventory', 'batches', query] as const,
  reservations: (query: inventory.ReservationQuery) => ['inventory', 'reservations', query] as const,
  orders: (query: inventory.OrderQuery) => ['inventory', 'orders', query] as const,
  rules: (query: inventory.RuleQuery) => ['rules', query] as const,
  stockValuation: (dealerId?: string) => ['inventory', 'stock-valuation', dealerId ?? 'all'] as const,
  lowStock: (dealerId?: string) => ['inventory', 'low-stock', dealerId ?? 'all'] as const,
}

export const useProducts = (query: inventory.ProductQuery) =>
  useQuery({ queryKey: inventoryKeys.products(query), queryFn: () => inventory.listProducts(query), staleTime: 5 * 60_000 })

export const useBatches = (query: inventory.InventoryQuery) =>
  useQuery({ queryKey: inventoryKeys.batches(query), queryFn: () => inventory.listBatches(query) })

export const useReservations = (query: inventory.ReservationQuery) =>
  useQuery({ queryKey: inventoryKeys.reservations(query), queryFn: () => inventory.listReservations(query) })

export const useOrders = (query: inventory.OrderQuery) =>
  useQuery({ queryKey: inventoryKeys.orders(query), queryFn: () => inventory.listOrders(query) })

export const useRules = (query: inventory.RuleQuery) =>
  useQuery({ queryKey: inventoryKeys.rules(query), queryFn: () => inventory.listRules(query) })

export const useStockValuation = (dealerId?: string, enabled = true) =>
  useQuery({ queryKey: inventoryKeys.stockValuation(dealerId), queryFn: () => inventory.getStockValuation(dealerId), enabled })

export const useLowStock = (dealerId?: string, enabled = true) =>
  useQuery({ queryKey: inventoryKeys.lowStock(dealerId), queryFn: () => inventory.getLowStock({ dealerId }), enabled })

/**
 * A hold, a commit or a recount changes the batch table, the holds list and the availability
 * figures together, so every stock mutation refreshes the whole inventory subtree.
 */
function useStockMutation<TArgs, TResult>(mutationFn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({ mutationFn, onSuccess: () => queryClient.invalidateQueries({ queryKey: inventoryKeys.all }) })
}

export const useCreateBatch = () => useStockMutation(inventory.createBatch)
export const useUpdateBatch = () =>
  useStockMutation(({ id, ...input }: inventory.BatchCorrection & { id: string }) => inventory.updateBatch(id, input))
export const useCreateReservation = () => useStockMutation(inventory.createReservation)
export const useCommitReservation = () => useStockMutation(inventory.commitReservation)
export const useReleaseReservation = () => useStockMutation(inventory.releaseReservation)
export const useFulfilOrder = () =>
  useStockMutation(({ id, status, pickupCode }: { id: string; status: OrderStatus; pickupCode?: string }) =>
    inventory.fulfilOrder(id, status, pickupCode),
  )

function useRuleMutation<TArgs, TResult>(mutationFn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({ mutationFn, onSuccess: () => queryClient.invalidateQueries({ queryKey: ['rules'] }) })
}

export const useCreateRule = () => useRuleMutation(inventory.createRule)
export const useUpdateRule = () =>
  useRuleMutation(({ id, ...limits }: RuleLimits & { id: string }) => inventory.updateRule(id, limits))
