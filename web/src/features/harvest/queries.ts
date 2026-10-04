import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as harvest from './api'

export const harvestKeys = {
  all: ['harvest'] as const,
  forecasts: (query: harvest.ForecastQuery) => ['harvest', 'forecasts', query] as const,
  report: (query: harvest.ReportQuery) => ['harvest', 'forecast-vs-actual', query] as const,
  centres: (districtId?: string) => ['harvest', 'centres', districtId ?? 'all'] as const,
  slots: (query: harvest.SlotQuery) => ['harvest', 'slots', query] as const,
  bookings: (query: harvest.BookingQuery) => ['harvest', 'bookings', query] as const,
  sprayWindow: (plotId: string) => ['harvest', 'spray-window', plotId] as const,
}

export const useForecasts = (query: harvest.ForecastQuery) =>
  useQuery({ queryKey: harvestKeys.forecasts(query), queryFn: () => harvest.listForecasts(query) })

export const useForecastVsActual = (query: harvest.ReportQuery) =>
  useQuery({ queryKey: harvestKeys.report(query), queryFn: () => harvest.getForecastVsActual(query) })

export const useCentres = (districtId?: string) =>
  useQuery({ queryKey: harvestKeys.centres(districtId), queryFn: () => harvest.listCentres(districtId), staleTime: 5 * 60_000 })

export const useSlots = (query: harvest.SlotQuery) =>
  useQuery({ queryKey: harvestKeys.slots(query), queryFn: () => harvest.listSlots(query) })

export const useBookings = (query: harvest.BookingQuery) =>
  useQuery({ queryKey: harvestKeys.bookings(query), queryFn: () => harvest.listBookings(query) })

/** Forecasts change a few times a day; the API caches them for three hours, so a few minutes here is plenty. */
export const useSprayWindow = (plotId: string | undefined) =>
  useQuery({
    queryKey: harvestKeys.sprayWindow(plotId ?? 'none'),
    queryFn: () => harvest.getSprayWindow(plotId!),
    enabled: Boolean(plotId),
    staleTime: 10 * 60_000,
  })

/** A recorded yield changes the forecast list and the report; a new slot changes the planner. Refresh the subtree. */
function useHarvestMutation<TArgs, TResult>(mutationFn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient()
  return useMutation({ mutationFn, onSuccess: () => queryClient.invalidateQueries({ queryKey: harvestKeys.all }) })
}

export const useRecordActual = () =>
  useHarvestMutation(({ id, actualYieldKg }: { id: string; actualYieldKg: number }) => harvest.recordActualYield(id, actualYieldKg))

export const useCreateSlot = () => useHarvestMutation(harvest.createSlot)
