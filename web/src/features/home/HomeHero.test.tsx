import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useAuthStore } from '@/features/auth/auth-store'
import { agronomist, authResponse } from '@/test/msw'
import { renderApp } from '@/test/render'

/** A matchMedia that says whether reduced motion is asked for; any other query does not match. */
function stubReducedMotion(reduce: boolean) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query.includes('prefers-reduced-motion') ? reduce : false,
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
}

describe('Home hero', () => {
  afterEach(() => vi.unstubAllGlobals())

  describe('background', () => {
    it('plays the self-hosted field video, muted and out of the accessibility tree', () => {
      stubReducedMotion(false)
      const { container } = renderApp('/')

      const video = container.querySelector('video')!
      expect(video).not.toBeNull()
      expect(video.autoplay).toBe(true)
      expect(video.muted).toBe(true)
      expect(video.loop).toBe(true)
      expect(video).toHaveAttribute('playsinline')
      expect(video).toHaveAttribute('aria-hidden', 'true')
      expect(video).toHaveAttribute('tabindex', '-1')
      expect(video).toHaveAttribute('poster', '/img/hero-field-poster.webp')
      expect(video.querySelector('source')).toHaveAttribute('src', '/video/hero-field.mp4')
    })

    it('with reduced motion, shows only the still: the video is never mounted', () => {
      stubReducedMotion(true)
      const { container } = renderApp('/')

      expect(container.querySelector('video')).toBeNull()
      const still = [...container.querySelectorAll<HTMLElement>('[style]')].find((el) => el.style.backgroundImage.includes('hero-field-poster'))
      expect(still).toBeDefined()
      expect(screen.queryByRole('img')).not.toBeInTheDocument()
    })
  })

  describe('phone menu', () => {
    it('is not in the page until opened', () => {
      renderApp('/')

      expect(screen.queryByRole('dialog', { name: 'Menu' })).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Open menu' })).toHaveAttribute('aria-expanded', 'false')
    })

    it('opens over the page, locks its scrolling, and closes on Escape, giving focus back', async () => {
      const user = userEvent.setup()
      renderApp('/')
      const opener = screen.getByRole('button', { name: 'Open menu' })

      await user.click(opener)

      const menu = screen.getByRole('dialog', { name: 'Menu' })
      expect(opener).toHaveAttribute('aria-expanded', 'true')
      expect(opener).toHaveAttribute('aria-controls', menu.id)
      expect(within(menu).getByRole('button', { name: 'Close menu' })).toHaveFocus()
      expect(document.body).toHaveClass('overflow-hidden')
      for (const name of ['How it works', 'Who it is for', 'Safety']) {
        expect(within(menu).getByRole('link', { name })).toBeInTheDocument()
      }

      await user.keyboard('{Escape}')

      expect(screen.queryByRole('dialog', { name: 'Menu' })).not.toBeInTheDocument()
      expect(document.body).not.toHaveClass('overflow-hidden')
      expect(screen.getByRole('button', { name: 'Open menu' })).toHaveFocus()
    })

    it('keeps Tab inside the menu while it is open', async () => {
      const user = userEvent.setup()
      renderApp('/')
      await user.click(screen.getByRole('button', { name: 'Open menu' }))
      const menu = screen.getByRole('dialog', { name: 'Menu' })
      const close = within(menu).getByRole('button', { name: 'Close menu' })
      const signIn = within(menu).getByRole('link', { name: /^Sign in/ })

      // Back from the first item wraps to the last; on from the last wraps to the first.
      await user.tab({ shift: true })
      expect(signIn).toHaveFocus()
      await user.tab()
      expect(close).toHaveFocus()
    })

    it('closes when a section is chosen', async () => {
      const user = userEvent.setup()
      renderApp('/')
      await user.click(screen.getByRole('button', { name: 'Open menu' }))

      await user.click(within(screen.getByRole('dialog', { name: 'Menu' })).getByRole('link', { name: 'Safety' }))

      expect(screen.queryByRole('dialog', { name: 'Menu' })).not.toBeInTheDocument()
      expect(document.body).not.toHaveClass('overflow-hidden')
    })

    it('ends with the same way in as the page: the dashboard for a signed-in user', async () => {
      useAuthStore.getState().setSession(authResponse(agronomist))
      const user = userEvent.setup()
      renderApp('/')
      await user.click(screen.getByRole('button', { name: 'Open menu' }))

      const menu = screen.getByRole('dialog', { name: 'Menu' })
      expect(within(menu).getByRole('link', { name: /Open your dashboard/ })).toHaveAttribute('href', '/dashboard')
      expect(within(menu).queryByRole('link', { name: /^Sign in/ })).not.toBeInTheDocument()
    })
  })

  it('every section the navigation names exists on the page', () => {
    const { container } = renderApp('/')

    for (const link of within(screen.getByRole('navigation', { name: 'Main' })).getAllByRole('link')) {
      const id = link.getAttribute('href')!.slice(1)
      expect(container.querySelector(`#${id}`)).not.toBeNull()
    }
  })
})
