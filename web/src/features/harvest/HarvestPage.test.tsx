import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { API_BASE_URL } from '@/lib/api'
import { useAuthStore } from '@/features/auth/auth-store'
import { isoToday } from '@/features/inventory/format'
import { agronomist, authResponse, http, HttpResponse, server } from '@/test/msw'
import { makeForecast, makeReport } from '@/test/harvest-handlers'
import { dealer } from '@/test/inventory-handlers'
import { paged } from '@/test/registry-handlers'
import { renderApp } from '@/test/render'

describe('HarvestPage', () => {
  beforeEach(() => useAuthStore.getState().setSession(authResponse(agronomist)))

  it('lists coming harvests from today', async () => {
    const requests: URLSearchParams[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/harvest-forecasts`, ({ request }) => {
        requests.push(new URL(request.url).searchParams)
        return HttpResponse.json(paged([makeForecast()]))
      }),
    )
    renderApp('/harvest')

    const row = await screen.findByRole('row', { name: /Tomato/ })
    expect(within(row).getByText('2,400 kg')).toBeInTheDocument()
    expect(within(row).getByText('P-01 · Sunil Perera')).toBeInTheDocument()
    expect(requests[0].get('from')).toBe(isoToday())
  })

  it('shows how close past forecasts came, overall, by crop and by forecaster', async () => {
    renderApp('/harvest')

    expect(await screen.findByText('8.9%')).toBeInTheDocument()
    expect(screen.getByText('−4.2%')).toBeInTheDocument()
    // 3 of 4 forecasts within ±10%.
    expect(screen.getByText('75%')).toBeInTheDocument()
    const byCrop = screen.getByRole('figure', { name: /by crop/ })
    expect(within(byCrop).getByText('−14.3%')).toBeInTheDocument()
    expect(within(byCrop).getByText('+1.7%')).toBeInTheDocument()
    expect(screen.getByRole('figure', { name: /who made the forecast/ })).toHaveTextContent('Agronomist forecasts')
  })

  it('records the actual harvest against a forecast', async () => {
    let body: unknown = null
    server.use(
      http.put(`${API_BASE_URL}/api/harvest-forecasts/:id/actual`, async ({ request, params }) => {
        body = { id: params.id, ...((await request.json()) as object) }
        return HttpResponse.json(makeForecast({ actualYieldKg: 2150 }))
      }),
    )
    const user = userEvent.setup()
    renderApp('/harvest')

    await user.click(await screen.findByRole('button', { name: 'Record the actual harvest of Tomato on P-01' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Harvested (kg)'), '2150')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(body).toEqual({ id: 'fc-1', actualYieldKg: 2150 }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('explains an empty comparison', async () => {
    server.use(
      http.get(`${API_BASE_URL}/api/reports/harvest-forecast-vs-actual`, () =>
        HttpResponse.json(makeReport({ overall: { forecasts: 0, forecastKg: 0, actualKg: 0, variancePercent: null, meanAbsolutePercentError: null, withinTolerance: 0 }, pendingActuals: 2 })),
      ),
    )
    renderApp('/harvest')

    expect(await screen.findByText('No harvest recorded against a forecast yet')).toBeInTheDocument()
    expect(screen.getByText('2 forecast(s) are waiting for their actual harvest.')).toBeInTheDocument()
  })

  it('is not for dealers', async () => {
    useAuthStore.getState().setSession(authResponse(dealer))
    const { router } = renderApp('/harvest')

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})
