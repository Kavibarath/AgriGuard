import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { API_BASE_URL } from '@/lib/api'
import { useAuthStore } from '@/features/auth/auth-store'
import { agronomist, authResponse, http, HttpResponse, server } from '@/test/msw'
import { makeCaseSummary, RUN_ID } from '@/test/agent-run-handlers'
import { paged } from '@/test/registry-handlers'
import { renderApp } from '@/test/render'

describe('AgentRunsPage', () => {
  beforeEach(() => useAuthStore.getState().setSession(authResponse(agronomist)))

  it('opens on the cases awaiting approval, each linked to its run', async () => {
    const requests: string[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/cases`, ({ request }) => {
        requests.push(new URL(request.url).search)
        return HttpResponse.json(paged([makeCaseSummary()]))
      }),
    )
    renderApp('/agent-runs')

    const link = await screen.findByRole('link', { name: 'AG-2026-000003' })
    expect(link).toHaveAttribute('href', `/agent-runs/${RUN_ID}`)
    const row = screen.getByRole('row', { name: /AG-2026-000003/ })
    expect(within(row).getAllByText('Awaiting approval').length).toBeGreaterThan(0)
    expect(requests[0]).toContain('status=PendingApproval')
  })

  it('asks the API for the chosen view, and for everything under "All cases"', async () => {
    const requests: string[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/cases`, ({ request }) => {
        requests.push(new URL(request.url).search)
        return HttpResponse.json(paged([makeCaseSummary()]))
      }),
    )
    const user = userEvent.setup()
    renderApp('/agent-runs')

    await screen.findByRole('link', { name: 'AG-2026-000003' })
    await user.selectOptions(screen.getByLabelText('Show'), 'AgentProcessing')
    await waitFor(() => expect(requests.at(-1)).toContain('status=AgentProcessing'))

    await user.selectOptions(screen.getByLabelText('Show'), 'all')
    await waitFor(() => expect(requests.at(-1)).not.toContain('status='))
  })

  it('explains an empty queue', async () => {
    server.use(http.get(`${API_BASE_URL}/api/cases`, () => HttpResponse.json(paged([]))))

    renderApp('/agent-runs')

    expect(await screen.findByText('Nothing to show under “Awaiting approval”')).toBeInTheDocument()
  })

  it('shows a case with no run yet without a link', async () => {
    server.use(
      http.get(`${API_BASE_URL}/api/cases`, () =>
        HttpResponse.json(paged([makeCaseSummary({ latestRunId: null, latestRunStatus: null, status: 'Submitted' })])),
      ),
    )

    renderApp('/agent-runs?view=all')

    expect(await screen.findByText('None yet')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'AG-2026-000003' })).not.toBeInTheDocument()
  })
})
