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

  it('hands an order over only with the farmer’s pickup code', async () => {
    const bodies: unknown[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/orders`, () =>
        HttpResponse.json(paged([makeOrder({ status: 'Packed', nextStatus: 'Collected', paymentStatus: 'Paid', paidBy: 'Cash' })])),
      ),
      http.post(`${API_BASE_URL}/api/orders/:id/fulfil`, async ({ request }) => {
        const body = (await request.json()) as { pickupCode?: string }
        bodies.push(body)
        return body.pickupCode === '482913'
          ? HttpResponse.json(makeOrder({ status: 'Collected', nextStatus: null }))
          : problem(422, 'Business rule violated', 'That is not this order’s pickup code. Check it with the farmer; nothing was handed over.', {
              code: 'WRONG_PICKUP_CODE',
            })
      }),
    )
    const user = userEvent.setup()
    renderApp('/orders?view=Packed')

    await user.click(await screen.findByRole('button', { name: 'Mark collected: ORD-2026-000001' }))
    const dialog = await screen.findByRole('dialog', { name: 'Hand over ORD-2026-000001' })
    await user.type(within(dialog).getByLabelText('Pickup code'), '48291')
    await user.click(within(dialog).getByRole('button', { name: 'Hand over' }))
    expect(within(dialog).getByText('Enter the six digits shown on the farmer’s phone.')).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText('Pickup code'), '4')
    await user.click(within(dialog).getByRole('button', { name: 'Hand over' }))
    expect(await within(dialog).findByText(/not this order’s pickup code/)).toBeInTheDocument()

    await user.clear(within(dialog).getByLabelText('Pickup code'))
    await user.type(within(dialog).getByLabelText('Pickup code'), '482 913')
    await user.click(within(dialog).getByRole('button', { name: 'Hand over' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(bodies).toEqual([
      { status: 'Collected', pickupCode: '482914' },
      { status: 'Collected', pickupCode: '482913' },
    ])
  })

  it('shows whether each order is paid, and how', async () => {
    server.use(
      http.get(`${API_BASE_URL}/api/orders`, () =>
        HttpResponse.json(
          paged([
            makeOrder(),
            makeOrder({ id: 'order-2', orderNo: 'ORD-2026-000002', paymentStatus: 'Paid', paidBy: 'Card', cardBrand: 'visa', cardLast4: '4242' }),
          ]),
        ),
      ),
    )
    renderApp('/orders')

    const unpaid = await screen.findByRole('row', { name: /ORD-2026-000001/ })
    expect(within(unpaid).getByText('Unpaid')).toBeInTheDocument()
    expect(within(unpaid).getByRole('button', { name: 'Record cash: ORD-2026-000001' })).toBeInTheDocument()
    const paid = screen.getByRole('row', { name: /ORD-2026-000002/ })
    expect(within(paid).getByText('Paid')).toBeInTheDocument()
    expect(within(paid).getByText('Card · Visa •••• 4242')).toBeInTheDocument()
    expect(within(paid).queryByRole('button', { name: /Record cash/ })).not.toBeInTheDocument()
  })

  it('records cash taken at the counter after the dealer confirms the amount', async () => {
    const paidFor: string[] = []
    server.use(
      http.post(`${API_BASE_URL}/api/orders/:id/payments/cash`, ({ params }) => {
        paidFor.push(String(params.id))
        return HttpResponse.json(makeOrder({ paymentStatus: 'Paid', paidBy: 'Cash' }))
      }),
    )
    const user = userEvent.setup()
    renderApp('/orders')

    await user.click(await screen.findByRole('button', { name: 'Record cash: ORD-2026-000001' }))
    const dialog = await screen.findByRole('dialog', { name: 'Record cash for ORD-2026-000001' })
    await user.click(within(dialog).getByRole('button', { name: 'Cash received: LKR 4,800.00' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(paidFor).toEqual(['order-1'])
  })

  it('will not hand an unpaid order over until the cash is recorded', async () => {
    const bodies: unknown[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/orders`, () => HttpResponse.json(paged([makeOrder({ status: 'Packed', nextStatus: 'Collected' })]))),
      http.post(`${API_BASE_URL}/api/orders/:id/payments/cash`, () =>
        HttpResponse.json(makeOrder({ status: 'Packed', nextStatus: 'Collected', paymentStatus: 'Paid', paidBy: 'Cash' })),
      ),
      http.post(`${API_BASE_URL}/api/orders/:id/fulfil`, async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json(makeOrder({ status: 'Collected', nextStatus: null, paymentStatus: 'Paid', paidBy: 'Cash' }))
      }),
    )
    const user = userEvent.setup()
    renderApp('/orders?view=Packed')

    await user.click(await screen.findByRole('button', { name: 'Mark collected: ORD-2026-000001' }))
    const dialog = await screen.findByRole('dialog', { name: 'Hand over ORD-2026-000001' })
    expect(within(dialog).getByText('Not paid yet')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Pickup code')).toBeDisabled()
    expect(within(dialog).getByRole('button', { name: 'Hand over' })).toBeDisabled()

    await user.click(within(dialog).getByRole('button', { name: 'Cash received: LKR 4,800.00' }))
    expect(await within(dialog).findByText(/Cash at the counter/)).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Pickup code'), '482913')
    await user.click(within(dialog).getByRole('button', { name: 'Hand over' }))

    await waitFor(() => expect(bodies).toEqual([{ status: 'Collected', pickupCode: '482913' }]))
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
