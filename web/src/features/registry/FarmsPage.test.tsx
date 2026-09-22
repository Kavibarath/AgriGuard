import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { API_BASE_URL } from '@/lib/api'
import { authResponse, farmer, agronomist, http, HttpResponse, problem, server } from '@/test/msw'
import { makeFarm, paged } from '@/test/registry-handlers'
import { useAuthStore } from '@/features/auth/auth-store'
import { renderApp } from '@/test/render'

function signIn(as = farmer) {
  useAuthStore.getState().setSession(authResponse(as))
}

describe('FarmsPage', () => {
  it('lists farms with their plot count and area', async () => {
    signIn()
    renderApp('/farms')

    expect(await screen.findByRole('link', { name: 'Green Acres' })).toBeInTheDocument()
    const row = screen.getByRole('row', { name: /green acres/i })
    expect(within(row).getByText('Hatton')).toBeInTheDocument()
    expect(within(row).getByText('2.05')).toBeInTheDocument()
  })

  it('explains an empty registry and offers the first action', async () => {
    signIn()
    server.use(http.get(`${API_BASE_URL}/api/farms`, () => HttpResponse.json(paged([]))))

    renderApp('/farms')

    expect(await screen.findByText('No farms yet')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /register farm/i }).length).toBeGreaterThan(0)
  })

  it('distinguishes "no results" from "nothing registered"', async () => {
    signIn()
    server.use(http.get(`${API_BASE_URL}/api/farms`, () => HttpResponse.json(paged([]))))

    renderApp('/farms?search=zzz')

    expect(await screen.findByText('No farms match that search')).toBeInTheDocument()
  })

  it('sends search, sort and page to the API rather than filtering in the browser', async () => {
    signIn()
    const requests: string[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/farms`, ({ request }) => {
        requests.push(new URL(request.url).search)
        return HttpResponse.json(paged([makeFarm()], { totalCount: 40, pageSize: 20 }))
      }),
    )
    const user = userEvent.setup()
    renderApp('/farms')

    await screen.findByRole('link', { name: 'Green Acres' })
    await user.click(screen.getByRole('button', { name: /^farm/i }))
    await waitFor(() => expect(requests.at(-1)).toContain('sortBy=name'))

    await user.click(screen.getByRole('button', { name: 'Next' }))
    await waitFor(() => expect(requests.at(-1)).toContain('page=2'))

    await user.type(screen.getByLabelText('Search'), 'acre')
    await waitFor(() => expect(requests.at(-1)).toContain('search=acre'))
    // A new search must return to page 1 — page 2 of a different result set is usually empty.
    expect(requests.at(-1)).not.toContain('page=2')
  })

  it('creates a farm and refreshes the list', async () => {
    signIn()
    let created: unknown = null
    server.use(
      http.post(`${API_BASE_URL}/api/farms`, async ({ request }) => {
        created = await request.json()
        return HttpResponse.json(makeFarm({ name: 'New Farm' }), { status: 201 })
      }),
    )
    const user = userEvent.setup()
    renderApp('/farms')

    await user.click(await screen.findByRole('button', { name: 'Register farm' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Farm name'), 'New Farm')
    await user.selectOptions(within(dialog).getByLabelText('District'), 'd-nuw')
    await user.click(within(dialog).getByRole('button', { name: 'Register farm' }))

    await waitFor(() => expect(created).toEqual({ name: 'New Farm', village: null, districtId: 'd-nuw' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('shows a duplicate-name conflict on the field that caused it', async () => {
    signIn()
    server.use(
      http.post(`${API_BASE_URL}/api/farms`, () =>
        problem(409, 'Conflict', "You already have a farm called 'Green Acres'."),
      ),
    )
    const user = userEvent.setup()
    renderApp('/farms')

    await user.click(await screen.findByRole('button', { name: 'Register farm' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Farm name'), 'Green Acres')
    await user.selectOptions(within(dialog).getByLabelText('District'), 'd-nuw')
    await user.click(within(dialog).getByRole('button', { name: 'Register farm' }))

    expect(await within(dialog).findByText(/already have a farm called/i)).toBeInTheDocument()
    // The dialog stays open so the name can be corrected.
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('validates before sending anything', async () => {
    signIn()
    let posted = false
    server.use(http.post(`${API_BASE_URL}/api/farms`, () => { posted = true; return HttpResponse.json(makeFarm(), { status: 201 }) }))
    const user = userEvent.setup()
    renderApp('/farms')

    await user.click(await screen.findByRole('button', { name: 'Register farm' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Register farm' }))

    expect(await within(dialog).findByText('Give the farm a name.')).toBeInTheDocument()
    expect(within(dialog).getByText('Choose a district.')).toBeInTheDocument()
    expect(posted).toBe(false)
  })

  it('refuses to delete a farm that still has plots, and says why', async () => {
    signIn()
    server.use(
      http.delete(`${API_BASE_URL}/api/farms/:id`, () =>
        problem(409, 'Conflict', 'This farm still has 2 plot(s). Retire or move them before deleting the farm.'),
      ),
    )
    const user = userEvent.setup()
    renderApp('/farms')

    await user.click(await screen.findByRole('button', { name: 'Delete' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Delete farm' }))

    expect(await within(dialog).findByText(/still has 2 plot/i)).toBeInTheDocument()
  })

  it('hides editing controls from an agronomist, who only advises', async () => {
    signIn(agronomist)
    renderApp('/farms')

    expect(await screen.findByRole('link', { name: 'Green Acres' })).toBeInTheDocument()
    expect(screen.getByText('Farms in your district')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Register farm' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
  })

  it('keeps a dealer out of the registry entirely', async () => {
    useAuthStore.getState().setSession(authResponse({ ...farmer, role: 'AgroDealer' }))

    const { router } = renderApp('/farms')

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('surfaces a server failure without leaking its internals', async () => {
    signIn()
    server.use(
      http.get(`${API_BASE_URL}/api/farms`, () =>
        problem(500, 'An unexpected error occurred', 'Npgsql.NpgsqlException: connection refused at 127.0.0.1:5433'),
      ),
    )

    renderApp('/farms')

    expect(await screen.findByText('Could not load this')).toBeInTheDocument()
    expect(screen.getByText(/server had a problem/i)).toBeInTheDocument()
    expect(screen.queryByText(/Npgsql/)).not.toBeInTheDocument()
  })
})
