import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { API_BASE_URL } from '@/lib/api'
import { useAuthStore } from '@/features/auth/auth-store'
import { agronomist, authResponse, http, HttpResponse, problem, server } from '@/test/msw'
import { dealer, makeOrder } from '@/test/inventory-handlers'
import { paged } from '@/test/registry-handlers'
import { renderApp } from '@/test/render'

describe('OrdersPage', () => {
  beforeEach(() => useAuthStore.getState().setSession(authResponse(dealer)))

  it('opens on the orders waiting to be packed', async () => {
    const requests: string[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/orders`, ({ request }) => {
        requests.push(new URL(request.url).search)
        return HttpResponse.json(paged([makeOrder()]))
      }),
    )
    renderApp('/orders')

    const row = await screen.findByRole('row', { name: /ORD-2026-000001/ })
    expect(within(row).getByText('Prescription RX-2026-000001')).toBeInTheDocument()
    expect(within(row).getByText('2 × Mancozeb 80 WP')).toBeInTheDocument()
    expect(within(row).getByText('Ready to pack')).toBeInTheDocument()
    expect(requests[0]).toContain('status=Confirmed')
  })

  it('performs the next step, naming the status it moves to', async () => {
    const bodies: unknown[] = []
    server.use(
      http.post(`${API_BASE_URL}/api/orders/:id/fulfil`, async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json(makeOrder({ status: 'Packed', nextStatus: 'Collected' }))
      }),
    )
    const user = userEvent.setup()
    renderApp('/orders')

    await user.click(await screen.findByRole('button', { name: 'Mark packed: ORD-2026-000001' }))

    await waitFor(() => expect(bodies).toEqual([{ status: 'Packed' }]))
  })

  it('offers no step for a collected order', async () => {
    server.use(
      http.get(`${API_BASE_URL}/api/orders`, () =>
        HttpResponse.json(paged([makeOrder({ status: 'Collected', nextStatus: null, collectedAt: '2026-09-28T10:00:00Z' })])),
      ),
    )
    renderApp('/orders?view=Collected')

    const row = await screen.findByRole('row', { name: /ORD-2026-000001/ })
    expect(within(row).getByText('Collected')).toBeInTheDocument()
    expect(within(row).queryByRole('button')).not.toBeInTheDocument()
  })

  it('shows why a step was refused', async () => {
    server.use(
      http.post(`${API_BASE_URL}/api/orders/:id/fulfil`, () =>
        problem(409, 'Conflict', 'This order was updated at the same moment, perhaps from another screen. Reload to see where it is.'),
      ),
    )
    const user = userEvent.setup()
    renderApp('/orders')

    await user.click(await screen.findByRole('button', { name: /Mark packed/ }))

    expect(await screen.findByText(/updated at the same moment/)).toBeInTheDocument()
  })

  it('explains an empty queue', async () => {
    server.use(http.get(`${API_BASE_URL}/api/orders`, () => HttpResponse.json(paged([]))))

    renderApp('/orders')

    expect(await screen.findByText('Nothing under “To pack”')).toBeInTheDocument()
    expect(screen.getByText(/When an agronomist approves a prescription/)).toBeInTheDocument()
  })

  it('keeps an agronomist out', async () => {
    useAuthStore.getState().setSession(authResponse(agronomist))

    const { router } = renderApp('/orders')

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})
