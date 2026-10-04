import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { API_BASE_URL } from '@/lib/api'
import { agronomist, authResponse, http, HttpResponse, server } from '@/test/msw'
import { makeCaseSummary } from '@/test/agent-run-handlers'
import { paged } from '@/test/registry-handlers'
import { useAuthStore } from '@/features/auth/auth-store'
import { renderApp } from '@/test/render'

function signIn() {
  useAuthStore.getState().setSession(authResponse(agronomist))
}

describe('CasesPage', () => {
  it('lists cases newest first and links each to its page', async () => {
    signIn()
    const requested: string[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/cases`, ({ request }) => {
        requested.push(new URL(request.url).search)
        return HttpResponse.json(paged([makeCaseSummary()]))
      }),
    )
    renderApp('/cases')

    const row = await screen.findByRole('row', { name: /AG-2026-000003/ })
    expect(within(row).getByRole('link', { name: 'AG-2026-000003' })).toHaveAttribute('href', '/cases/case-1')
    expect(within(row).getByText('High')).toBeInTheDocument()
    expect(requested[0]).toContain('sortBy=createdAt')
    expect(requested[0]).toContain('desc=true')
  })

  it('puts every case on the map, linked and described for screen readers', async () => {
    signIn()
    server.use(
      http.get(`${API_BASE_URL}/api/cases`, () =>
        HttpResponse.json(
          paged([
            makeCaseSummary(),
            makeCaseSummary({ id: 'case-2', referenceNo: 'AG-2026-000004', severity: 'Critical', reportedLatitude: 8.351, reportedLongitude: 80.504 }),
          ]),
        ),
      ),
    )
    renderApp('/cases')

    const map = await screen.findByRole('region', { name: /Map of the 2 cases/ })
    const pin = within(map).getByRole('link', { name: /AG-2026-000004: Tomato on P-01, critical severity/ })
    expect(pin).toHaveAttribute('href', '/cases/case-2')
    // Tiles come from OpenStreetMap, with the attribution its usage policy asks for.
    expect(map.querySelector('img')?.getAttribute('src')).toMatch(/^https:\/\/tile\.openstreetmap\.org\/\d+\/\d+\/\d+\.png$/)
    expect(within(map).getByRole('link', { name: 'OpenStreetMap' })).toBeInTheDocument()
  })

  it('keeps filters in the URL and sends them to the API', async () => {
    signIn()
    const requested: string[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/cases`, ({ request }) => {
        requested.push(new URL(request.url).search)
        return HttpResponse.json(paged([makeCaseSummary()]))
      }),
    )
    const user = userEvent.setup()
    const { router } = renderApp('/cases')

    await screen.findByRole('row', { name: /AG-2026-000003/ })
    await user.selectOptions(screen.getByLabelText('Severity'), 'Critical')

    expect(router.state.location.search).toBe('?severity=Critical')
    await waitFor(() => expect(requested.at(-1)).toContain('severity=Critical'))
  })

  it('opens with the filters a shared link carries', async () => {
    signIn()
    const requested: string[] = []
    server.use(
      http.get(`${API_BASE_URL}/api/cases`, ({ request }) => {
        requested.push(new URL(request.url).search)
        return HttpResponse.json(paged([makeCaseSummary({ status: 'Prescribed' })]))
      }),
    )
    renderApp('/cases?status=Prescribed&cropId=c-tom')

    await screen.findByRole('row', { name: /AG-2026-000003/ })
    expect(requested[0]).toContain('status=Prescribed')
    expect(requested[0]).toContain('cropId=c-tom')
    expect(screen.getByLabelText('Status')).toHaveValue('Prescribed')
  })

  it('tells an empty filter apart from an empty district', async () => {
    signIn()
    server.use(http.get(`${API_BASE_URL}/api/cases`, () => HttpResponse.json(paged([]))))
    renderApp('/cases?severity=Low')

    expect(await screen.findByText('No cases match these filters')).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: /Map of/ })).not.toBeInTheDocument()
  })
})
