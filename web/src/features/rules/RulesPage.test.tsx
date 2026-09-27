import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { API_BASE_URL } from '@/lib/api'
import { useAuthStore } from '@/features/auth/auth-store'
import { authResponse, http, HttpResponse, problem, server } from '@/test/msw'
import { admin, dealer, makeRule } from '@/test/inventory-handlers'
import { renderApp } from '@/test/render'

describe('RulesPage', () => {
  beforeEach(() => useAuthStore.getState().setSession(authResponse(admin)))

  it('lists each product-crop rule with its limits', async () => {
    renderApp('/rules')

    const row = await screen.findByRole('row', { name: /Mancozeb 80 WP/ })
    expect(within(row).getByText('Tomato')).toBeInTheDocument()
    expect(within(row).getByText('1.5–2.5 kg')).toBeInTheDocument()
    expect(within(row).getByText('7 d')).toBeInTheDocument()
    expect(within(row).getByText('Approved')).toBeInTheDocument()
  })

  it('filters by crop and status on the server', async () => {
    const requests: URLSearchParams[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/product-crop-approvals`, ({ request }) => {
        requests.push(new URL(request.url).searchParams)
        return HttpResponse.json({ items: [makeRule()], page: 1, pageSize: 20, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false })
      }),
    )
    const user = userEvent.setup()
    renderApp('/rules')

    await screen.findByRole('row', { name: /Mancozeb 80 WP/ })
    expect(requests[0].get('isActive')).toBe('true')

    await user.selectOptions(screen.getByLabelText('Crop'), 'c-tom')
    await waitFor(() => expect(requests.at(-1)?.get('cropId')).toBe('c-tom'))

    await user.selectOptions(screen.getByLabelText('Status'), 'all')
    await waitFor(() => expect(requests.at(-1)?.has('isActive')).toBe(false))
  })

  it('saves an edited limit — the viva task of changing a business rule', async () => {
    let saved: unknown = null
    server.use(
      http.put(`${API_BASE_URL}/api/product-crop-approvals/:id`, async ({ request }) => {
        saved = await request.json()
        return HttpResponse.json(makeRule({ maxDosePerHectare: 1.8 }))
      }),
    )
    const user = userEvent.setup()
    renderApp('/rules')

    await user.click(await screen.findByRole('button', { name: 'Edit Mancozeb 80 WP on Tomato' }))
    const dialog = await screen.findByRole('dialog')
    const max = within(dialog).getByLabelText('Maximum dose')
    await user.clear(max)
    await user.type(max, '1.8')
    await user.click(within(dialog).getByRole('button', { name: 'Save rule' }))

    await waitFor(() =>
      expect(saved).toEqual({
        minDosePerHectare: 1.5,
        maxDosePerHectare: 1.8,
        preHarvestIntervalDays: 7,
        reEntryIntervalHours: 24,
        maxApplicationsPerCycle: 4,
        minDaysBetweenApplications: 7,
        rainfastHours: 4,
        isRestricted: false,
        isActive: true,
      }),
    )
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('refuses a maximum below the minimum before sending it', async () => {
    let sent = false
    server.use(http.put(`${API_BASE_URL}/api/product-crop-approvals/:id`, () => { sent = true; return HttpResponse.json(makeRule()) }))
    const user = userEvent.setup()
    renderApp('/rules')

    await user.click(await screen.findByRole('button', { name: 'Edit Mancozeb 80 WP on Tomato' }))
    const dialog = await screen.findByRole('dialog')
    const max = within(dialog).getByLabelText('Maximum dose')
    await user.clear(max)
    await user.type(max, '1')
    await user.click(within(dialog).getByRole('button', { name: 'Save rule' }))

    expect(await within(dialog).findByText('The maximum dose cannot be below the minimum.')).toBeInTheDocument()
    expect(sent).toBe(false)
  })

  it('withdraws an approval by unticking it', async () => {
    let saved: { isActive?: boolean } = {}
    server.use(
      http.put(`${API_BASE_URL}/api/product-crop-approvals/:id`, async ({ request }) => {
        saved = (await request.json()) as { isActive: boolean }
        return HttpResponse.json(makeRule({ isActive: false }))
      }),
    )
    const user = userEvent.setup()
    renderApp('/rules')

    await user.click(await screen.findByRole('button', { name: 'Edit Mancozeb 80 WP on Tomato' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('checkbox', { name: /Approved \(active\)/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Save rule' }))

    await waitFor(() => expect(saved.isActive).toBe(false))
  })

  it('approves a new product-crop pair and reports a duplicate', async () => {
    let posted: Record<string, unknown> = {}
    server.use(
      http.post(`${API_BASE_URL}/api/product-crop-approvals`, async ({ request }) => {
        posted = (await request.json()) as Record<string, unknown>
        return problem(409, 'Conflict', 'Mancozeb 80 WP already has a rule for Tomato. Edit that rule instead.')
      }),
    )
    const user = userEvent.setup()
    renderApp('/rules')

    await user.click(await screen.findByRole('button', { name: 'Approve a product' }))
    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText('Product'), 'p-mancozeb')
    await user.selectOptions(within(dialog).getByLabelText('Crop'), 'c-tom')
    await user.type(within(dialog).getByLabelText('Minimum dose'), '1.5')
    await user.type(within(dialog).getByLabelText('Maximum dose'), '2.5')
    await user.type(within(dialog).getByLabelText('Pre-harvest interval (days)'), '7')
    await user.type(within(dialog).getByLabelText('Applications per crop cycle'), '4')
    await user.type(within(dialog).getByLabelText('Days between applications'), '7')
    await user.click(within(dialog).getByRole('button', { name: 'Approve' }))

    expect(await within(dialog).findByText(/already has a rule for Tomato/)).toBeInTheDocument()
    expect(posted).toMatchObject({ productId: 'p-mancozeb', cropId: 'c-tom', maxDosePerHectare: 2.5, preHarvestIntervalDays: 7, isActive: true })
  })

  it('keeps a dealer out of the rules', async () => {
    useAuthStore.getState().setSession(authResponse(dealer))

    const { router } = renderApp('/rules')

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})
