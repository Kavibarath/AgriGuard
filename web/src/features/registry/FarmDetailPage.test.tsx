import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { API_BASE_URL } from '@/lib/api'
import { authResponse, farmer, agronomist, http, HttpResponse, problem, server } from '@/test/msw'
import { makeCycle, makePlot, paged } from '@/test/registry-handlers'
import { useAuthStore } from '@/features/auth/auth-store'
import { renderApp } from '@/test/render'

function signIn(as = farmer) {
  useAuthStore.getState().setSession(authResponse(as))
}

describe('FarmDetailPage', () => {
  it('shows the farm, its plots and the crop growing on each', async () => {
    signIn()
    renderApp('/farms/farm-1')

    expect(await screen.findByRole('heading', { name: 'Green Acres' })).toBeInTheDocument()
    expect(screen.getByText(/Hatton, Nuwara Eliya · 2 plots · 2.05 ha/)).toBeInTheDocument()

    const firstRow = screen.getByRole('row', { name: /P-01/ })
    expect(within(firstRow).getByText('Tomato')).toBeInTheDocument()
    expect(within(firstRow).getByText('Vegetative')).toBeInTheDocument()
    expect(within(firstRow).getByText('0.800')).toBeInTheDocument()
  })

  it('offers only the next legal stage, named on the button', async () => {
    signIn()
    renderApp('/farms/farm-1')

    // The cycle is Vegetative; Flowering is the only legal move, and the button says so.
    expect(await screen.findByRole('button', { name: 'Advance to Flowering' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Advance to Pre-harvest/ })).not.toBeInTheDocument()
  })

  it('advances a stage and sends the date the farmer gives', async () => {
    signIn()
    let body: Record<string, unknown> | null = null
    server.use(
      http.post(`${API_BASE_URL}/api/crop-cycles/:id/advance-stage`, async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(makeCycle({ stage: 'Flowering', allowedNextStages: ['FruitSet'] }))
      }),
    )
    const user = userEvent.setup()
    renderApp('/farms/farm-1')

    await user.click(await screen.findByRole('button', { name: 'Advance to Flowering' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Note'), 'First flowers')
    await user.click(within(dialog).getByRole('button', { name: /Confirm Flowering/ }))

    await waitFor(() => expect(body).toMatchObject({ toStage: 'Flowering', note: 'First flowers' }))
    expect(body!.reachedOn).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('explains an illegal transition returned by the server', async () => {
    signIn()
    server.use(
      http.post(`${API_BASE_URL}/api/crop-cycles/:id/advance-stage`, () =>
        problem(422, 'Business rule violated', 'A crop cycle moves one stage at a time: Vegetative must advance to Flowering before PreHarvest.', {
          code: 'ILLEGAL_STAGE_TRANSITION',
        }),
      ),
    )
    const user = userEvent.setup()
    renderApp('/farms/farm-1')

    await user.click(await screen.findByRole('button', { name: 'Advance to Flowering' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: /Confirm Flowering/ }))

    expect(await within(dialog).findByText(/one stage at a time/)).toBeInTheDocument()
  })

  it('shows the stage history as an audit trail', async () => {
    signIn()
    renderApp('/farms/farm-1')

    expect(await screen.findByText('Stage history')).toBeInTheDocument()
    expect(screen.getByText('2026-07-25')).toBeInTheDocument()
    expect(screen.getByText(/“First true leaves”/)).toBeInTheDocument()
  })

  it('invites sowing on an empty plot and previews the harvest date', async () => {
    signIn()
    server.use(http.get(`${API_BASE_URL}/api/plots`, () => HttpResponse.json(paged([makePlot()]))))
    const user = userEvent.setup()
    renderApp('/farms/farm-1')

    expect(await screen.findByText('No crop growing')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Record sowing' }))

    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText('Crop'), 'c-tom')
    // Maturity days come from the crop list, so the farmer sees the consequence before saving.
    expect(within(dialog).getByText(/Harvest expected around/)).toBeInTheDocument()
  })

  it('locks the area while a crop is growing', async () => {
    signIn()
    const user = userEvent.setup()
    renderApp('/farms/farm-1')

    const row = await screen.findByRole('row', { name: /P-01/ })
    await user.click(within(row).getByRole('button', { name: 'Edit' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByLabelText(/Area \(hectares\)/)).toBeDisabled()
    expect(within(dialog).getByText('Locked while a crop cycle is active')).toBeInTheDocument()
  })

  it('gives an agronomist the read-only view', async () => {
    signIn(agronomist)
    renderApp('/farms/farm-1')

    expect(await screen.findByRole('heading', { name: 'Green Acres' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add plot' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Advance to/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
  })

  it('explains an empty farm', async () => {
    signIn()
    server.use(http.get(`${API_BASE_URL}/api/plots`, () => HttpResponse.json(paged([]))))

    renderApp('/farms/farm-1')

    expect(await screen.findByText('No plots yet')).toBeInTheDocument()
    expect(screen.getByText(/area drives treatment quantities/i)).toBeInTheDocument()
  })
})
