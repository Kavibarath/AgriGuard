import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as registry from './api'

/**
 * Query keys as a tree, so invalidating `registry.farms` refreshes every farm list regardless of
 * its filters, while `registry.farm(id)` targets one row.
 */
export const registryKeys = {
  all: ['registry'] as const,
  farms: (query?: registry.FarmQuery) => ['registry', 'farms', query ?? {}] as const,
  farm: (id: string) => ['registry', 'farm', id] as const,
  plots: (query?: registry.PlotQuery) => ['registry', 'plots', query ?? {}] as const,
  plot: (id: string) => ['registry', 'plot', id] as const,
  cropCycle: (id: string) => ['registry', 'crop-cycle', id] as const,
  districts: ['registry', 'districts'] as const,
  crops: ['registry', 'crops'] as const,
}

export const useFarms = (query: registry.FarmQuery) =>
  useQuery({ queryKey: registryKeys.farms(query), queryFn: () => registry.listFarms(query) })

export const useFarm = (id: string) =>
  useQuery({ queryKey: registryKeys.farm(id), queryFn: () => registry.getFarm(id) })

export const usePlots = (query: registry.PlotQuery, enabled = true) =>
  useQuery({ queryKey: registryKeys.plots(query), queryFn: () => registry.listPlots(query), enabled })

export const useCropCycle = (id: string | null) =>
  useQuery({
    queryKey: registryKeys.cropCycle(id ?? 'none'),
    queryFn: () => registry.getCropCycle(id!),
    enabled: id !== null,
  })

// Look-ups change perhaps once a year; keep them out of the network path for the session.
export const useDistricts = () =>
  useQuery({ queryKey: registryKeys.districts, queryFn: registry.listDistricts, staleTime: Infinity })

export const useCrops = () =>
  useQuery({ queryKey: registryKeys.crops, queryFn: registry.listCrops, staleTime: Infinity })

/**
 * Every mutation invalidates the whole registry subtree rather than surgically patching caches.
 * One farm edit can change a list, a detail page and a plot's farm name; re-fetching the few
 * active queries is cheaper to reason about than keeping three caches consistent by hand.
 */
function useRegistryMutation<TArgs, TResult>(mutationFn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: registryKeys.all }),
  })
}

export const useCreateFarm = () => useRegistryMutation(registry.createFarm)
export const useUpdateFarm = () =>
  useRegistryMutation(({ id, ...input }: registry.FarmInput & { id: string }) => registry.updateFarm(id, input))
export const useDeleteFarm = () => useRegistryMutation(registry.deleteFarm)

export const useCreatePlot = () => useRegistryMutation(registry.createPlot)
export const useUpdatePlot = () =>
  useRegistryMutation(({ id, ...input }: registry.PlotInput & { id: string; status: string }) =>
    registry.updatePlot(id, input),
  )

export const useCreateCropCycle = () => useRegistryMutation(registry.createCropCycle)
export const useAdvanceStage = () =>
  useRegistryMutation(({ id, ...input }: { id: string; toStage: string; reachedOn?: string | null; note?: string | null }) =>
    registry.advanceStage(id, input),
  )
