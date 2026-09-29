import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { API_BASE_URL } from '@/lib/api'
import { agronomist, authResponse, farmer, http, HttpResponse, problem, server } from '@/test/msw'
import { INJECTED_NOTE, makeCaseDetail } from '@/test/agent-run-handlers'
import { useAuthStore } from '@/features/auth/auth-store'
import type { UserSummary } from '@/features/auth/types'
import type { CaseDetail } from '@/features/agent-runs/types'
import { renderApp } from '@/test/render'

function signIn(as: UserSummary = agronomist) {
  useAuthStore.getState().setSession(authResponse(as))
}

function serveCase(overrides: Partial<CaseDetail>) {
  server.use(http.get(`${API_BASE_URL}/api/cases/:id`, () => HttpResponse.json(makeCaseDetail(overrides))))
}

describe('CaseDetailPage', () => {
  it('shows what was reported, with the note as plain text', async () => {
    signIn()
    renderApp('/cases/case-1')

    expect(await screen.findByRole('heading', { name: 'Case AG-2026-000003' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'P-01' })).toHaveAttribute('href', '/plots/plot-1')
    expect(screen.getByText('Water-soaked patches on leaves')).toBeInTheDocument()
    // The injection attempt is visible as text and never becomes markup.
    expect(screen.getByText(INJECTED_NOTE)).toBeInTheDocument()
    expect(document.querySelector('main script')).toBeNull()
  })

  it('pins the phone and the plot on the map and says how far apart they were', async () => {
    signIn()
    renderApp('/cases/case-1')

    const map = await screen.findByRole('region', { name: /where AG-2026-000003 was reported/ })
    expect(within(map).getByRole('img', { name: /Where the phone was/ })).toBeInTheDocument()
    expect(within(map).getByRole('img', { name: 'Plot P-01, registered centre' })).toBeInTheDocument()
    expect(screen.getByText(/Reported \d+ m from the plot's registered centre/)).toBeInTheDocument()
  })

  it('warns when the report was made far from its plot', async () => {
    signIn()
    serveCase({ reportedLatitude: 7.05, reportedLongitude: 80.79 })
    renderApp('/cases/case-1')

    expect(await screen.findByText(/Reported 11\.\d km from the plot/)).toBeInTheDocument()
    expect(screen.getByText(/Check with the farmer that it is the right plot/)).toBeInTheDocument()
  })

  it('says when a report waited in the phone\'s offline queue', async () => {
    signIn()
    serveCase({ capturedAt: '2026-09-25T08:03:20Z', createdAt: '2026-09-25T11:03:20Z' })
    renderApp('/cases/case-1')

    expect(await screen.findByText("Sent from the phone's offline queue")).toBeInTheDocument()
    expect(screen.getByText(/after waiting 3 h for a connection/)).toBeInTheDocument()
  })

  it('lists the agent runs and the advice the farmer was given', async () => {
    signIn()
    renderApp('/cases/case-1')

    const runs = await screen.findByRole('region', { name: 'Agent runs' })
    expect(within(runs).getByRole('link', { name: /Run of/ })).toHaveAttribute('href', '/agent-runs/run-1')
    const advice = screen.getByRole('region', { name: 'Advice the farmer was given' })
    expect(within(advice).getAllByRole('listitem')).toHaveLength(2)
  })

  it('offers no hand changes while a proposal awaits a decision', async () => {
    signIn()
    renderApp('/cases/case-1')

    await screen.findByRole('heading', { name: 'Case AG-2026-000003' })
    expect(screen.queryByRole('button', { name: 'Close case' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Take into manual review' })).not.toBeInTheDocument()
  })

  it('lets an agronomist take a fresh case into manual review', async () => {
    signIn()
    serveCase({ status: 'Submitted', agentRuns: [] })
    let body: unknown = null
    server.use(
      http.patch(`${API_BASE_URL}/api/cases/:id/status`, async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(makeCaseDetail({ status: 'AwaitingManualReview' }))
      }),
    )
    const user = userEvent.setup()
    renderApp('/cases/case-1')

    await user.click(await screen.findByRole('button', { name: 'Take into manual review' }))
    await waitFor(() => expect(body).toEqual({ status: 'AwaitingManualReview' }))
  })

  it('asks before closing, because a closed case cannot be reopened', async () => {
    signIn(farmer)
    serveCase({ status: 'Prescribed' })
    let body: unknown = null
    server.use(
      http.patch(`${API_BASE_URL}/api/cases/:id/status`, async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(makeCaseDetail({ status: 'Closed' }))
      }),
    )
    const user = userEvent.setup()
    renderApp('/cases/case-1')

    // Manual review is an agronomist's call; the farmer is not offered it.
    await user.click(await screen.findByRole('button', { name: 'Close case' }))
    expect(screen.queryByRole('button', { name: 'Take into manual review' })).not.toBeInTheDocument()
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/cannot be reopened/)).toBeInTheDocument()
    expect(body).toBeNull()

    await user.click(within(dialog).getByRole('button', { name: 'Close case' }))
    await waitFor(() => expect(body).toEqual({ status: 'Closed' }))
  })

  it('shows the server\'s reason when it refuses a change', async () => {
    signIn()
    serveCase({ status: 'Submitted', agentRuns: [] })
    server.use(
      http.patch(`${API_BASE_URL}/api/cases/:id/status`, () =>
        problem(422, 'Business rule violated', 'The agent is still working on this case. Wait for the run to finish.', { code: 'ILLEGAL_CASE_STATUS_CHANGE' }),
      ),
    )
    const user = userEvent.setup()
    renderApp('/cases/case-1')

    await user.click(await screen.findByRole('button', { name: 'Take into manual review' }))
    expect(await screen.findByText(/still working on this case/)).toBeInTheDocument()
  })
})
