import { useQuery } from '@tanstack/react-query'
import * as intelligence from './api'

export const useOutbreakSignal = (query: intelligence.OutbreakQuery, enabled = true) =>
  useQuery({
    queryKey: ['intelligence', 'outbreak-signal', query],
    queryFn: () => intelligence.getOutbreakSignal(query),
    enabled,
    // Confirmed cases arrive a few a day; a minute's staleness is harmless.
    staleTime: 60_000,
  })
