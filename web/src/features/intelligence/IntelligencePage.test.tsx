import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { API_BASE_URL } from '@/lib/api'
import { useAuthStore } from '@/features/auth/auth-store'
import { agronomist, authResponse, http, HttpResponse, server } from '@/test/msw'
import { makeSignal } from '@/test/harvest-handlers'
import { dealer } from '@/test/inventory-handlers'
import { renderApp } from '@/test/render'

/** Records the query string of every outbreak-signal request. */
function captureRequests(signal = makeSignal()) {
  const requests: URLSearchParams[] = []
  server.use(
    http.get(`${API_BASE_URL}/api/intelligence/outbreak-signal`, ({ request }) => {
      requests.push(new URL(request.url).searchParams)
      return HttpResponse.json(signal)
    }),
  )
  return requests
}

describe('IntelligencePage', () => {
  beforeEach(() => useAuthStore.getState().setSession(authResponse(agronomist)))

  it('shows the pressure index with its level and trend, the pathogens behind it and every district', async () => {
    renderApp('/intelligence')

    const headline = await screen.findByRole('region', { name: 'Headline figures' })
    expect(within(headline).getByText('78')).toBeInTheDocument()
    expect(within(headline).getByText('Severe')).toBeInTheDocument()
    expect(within(headline).getByText('↑ Rising')).toBeInTheDocument()
    expect(within(headline).getByText('85% of the pressure · 8 confirmed')).toBeInTheDocument()
    expect(screen.getByText(/Late blight pressure is severe in Nuwara Eliya/)).toBeInTheDocument()

    // The chart's numbers are also a table for screen readers.
    expect(screen.getByRole('table', { name: 'Cases per day, last 14 days' })).toBeInTheDocument()
    const matale = screen.getByRole('row', { name: /Matale/ })
    expect(within(matale).getByText('Low')).toBeInTheDocument()
    // A pathogen outside the named top list is still readable.
    expect(within(matale).getByText('Thrips')).toBeInTheDocument()
  })

  it('keeps its filters in the URL and asks the API for them', async () => {
    const requests = captureRequests()
    const user = userEvent.setup()
    const { router } = renderApp('/intelligence')

    await screen.findByRole('region', { name: 'Headline figures' })
    await user.selectOptions(screen.getByLabelText('Crop'), 'c-tom')
    await user.selectOptions(screen.getByLabelText('Window'), '30')

    await waitFor(() => expect(requests.some((q) => q.get('cropId') === 'c-tom' && q.get('days') === '30')).toBe(true))
    expect(router.state.location.search).toContain('cropId=c-tom')
  })

  it('focuses on a district when its row is chosen', async () => {
    const requests = captureRequests()
    const user = userEvent.setup()
    renderApp('/intelligence')

    await user.click(await screen.findByRole('row', { name: /Matale/ }))

    await waitFor(() => expect(requests.some((q) => q.get('districtId') === 'd-mtl')).toBe(true))
  })

  it('says so when nothing was reported in the window', async () => {
    captureRequests(makeSignal({ reportedCases: 0, confirmedCases: 0, topPathogens: [], districts: [] }))
    renderApp('/intelligence')

    expect(await screen.findByText('No cases reported in this window')).toBeInTheDocument()
  })

  it('is open to a dealer too: the signal names no farmer', async () => {
    useAuthStore.getState().setSession(authResponse(dealer))
    renderApp('/intelligence')

    expect(await screen.findByRole('heading', { name: 'Disease intelligence' })).toBeInTheDocument()
    expect(await screen.findByRole('region', { name: 'Headline figures' })).toBeInTheDocument()
  })
})
