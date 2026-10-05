import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { API_BASE_URL } from '@/lib/api'
import { useAuthStore } from '@/features/auth/auth-store'
import { agronomist, authResponse, farmer, http, HttpResponse, problem, server } from '@/test/msw'
import { makeBooking, makeSlot } from '@/test/harvest-handlers'
import { admin } from '@/test/inventory-handlers'
import { paged } from '@/test/registry-handlers'
import { renderApp } from '@/test/render'

const WEEK = '/collection-planner?week=2026-09-28'

describe('CollectionPlannerPage', () => {
  beforeEach(() => useAuthStore.getState().setSession(authResponse(agronomist)))

  it('shows each centre by day with what is booked and what is left, and the bookings', async () => {
    server.use(
      http.get(`${API_BASE_URL}/api/collection-slots`, () =>
        HttpResponse.json(paged([makeSlot(), makeSlot({ id: 'slot-2', slotIndex: 2, startTime: '09:00:00', endTime: '11:00:00', bookedKg: 1500, remainingKg: 0 })])),
      ),
    )
    renderApp(WEEK)

    const centre = await screen.findByRole('row', { name: /4,500 kg a day/ })
    expect(within(centre).getByText('1,100 kg left')).toBeInTheDocument()
    expect(within(centre).getByText('Full')).toBeInTheDocument()
    expect(within(centre).getByText('1,900 kg / 3,000 kg')).toBeInTheDocument()
    expect(within(centre).getByRole('meter', { name: '07:00–09:00 slot: 400 kg of 1,500 kg booked' })).toBeInTheDocument()
    // The second centre has nothing that week.
    expect(within(screen.getByRole('row', { name: /3,000 kg a day/ })).getAllByText('No slots')).toHaveLength(7)

    expect(await screen.findByRole('row', { name: /BK-2026-000001/ })).toHaveTextContent('400 kg')
  })

  it('asks for the chosen week and moves a week at a time', async () => {
    const requests: URLSearchParams[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/collection-slots`, ({ request }) => {
        requests.push(new URL(request.url).searchParams)
        return HttpResponse.json(paged([]))
      }),
    )
    const user = userEvent.setup()
    renderApp(WEEK)

    await waitFor(() => expect(requests[0]?.get('from')).toBe('2026-09-28'))
    expect(requests[0].get('to')).toBe('2026-10-04')
    await user.click(screen.getByRole('button', { name: 'Next week →' }))

    await waitFor(() => expect(requests.some((q) => q.get('from') === '2026-10-05')).toBe(true))
  })

  it('lets only the administrator open slots', async () => {
    renderApp(WEEK)

    await screen.findByRole('row', { name: /3,000 kg a day/ })
    expect(screen.queryByRole('button', { name: 'Open a slot' })).not.toBeInTheDocument()
  })

  it('opens a slot as the administrator', async () => {
    useAuthStore.getState().setSession(authResponse(admin))
    let posted: unknown = null
    server.use(
      http.post(`${API_BASE_URL}/api/collection-slots`, async ({ request }) => {
        posted = await request.json()
        return HttpResponse.json(makeSlot(), { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderApp(WEEK)

    await user.click(await screen.findByRole('button', { name: 'Open a slot' }))
    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText('Centre'), 'cc-wel')
    await user.clear(within(dialog).getByLabelText('Day'))
    await user.type(within(dialog).getByLabelText('Day'), '2099-01-05')
    await user.type(within(dialog).getByLabelText('Capacity (kg)'), '750')
    await user.click(within(dialog).getByRole('button', { name: 'Open slot' }))

    await waitFor(() =>
      expect(posted).toEqual({ centreId: 'cc-wel', slotDate: '2099-01-05', slotIndex: 4, startTime: '13:00', endTime: '15:00', capacityKg: 750 }),
    )
  })

  it("puts the centre's daily limit on the capacity field", async () => {
    useAuthStore.getState().setSession(authResponse(admin))
    server.use(
      http.post(`${API_BASE_URL}/api/collection-slots`, () =>
        problem(422, 'Business rule violated', 'Welimada Collection Point handles 3000 kg a day; 3000 kg of slots already exist on 2099-01-05.', {
          code: 'CENTRE_CAPACITY_EXCEEDED',
        }),
      ),
    )
    const user = userEvent.setup()
    renderApp(WEEK)

    await user.click(await screen.findByRole('button', { name: 'Open a slot' }))
    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText('Centre'), 'cc-wel')
    await user.clear(within(dialog).getByLabelText('Day'))
    await user.type(within(dialog).getByLabelText('Day'), '2099-01-05')
    await user.type(within(dialog).getByLabelText('Capacity (kg)'), '750')
    await user.click(within(dialog).getByRole('button', { name: 'Open slot' }))

    expect(await within(dialog).findByText(/handles 3000 kg a day/)).toBeInTheDocument()
  })

  describe('at the centre', () => {
    const recorded: unknown[] = []
    beforeEach(() => {
      recorded.length = 0
      server.use(
        http.post(`${API_BASE_URL}/api/collection-bookings/:id/record`, async ({ request }) => {
          const body = (await request.json()) as { status: string; actualQuantityKg?: number }
          recorded.push(body)
          return HttpResponse.json(makeBooking({ status: body.status as never, actualQuantityKg: body.actualQuantityKg ?? null }))
        }),
      )
    })

    it('checks a farmer in on the day', async () => {
      const user = userEvent.setup()
      renderApp(WEEK)

      await user.click(await screen.findByRole('button', { name: 'Check in: BK-2026-000001' }))

      await waitFor(() => expect(recorded).toEqual([{ status: 'CheckedIn' }]))
    })

    it('records the weight delivered to complete a checked-in booking', async () => {
      server.use(http.get(`${API_BASE_URL}/api/collection-bookings`, () => HttpResponse.json(paged([makeBooking({ status: 'CheckedIn' })]))))
      const user = userEvent.setup()
      renderApp(WEEK)

      const row = await screen.findByRole('row', { name: /BK-2026-000001/ })
      expect(within(row).getByText('Checked in')).toBeInTheDocument()
      await user.click(within(row).getByRole('button', { name: 'Record weight: BK-2026-000001' }))
      const dialog = await screen.findByRole('dialog', { name: 'Record the weight for BK-2026-000001' })
      await user.click(within(dialog).getByRole('button', { name: 'Complete delivery' }))
      expect(within(dialog).getByText('Enter the weight delivered, in kg.')).toBeInTheDocument()

      await user.type(within(dialog).getByLabelText('Weight delivered (kg)'), '387.5')
      await user.click(within(dialog).getByRole('button', { name: 'Complete delivery' }))

      await waitFor(() => expect(recorded).toEqual([{ status: 'Completed', actualQuantityKg: 387.5 }]))
    })

    it('marks a farmer who never came as missed, after asking', async () => {
      const user = userEvent.setup()
      renderApp(WEEK)

      await user.click(await screen.findByRole('button', { name: 'Mark missed: BK-2026-000001' }))
      const dialog = await screen.findByRole('dialog', { name: 'Mark BK-2026-000001 missed' })
      await user.click(within(dialog).getByRole('button', { name: 'Mark missed' }))

      await waitFor(() => expect(recorded).toEqual([{ status: 'NoShow' }]))
    })

    it('offers nothing before the collection day, and shows a delivered weight and a missed booking plainly', async () => {
      server.use(
        http.get(`${API_BASE_URL}/api/collection-bookings`, () =>
          HttpResponse.json(
            paged([
              makeBooking({ id: 'bk-f', bookingNo: 'BK-2026-000010', slotDate: '2099-01-05' }),
              makeBooking({ id: 'bk-c', bookingNo: 'BK-2026-000011', status: 'Completed', actualQuantityKg: 380 }),
              makeBooking({ id: 'bk-m', bookingNo: 'BK-2026-000012', status: 'NoShow' }),
            ]),
          ),
        ),
      )
      renderApp(WEEK)

      const future = await screen.findByRole('row', { name: /BK-2026-000010/ })
      expect(within(future).queryByRole('button')).not.toBeInTheDocument()
      expect(within(future).getByText('On the day')).toBeInTheDocument()
      expect(within(screen.getByRole('row', { name: /BK-2026-000011/ })).getByText('380 kg delivered')).toBeInTheDocument()
      expect(within(screen.getByRole('row', { name: /BK-2026-000012/ })).getByText('Missed')).toBeInTheDocument()
    })

    it('shows why a step was refused', async () => {
      server.use(
        http.post(`${API_BASE_URL}/api/collection-bookings/:id/record`, () =>
          problem(422, 'Business rule violated', 'The farmer cancelled this booking.', { code: 'ILLEGAL_BOOKING_TRANSITION' }),
        ),
      )
      const user = userEvent.setup()
      renderApp(WEEK)

      await user.click(await screen.findByRole('button', { name: 'Check in: BK-2026-000001' }))

      expect(await screen.findByText('The farmer cancelled this booking.')).toBeInTheDocument()
    })

    it('gives farmers no centre actions', async () => {
      useAuthStore.getState().setSession(authResponse(farmer))
      renderApp(WEEK)

      const row = await screen.findByRole('row', { name: /BK-2026-000001/ })
      expect(within(row).queryByRole('button')).not.toBeInTheDocument()
    })
  })
})
