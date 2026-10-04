import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { API_BASE_URL } from '@/lib/api'
import { agronomist, authResponse, farmer, http, HttpResponse, problem, server } from '@/test/msw'
import { makeSafetyProfile, makeTreatmentHistory } from '@/test/registry-handlers'
import { useAuthStore } from '@/features/auth/auth-store'
import { renderApp } from '@/test/render'

function signIn(as = farmer) {
  useAuthStore.getState().setSession(authResponse(as))
}

// The history table lists the same products, so product rows are looked up in the safety table.
const productsTable = async () => within(await screen.findByRole('table', { name: /Approved products/ }))

describe('PlotDetailPage', () => {
  it('shows the plot with its crop cycle, spray safety and history', async () => {
    signIn()
    renderApp('/plots/plot-1')

    expect(await screen.findByRole('heading', { name: /P-01/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '← Green Acres' })).toHaveAttribute('href', '/farms/farm-1')
    expect(await screen.findByRole('heading', { name: 'Spray safety' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Treatment history' })).toBeInTheDocument()
    // The owner gets the crop-cycle controls, as on the farm page.
    expect(await screen.findByRole('button', { name: 'Advance to Flowering' })).toBeInTheDocument()
  })

  it('says which products can be sprayed today, and why the others cannot', async () => {
    signIn()
    renderApp('/plots/plot-1')

    const products = await productsTable()
    const amistar = products.getByRole('row', { name: /Amistar 25 SC/ })
    expect(within(amistar).getByText('Can spray today')).toBeInTheDocument()
    expect(within(amistar).getByText('1 of 3')).toBeInTheDocument()

    const dithane = products.getByRole('row', { name: /Dithane M-45/ })
    // The rule is named, so the page and the validator's report use the same words.
    expect(within(dithane).getByText('Too close to harvest')).toBeInTheDocument()
    expect(within(dithane).getByText('Rule V5.')).toBeInTheDocument()
    expect(within(dithane).getByText(/needs 21 days before picking/)).toBeInTheDocument()
    expect(within(dithane).getByText('Restricted')).toBeInTheDocument()
    expect(screen.getByText('1 of 2')).toBeInTheDocument()
  })

  it('keeps the product filter in the URL', async () => {
    signIn()
    const user = userEvent.setup()
    const { router } = renderApp('/plots/plot-1')

    await productsTable()
    await user.selectOptions(screen.getByLabelText('Products'), 'sprayable')

    expect(router.state.location.search).toBe('?products=sprayable')
    const products = await productsTable()
    expect(products.queryByRole('row', { name: /Dithane M-45/ })).not.toBeInTheDocument()
    expect(products.getByRole('row', { name: /Amistar 25 SC/ })).toBeInTheDocument()
  })

  it('opens with the filter a shared link carries', async () => {
    signIn()
    renderApp('/plots/plot-1?products=blocked')

    const products = await productsTable()
    expect(products.getByRole('row', { name: /Dithane M-45/ })).toBeInTheDocument()
    expect(products.queryByRole('row', { name: /Amistar 25 SC/ })).not.toBeInTheDocument()
  })

  it('collapses the PHI-blocked days into a range', async () => {
    signIn()
    renderApp('/plots/plot-1')

    const blocked = await screen.findByRole('list', { name: 'PHI-blocked days' })
    expect(within(blocked).getByText(/17 Oct – 19 Oct/)).toBeInTheDocument()
    expect(within(blocked).getByText(/3 days/)).toBeInTheDocument()
  })

  it('warns to keep out of the field while re-entry is running', async () => {
    signIn()
    const clearAt = new Date(Date.now() + 6 * 3_600_000).toISOString()
    server.use(
      http.get(`${API_BASE_URL}/api/plots/:id/safety-profile`, () => HttpResponse.json(makeSafetyProfile({ reEntryClearAtUtc: clearAt }))),
    )
    renderApp('/plots/plot-1')

    expect(await screen.findByText('Keep out of the field')).toBeInTheDocument()
    expect(screen.queryByText(/No re-entry restriction/)).not.toBeInTheDocument()
  })

  it('explains that a plot with nothing growing has no spray profile', async () => {
    signIn()
    server.use(
      http.get(`${API_BASE_URL}/api/plots/:id/safety-profile`, () =>
        HttpResponse.json(
          makeSafetyProfile({ cropCycleId: null, cropName: null, stage: null, sownDate: null, harvestDate: null, daysToHarvest: null, productWindows: [], phiBlockedSprayDates: [] }),
        ),
      ),
    )
    renderApp('/plots/plot-1')

    expect(await screen.findByText('Nothing growing')).toBeInTheDocument()
  })

  it('lists every spray with the harvest date its PHI sets, and counts by ingredient', async () => {
    signIn()
    renderApp('/plots/plot-1')

    const applied = await screen.findByRole('row', { name: /10 Sept? 2026/ })
    expect(within(applied).getByText('RX-2026-000002')).toBeInTheDocument()
    expect(within(applied).getByText('PHI 3 d')).toBeInTheDocument()
    expect(within(applied).getByText('0.5 L/ha')).toBeInTheDocument()

    const cancelled = screen.getByRole('row', { name: /20 Aug 2026/ })
    expect(within(cancelled).getByText('Not sprayed')).toBeInTheDocument()

    const byIngredient = screen.getByRole('list', { name: 'Sprays by active ingredient' })
    expect(within(byIngredient).getByText('Azoxystrobin')).toBeInTheDocument()
    expect(within(byIngredient).getByText(/2×/)).toBeInTheDocument()
  })

  it('sends the date range to the API and filters status on the page', async () => {
    signIn()
    const requested: string[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/reports/plot-treatment-history`, ({ request }) => {
        requested.push(new URL(request.url).search)
        return HttpResponse.json(makeTreatmentHistory())
      }),
    )
    const { router } = renderApp('/plots/plot-1?from=2026-08-01&status=Applied')

    await waitFor(() => expect(requested).toContain('?plotId=plot-1&from=2026-08-01'))
    expect(await screen.findByRole('row', { name: /10 Sept? 2026/ })).toBeInTheDocument()
    expect(screen.queryByRole('row', { name: /20 Aug 2026/ })).not.toBeInTheDocument()
    expect(router.state.location.search).toBe('?from=2026-08-01&status=Applied')
  })

  it('distinguishes no sprays from no matches', async () => {
    signIn()
    server.use(
      http.get(`${API_BASE_URL}/api/reports/plot-treatment-history`, () =>
        HttpResponse.json(makeTreatmentHistory({ applications: 0, byActiveIngredient: [], rows: [] })),
      ),
    )
    renderApp('/plots/plot-1')

    expect(await screen.findByText('No sprays recorded')).toBeInTheDocument()
  })

  it('does not call the API with an inverted date range', async () => {
    signIn()
    let calls = 0
    server.use(
      http.get(`${API_BASE_URL}/api/reports/plot-treatment-history`, () => {
        calls += 1
        return HttpResponse.json(makeTreatmentHistory())
      }),
    )
    renderApp('/plots/plot-1?from=2026-09-10&to=2026-08-01')

    expect(await screen.findByText(/start date must be on or before the end date/)).toBeInTheDocument()
    expect(calls).toBe(0)
  })

  it('gives an agronomist the read-only view', async () => {
    signIn(agronomist)
    renderApp('/plots/plot-1')

    const products = await productsTable()
    expect(products.getByRole('row', { name: /Amistar 25 SC/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Advance to/ })).not.toBeInTheDocument()
  })

  it('explains a plot the caller may not see', async () => {
    signIn()
    server.use(http.get(`${API_BASE_URL}/api/plots/:id`, () => problem(403, 'Forbidden', 'You do not have access to this plot.')))
    renderApp('/plots/plot-9')

    expect(await screen.findByText('Could not load this')).toBeInTheDocument()
  })
})
