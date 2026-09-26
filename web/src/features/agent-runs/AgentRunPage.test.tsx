import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { API_BASE_URL } from '@/lib/api'
import { useAuthStore } from '@/features/auth/auth-store'
import { agronomist, authResponse, farmer, http, HttpResponse, problem, server } from '@/test/msw'
import { INJECTED_NOTE, makeDecisionResult, makeRun, RUN_ID } from '@/test/agent-run-handlers'
import { renderApp } from '@/test/render'

function signIn(as = agronomist) {
  useAuthStore.getState().setSession(authResponse(as))
}

const decisionUrl = `${API_BASE_URL}/api/agent-runs/:id/decision`

/** Records every decision request: its body and its Idempotency-Key. */
function captureDecisions() {
  const sent: { body: unknown; key: string | null }[] = []
  server.use(
    http.post(decisionUrl, async ({ request }) => {
      sent.push({ body: await request.json(), key: request.headers.get('Idempotency-Key') })
      return HttpResponse.json(makeDecisionResult())
    }),
  )
  return sent
}

describe('AgentRunPage', () => {
  it('shows the case, each agent’s step, the diagnosis, the proposal and all eleven rules', async () => {
    signIn()
    renderApp(`/agent-runs/${RUN_ID}`)

    expect(await screen.findByRole('heading', { name: 'Case AG-2026-000003' })).toBeInTheDocument()

    const steps = screen.getByRole('region', { name: 'Plan and steps' })
    for (const role of ['Coordinator', 'Diagnosis', 'Action', 'Validation']) {
      expect(within(steps).getAllByText(role).length).toBeGreaterThan(0)
    }
    expect(within(steps).getByText(/LATE_BLIGHT/)).toBeInTheDocument()
    expect(within(steps).getByText('82%')).toBeInTheDocument()
    // A tool retry inside the step is surfaced, not hidden.
    expect(within(steps).getByText(/1 tool call attempt failed/)).toBeInTheDocument()

    const proposal = screen.getByRole('region', { name: 'Proposed treatment' })
    expect(within(proposal).getByText('Azoxystrobin 25 SC')).toBeInTheDocument()
    expect(within(proposal).getByText('2026-09-26')).toBeInTheDocument()

    const rules = within(screen.getByRole('region', { name: 'Safety rules' })).getAllByRole('row').slice(1)
    expect(rules).toHaveLength(11)
    // "Not checked" is shown as such, not folded into the passes.
    expect(within(rules[7]).getByText('Not checked')).toBeInTheDocument()
  })

  it('shows the farmer’s note as plain text, never as markup', async () => {
    signIn()
    const { container } = renderApp(`/agent-runs/${RUN_ID}`)

    const note = await screen.findByText(INJECTED_NOTE)
    expect(note.tagName).toBe('BLOCKQUOTE')
    expect(container.querySelector('script')).toBeNull()
    // The timeline flags that the note tried to steer the model.
    expect(screen.getByText(/instruction-like text in the note/i)).toBeInTheDocument()
  })

  it('approves with an Idempotency-Key after a confirmation, then shows the prescription', async () => {
    signIn()
    const sent = captureDecisions()
    const user = userEvent.setup()
    renderApp(`/agent-runs/${RUN_ID}`)

    await user.click(await screen.findByRole('button', { name: 'Approve' }))
    const dialog = await screen.findByRole('dialog', { name: /approve and issue/i })
    expect(within(dialog).getByText(/takes the stock off the shelf/i)).toBeInTheDocument()

    // Once decided, the API reports the run completed with its prescription.
    server.use(
      http.get(`${API_BASE_URL}/api/agent-runs/:id`, () =>
        HttpResponse.json(makeRun({ status: 'Completed', prescription: makeDecisionResult().prescription })),
      ),
    )
    await user.click(within(dialog).getByRole('button', { name: 'Approve and issue' }))

    await waitFor(() => expect(sent).toHaveLength(1))
    expect(sent[0].body).toEqual({ decision: 'Approve' })
    expect(sent[0].key).toMatch(/^[0-9a-f-]{36}$/)
    expect(await screen.findByRole('heading', { name: 'Prescription RX-2026-000001 issued' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
  })

  it('retries a failed request with the same key, so it can never approve twice', async () => {
    signIn()
    const keys: (string | null)[] = []
    server.use(
      http.post(decisionUrl, ({ request }) => {
        keys.push(request.headers.get('Idempotency-Key'))
        // First attempt: the connection drops. Second: it goes through.
        return keys.length === 1 ? HttpResponse.error() : HttpResponse.json(makeDecisionResult())
      }),
    )
    const user = userEvent.setup()
    renderApp(`/agent-runs/${RUN_ID}`)

    await user.click(await screen.findByRole('button', { name: 'Approve' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Approve and issue' }))
    expect(await within(dialog).findByText(/could not reach/i)).toBeInTheDocument()

    await user.click(within(dialog).getByRole('button', { name: 'Approve and issue' }))

    await waitFor(() => expect(keys).toHaveLength(2))
    expect(keys[1]).toBe(keys[0])
  })

  it('needs a real reason to reject, and sends it', async () => {
    signIn()
    const sent = captureDecisions()
    const user = userEvent.setup()
    renderApp(`/agent-runs/${RUN_ID}`)

    await user.click(await screen.findByRole('button', { name: 'Reject' }))
    const dialog = await screen.findByRole('dialog')
    const reason = within(dialog).getByLabelText('Reason for rejecting')

    await user.type(reason, 'no')
    await user.click(within(dialog).getByRole('button', { name: 'Reject' }))
    expect(await within(dialog).findByText(/say what is wrong/i)).toBeInTheDocument()
    expect(sent).toHaveLength(0)

    await user.clear(reason)
    await user.type(reason, 'Symptoms fit bacterial wilt, not blight.')
    await user.click(within(dialog).getByRole('button', { name: 'Reject' }))

    await waitFor(() => expect(sent).toHaveLength(1))
    expect(sent[0].body).toEqual({ decision: 'Reject', reason: 'Symptoms fit bacterial wilt, not blight.' })
  })

  it('keeps revision guidance within what the agent accepts', async () => {
    signIn()
    const sent = captureDecisions()
    const user = userEvent.setup()
    renderApp(`/agent-runs/${RUN_ID}`)

    await user.click(await screen.findByRole('button', { name: 'Request revision' }))
    const dialog = await screen.findByRole('dialog')
    const guidance = within(dialog).getByLabelText('What should the agent change?')

    await user.click(guidance)
    await user.paste('x'.repeat(301))
    await user.click(within(dialog).getByRole('button', { name: 'Request revision' }))
    expect(await within(dialog).findByText(/under 300 characters/i)).toBeInTheDocument()
    expect(sent).toHaveLength(0)
  })

  it('explains a proposal that no longer passes the rules at approval time', async () => {
    signIn()
    server.use(
      http.post(decisionUrl, () =>
        problem(422, 'Business rule violated', 'The proposal no longer passes the safety rules (Rejected: V1.). Request a revision instead.', {
          code: 'PROPOSAL_NO_LONGER_VALID',
        }),
      ),
    )
    const user = userEvent.setup()
    renderApp(`/agent-runs/${RUN_ID}`)

    await user.click(await screen.findByRole('button', { name: 'Approve' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Approve and issue' }))

    expect(await within(dialog).findByText(/no longer passes the safety rules/i)).toBeInTheDocument()
  })

  it('shows a non-agronomist the run but not the decision', async () => {
    signIn({ ...farmer, role: 'CoopAdministrator' })
    renderApp(`/agent-runs/${RUN_ID}`)

    expect(await screen.findByText(/only a field agronomist/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
  })

  it('explains a failed run and where the case went', async () => {
    signIn()
    server.use(
      http.get(`${API_BASE_URL}/api/agent-runs/:id`, () =>
        HttpResponse.json(makeRun({ status: 'Failed', failureReason: 'The language model is unreachable.', proposal: null, verdict: null })),
      ),
    )
    renderApp(`/agent-runs/${RUN_ID}`)

    expect(await screen.findByText(/language model is unreachable/i)).toBeInTheDocument()
    expect(screen.getByText(/gone to manual review/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
  })

  it('updates live while the agent is working', async () => {
    signIn()
    let reads = 0
    server.use(
      http.get(`${API_BASE_URL}/api/agent-runs/:id`, () => {
        reads += 1
        return HttpResponse.json(reads === 1 ? makeRun({ status: 'Diagnosing', proposal: null, verdict: null }) : makeRun())
      }),
    )
    renderApp(`/agent-runs/${RUN_ID}`)

    expect(await screen.findByText('Updating live')).toBeInTheDocument()
    // The next poll brings the finished proposal, and polling stops.
    expect(await screen.findByRole('button', { name: 'Approve' }, { timeout: 5000 })).toBeInTheDocument()
    expect(screen.queryByText('Updating live')).not.toBeInTheDocument()
  })
})
