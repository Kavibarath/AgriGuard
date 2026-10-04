import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useAuthStore } from '@/features/auth/auth-store'
import { agronomist, authResponse } from '@/test/msw'
import { renderApp } from '@/test/render'

describe('HomePage', () => {
  it('introduces AgriGuard and leads to the sign-in page', async () => {
    const user = userEvent.setup()
    const { router } = renderApp('/')

    expect(screen.getByRole('heading', { level: 1, name: /Crop advice you can trust/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'AgriGuard' })).toHaveAttribute('href', '/')
    expect(screen.getByRole('heading', { name: 'Eleven rules check it' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Field agronomists' })).toBeInTheDocument()

    // The one way in, under the name at the top: the hero and the page's end no longer repeat it.
    const signIn = screen.getAllByRole('link', { name: /^Sign in/ })
    expect(signIn).toHaveLength(1)
    await user.click(signIn[0])

    expect(await screen.findByRole('heading', { name: 'Sign in to the advisory console' })).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
  })

  it('offers a signed-in user their dashboard instead of the sign-in page', () => {
    useAuthStore.getState().setSession(authResponse(agronomist))
    renderApp('/')

    expect(screen.queryByRole('link', { name: /^Sign in/ })).not.toBeInTheDocument()
    for (const link of screen.getAllByRole('link', { name: /Open your dashboard/ })) {
      expect(link).toHaveAttribute('href', '/dashboard')
    }
  })

  it('shows the eleven safety rules one at a time, opening on the pre-harvest interval', () => {
    renderApp('/')

    const carousel = screen.getByRole('region', { name: 'Eleven rules stand between a proposal and the field.' })
    const active = within(carousel).getByRole('group')
    expect(active).toHaveAccessibleName('5 of 11')
    expect(active).toHaveTextContent('V5 · Hard stop')
    expect(active).toHaveTextContent('Pre-harvest interval is respected')
    expect(screen.getByRole('button', { name: 'See all 11 rules' })).toBeInTheDocument()
  })

  it('ends with the credits, each opening its source in a new tab, and a way back up', () => {
    renderApp('/')

    const footer = screen.getByRole('contentinfo')
    for (const [name, href] of [
      [/Open-Meteo/, 'https://open-meteo.com/'],
      [/OpenStreetMap contributors/, 'https://www.openstreetmap.org/copyright'],
      [/Andi Farruku on Pexels/, 'https://www.pexels.com/video/green-plants-on-the-field-7983392/'],
    ] as const) {
      const link = within(footer).getByRole('link', { name })
      expect(link).toHaveAttribute('href', href)
      expect(link).toHaveAttribute('target', '_blank')
      expect(link).toHaveAccessibleName(/opens in a new tab/)
    }
    expect(within(footer).getByRole('link', { name: 'Back to the top' })).toHaveAttribute('href', '#top')
    expect(within(footer).queryByRole('link', { name: /^Sign in/ })).not.toBeInTheDocument()
  })

  it('keeps the spotlight photo out of the accessibility tree', () => {
    renderApp('/')

    // The photo is decoration: nothing in it is named or announced.
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })
})
