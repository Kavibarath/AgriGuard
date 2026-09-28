import { screen, waitFor } from '@testing-library/react'
import { API_BASE_URL } from '@/lib/api'
import { useAuthStore } from '@/features/auth/auth-store'
import { agronomist, authResponse, farmer, http, HttpResponse, server } from '@/test/msw'
import { makeSignal } from '@/test/harvest-handlers'
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
})
