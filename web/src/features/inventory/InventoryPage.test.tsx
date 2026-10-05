import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { API_BASE_URL } from '@/lib/api'
import { useAuthStore } from '@/features/auth/auth-store'
import { authResponse, farmer, http, HttpResponse, problem, server } from '@/test/msw'
import { dealer, makeReservation } from '@/test/inventory-handlers'
import { paged } from '@/test/registry-handlers'
import { renderApp } from '@/test/render'

describe('InventoryPage', () => {
  beforeEach(() => useAuthStore.getState().setSession(authResponse(dealer)))

  it('lists the shelf soonest expiry first, with warnings and what is held', async () => {
    renderApp('/inventory')

    const soon = await screen.findByRole('row', { name: /Batch MZ-2512/ })
    expect(within(soon).getByText('Expiring soon')).toBeInTheDocument()
    expect(within(soon).getByText('in 10 day(s)')).toBeInTheDocument()

    const fresh = screen.getByRole('row', { name: /Batch MZ-2601/ })
    expect(within(fresh).getByText('In date')).toBeInTheDocument()
    expect(within(fresh).getByText('37 kg')).toBeInTheDocument()
    expect(within(fresh).getByText('LKR 2,400.00')).toBeInTheDocument()
  })

  it('warns about batches near expiry and filters to them on request', async () => {
    const requests: URLSearchParams[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/inventory`, ({ request }) => {
        const params = new URL(request.url).searchParams
        requests.push(params)
        // The warning's count comes from its own one-row query.
        return params.get('pageSize') === '1'
          ? HttpResponse.json(paged([], { totalCount: 2, pageSize: 1 }))
          : HttpResponse.json(paged([]))
      }),
    )
    const user = userEvent.setup()
    renderApp('/inventory')

    expect(await screen.findByText(/2 batch\(es\) expire within 30 days/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Show them' }))

    await waitFor(() => expect(requests.some((q) => q.has('expiringBefore') && q.get('pageSize') === '20')).toBe(true))
  })

  it('receives a delivery exactly as printed on the pack', async () => {
    let posted: unknown = null
    server.use(
      http.post(`${API_BASE_URL}/api/inventory/batches`, async ({ request }) => {
        posted = await request.json()
        return HttpResponse.json({}, { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderApp('/inventory')

    await user.click(await screen.findByRole('button', { name: 'Receive delivery' }))
    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText('Product'), 'p-mancozeb')
    await user.type(within(dialog).getByLabelText('Batch number'), 'MZ-2701')
    await user.type(within(dialog).getByLabelText('Expiry date'), '2028-01-31')
    await user.type(within(dialog).getByLabelText('Quantity on hand (kg)'), '25')
    // The co-op's reference price is filled in; this dealer sells a little cheaper.
    const price = within(dialog).getByLabelText('Price per pack (LKR)')
    expect(price).toHaveValue(2400)
    await user.clear(price)
    await user.type(price, '2350')
    await user.click(within(dialog).getByRole('button', { name: 'Add to shelf' }))

    await waitFor(() =>
      expect(posted).toEqual({ productId: 'p-mancozeb', batchNo: 'MZ-2701', expiryDate: '2028-01-31', quantityOnHand: 25, unitPrice: 2350 }),
    )
  })

  it('checks the delivery form before sending anything', async () => {
    let posted = false
    server.use(http.post(`${API_BASE_URL}/api/inventory/batches`, () => { posted = true; return HttpResponse.json({}, { status: 201 }) }))
    const user = userEvent.setup()
    renderApp('/inventory')

    await user.click(await screen.findByRole('button', { name: 'Receive delivery' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Add to shelf' }))

    expect(await within(dialog).findByText('Choose the product.')).toBeInTheDocument()
    expect(within(dialog).getByText('Enter the quantity on hand.')).toBeInTheDocument()
    expect(posted).toBe(false)
  })

  it('puts a duplicate batch number on the field that caused it', async () => {
    server.use(
      http.post(`${API_BASE_URL}/api/inventory/batches`, () =>
        problem(409, 'Conflict', 'Batch MZ-2601 of this product is already on your shelf. Update its count instead.'),
      ),
    )
    const user = userEvent.setup()
    renderApp('/inventory')

    await user.click(await screen.findByRole('button', { name: 'Receive delivery' }))
    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText('Product'), 'p-mancozeb')
    await user.type(within(dialog).getByLabelText('Batch number'), 'MZ-2601')
    await user.type(within(dialog).getByLabelText('Expiry date'), '2028-01-31')
    await user.type(within(dialog).getByLabelText('Quantity on hand (kg)'), '5')
    await user.click(within(dialog).getByRole('button', { name: 'Add to shelf' }))

    expect(await within(dialog).findByText(/already on your shelf/)).toBeInTheDocument()
  })

  it('fills in the reference price and shows the packs and the value as the quantity is typed', async () => {
    const user = userEvent.setup()
    renderApp('/inventory')

    await user.click(await screen.findByRole('button', { name: 'Receive delivery' }))
    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText('Product'), 'p-mancozeb')

    expect(within(dialog).getByLabelText('Price per pack (LKR)')).toHaveValue(2400)
    expect(within(dialog).getByText(/Co-op reference price LKR 2,400.00 per 1 kg pack/)).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText('Quantity on hand (kg)'), '20')
    expect(within(dialog).getByRole('status')).toHaveTextContent('20 kg = 20 packs of 1 kg × LKR 2,400.00 = LKR 48,000.00 on the shelf')

    await user.clear(within(dialog).getByLabelText('Quantity on hand (kg)'))
    await user.type(within(dialog).getByLabelText('Quantity on hand (kg)'), '2.5')
    expect(within(dialog).getByRole('status')).toHaveTextContent('2.5 packs (not a whole number of packs)')
  })

  it('explains why a recount cannot go below what is held', async () => {
    server.use(
      http.put(`${API_BASE_URL}/api/inventory/batches/:id`, () =>
        problem(422, 'Business rule violated', '3 of this batch is held for pending orders, so stock on hand cannot go below that. Release the hold first.', { code: 'BELOW_RESERVED' }),
      ),
    )
    const user = userEvent.setup()
    renderApp('/inventory')

    await user.click(await screen.findByRole('button', { name: 'Correct batch MZ-2601' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/3 kg is held/)).toBeInTheDocument()
    const count = within(dialog).getByLabelText('Quantity on hand (kg)')
    await user.clear(count)
    await user.type(count, '1')
    await user.click(within(dialog).getByRole('button', { name: 'Save correction' }))

    expect(await within(dialog).findByText(/Release the hold first/)).toBeInTheDocument()
  })

  it('holds stock and shows the shortfall when the shop cannot cover it', async () => {
    const bodies: unknown[] = []
    server.use(
      http.post(`${API_BASE_URL}/api/inventory/reservations`, async ({ request }) => {
        bodies.push(await request.json())
        return problem(422, 'Business rule violated', 'Not enough Mancozeb 80 WP in date on 2026-09-27 to hold 50 pack(s) (50 Kilogram). Available: 40 Kilogram.', { code: 'INSUFFICIENT_STOCK' })
      }),
    )
    const user = userEvent.setup()
    renderApp('/inventory')

    await user.click(await screen.findByRole('button', { name: 'Hold stock' }))
    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText('Product'), 'p-mancozeb')
    await user.type(within(dialog).getByLabelText('Quantity (kg)'), '50')
    await user.type(within(dialog).getByLabelText('Note'), 'Phone order')
    await user.click(within(dialog).getByRole('button', { name: 'Hold stock' }))

    expect(await within(dialog).findByText(/Available: 40 Kilogram/)).toBeInTheDocument()
    expect(bodies[0]).toEqual({ productId: 'p-mancozeb', quantity: 50, usableOn: null, note: 'Phone order' })
  })

  it('commits a counter hold once sold', async () => {
    let committed = ''
    server.use(
      http.post(`${API_BASE_URL}/api/inventory/reservations/:id/commit`, ({ params }) => {
        committed = String(params.id)
        return HttpResponse.json(makeReservation({ status: 'Committed' }))
      }),
    )
    const user = userEvent.setup()
    renderApp('/inventory')

    const row = await screen.findByRole('row', { name: /Phone order, Mr Silva/ })
    await user.click(within(row).getByRole('button', { name: 'Sold' }))

    await waitFor(() => expect(committed).toBe('hold-1'))
  })

  it('leaves a hold for a pending prescription to the agronomist', async () => {
    server.use(
      http.get(`${API_BASE_URL}/api/inventory/reservations`, () =>
        HttpResponse.json(paged([makeReservation({ agentRunId: 'run-1', note: null })])),
      ),
    )
    renderApp('/inventory')

    expect(await screen.findByText('For a prescription awaiting approval')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Sold' })).not.toBeInTheDocument()
  })

  it('keeps everyone but dealers out', async () => {
    useAuthStore.getState().setSession(authResponse(farmer))

    const { router } = renderApp('/inventory')

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})
