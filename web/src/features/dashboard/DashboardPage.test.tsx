import { screen, waitFor, within } from '@testing-library/react'
import { API_BASE_URL } from '@/lib/api'
import { useAuthStore } from '@/features/auth/auth-store'
import { agronomist, authResponse, farmer, http, HttpResponse, server } from '@/test/msw'
import { makeSignal, makeSprayWindow } from '@/test/harvest-handlers'
import { admin, dealer } from '@/test/inventory-handlers'
import { renderApp } from '@/test/render'

describe('DashboardPage', () => {
  it('shows the disease pressure in the agronomist’s own district', async () => {
    const requests: URLSearchParams[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/intelligence/outbreak-signal`, ({ request }) => {
        requests.push(new URL(request.url).searchParams)
        return HttpResponse.json(makeSignal())
      }),
    )
    useAuthStore.getState().setSession(authResponse(agronomist))
    renderApp('/dashboard')

    expect(await screen.findByText('Disease pressure · Nuwara Eliya')).toBeInTheDocument()
    expect(screen.getByText('Late blight leads · ↑ Rising')).toBeInTheDocument()
    expect(requests[0].get('districtId')).toBe(agronomist.districtId)
    expect(screen.getByRole('link', { name: /Harvests/ })).toHaveAttribute('href', '/harvest')
    expect(screen.getByRole('link', { name: /Collection planner/ })).toBeInTheDocument()
    expect(screen.queryByText('Stock on the shelf')).not.toBeInTheDocument()
  })

  it('gives a dealer their stock value and what has run out, but no farm areas', async () => {
    useAuthStore.getState().setSession(authResponse(dealer))
    renderApp('/dashboard')

    expect(await screen.findByText('LKR 103,200.00')).toBeInTheDocument()
    expect(screen.getByText('1 out of stock · 2 running low')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Disease intelligence/ })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Harvests/ })).not.toBeInTheDocument()
  })

  it('gives the administrator every shop’s stock', async () => {
    useAuthStore.getState().setSession(authResponse(admin))
    renderApp('/dashboard')

    expect(await screen.findByText('Stock on the shelf')).toBeInTheDocument()
  })

  it('shows a farmer no stock figures', async () => {
    useAuthStore.getState().setSession(authResponse(farmer))
    renderApp('/dashboard')

    await screen.findByText('Disease pressure · Nuwara Eliya')
    await waitFor(() => expect(screen.queryByText('Stock on the shelf')).not.toBeInTheDocument())
  })

  it('puts the agronomist’s queue beside the spray weather at the longest-waiting plot', async () => {
    useAuthStore.getState().setSession(authResponse(agronomist))
    renderApp('/dashboard')

    const queue = await screen.findByRole('region', { name: 'Proposals awaiting your decision' })
    expect(await within(queue).findByRole('link', { name: 'AG-2026-000003' })).toHaveAttribute('href', '/agent-runs/run-1')
    const weather = await screen.findByRole('region', { name: 'Spray weather' })
    expect(await within(weather).findAllByText('Suits spraying')).toHaveLength(2)
    // The reason in two words, and the rule's full sentence for screen readers.
    expect(within(weather).getByText('Rain likely')).toBeInTheDocument()
    expect(within(weather).getByText(': Rain likely (80%, 9.4 mm)')).toBeInTheDocument()
  })

  it('says when the forecast is unavailable instead of guessing', async () => {
    server.use(http.get(`${API_BASE_URL}/api/weather/spray-window`, () => HttpResponse.json(makeSprayWindow({ forecastAvailable: false, days: [] }))))
    useAuthStore.getState().setSession(authResponse(farmer))
    renderApp('/dashboard')

    expect(await screen.findByText('No forecast right now')).toBeInTheDocument()
  })

  it('gives a dealer the orders to pack and what is running low', async () => {
    useAuthStore.getState().setSession(authResponse(dealer))
    renderApp('/dashboard')

    expect(await screen.findByRole('region', { name: 'Orders to pack' })).toBeInTheDocument()
    expect(await screen.findByRole('region', { name: 'Running low' })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Spray weather' })).not.toBeInTheDocument()
  })
})
