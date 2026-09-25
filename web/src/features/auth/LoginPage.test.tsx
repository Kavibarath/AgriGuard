import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { renderApp } from '@/test/render'
import { agronomist, http, HttpResponse, PASSWORD, server } from '@/test/msw'
import { API_BASE_URL } from '@/lib/api'
import { useAuthStore } from './auth-store'

describe('LoginPage', () => {
  it('validates in the browser before calling the API', async () => {
    const user = userEvent.setup()
    renderApp('/login')

    await user.click(screen.getByRole('button', { name: /sign in/i }))

    expect(await screen.findByText('Enter a valid email address')).toBeInTheDocument()
    expect(screen.getByText('Enter your password')).toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toHaveAttribute('aria-invalid', 'true')
  })

  it('shows the API message for wrong credentials without revealing which half was wrong', async () => {
    const user = userEvent.setup()
    renderApp('/login')

    await user.type(screen.getByLabelText('Email'), agronomist.email)
    await user.type(screen.getByLabelText('Password'), 'not-the-password')
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid credentials.')
    expect(useAuthStore.getState().accessToken).toBeNull()
  })

  it('stores the session and lands on the dashboard after a successful login', async () => {
    const user = userEvent.setup()
    const { router } = renderApp('/login')

    await user.type(screen.getByLabelText('Email'), agronomist.email)
    await user.type(screen.getByLabelText('Password'), PASSWORD)
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    // Wait on the rendered page, not on router.state: the router's location updates before
    // React has rendered the new route, so querying the DOM straight after the router assertion
    // is a race — one this test lost on CI's slower runner.
    expect(await screen.findByText('Dr. Nimali Fernando')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/dashboard')
    expect(useAuthStore.getState().user?.role).toBe('FieldAgronomist')
    expect(localStorage.getItem('agriguard.auth')).toContain('refresh-FieldAgronomist')
  })

  it('returns to the page the user was trying to reach', async () => {
    const user = userEvent.setup()
    const { router } = renderApp('/farms')

    // Bounced to login by ProtectedRoute…
    expect(await screen.findByRole('heading', { name: 'AgriGuard' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')

    await user.type(screen.getByLabelText('Email'), agronomist.email)
    await user.type(screen.getByLabelText('Password'), PASSWORD)
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    // …and sent back afterwards.
    await waitFor(() => expect(router.state.location.pathname).toBe('/farms'))
  })

  it('never shows a server stack trace, only a short message', async () => {
    server.use(
      http.post(`${API_BASE_URL}/api/auth/login`, () =>
        HttpResponse.json(
          { status: 500, title: 'An unexpected error occurred', detail: 'Npgsql.NpgsqlException: Failed to connect to 127.0.0.1:5433\n   at Npgsql...' },
          { status: 500 },
        ),
      ),
    )
    const user = userEvent.setup()
    renderApp('/login')

    await user.type(screen.getByLabelText('Email'), agronomist.email)
    await user.type(screen.getByLabelText('Password'), PASSWORD)
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('The server had a problem. Try again in a moment.')
    expect(alert).not.toHaveTextContent(/Npgsql/)
  })

  it('explains a rate limit instead of failing silently', async () => {
    server.use(
      http.post(`${API_BASE_URL}/api/auth/login`, () =>
        HttpResponse.json(
          { status: 429, title: 'Too many requests', detail: 'Rate limit exceeded. Wait a moment and try again.' },
          { status: 429 },
        ),
      ),
    )
    const user = userEvent.setup()
    renderApp('/login')

    await user.type(screen.getByLabelText('Email'), agronomist.email)
    await user.type(screen.getByLabelText('Password'), PASSWORD)
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/rate limit exceeded/i)
  })
})
