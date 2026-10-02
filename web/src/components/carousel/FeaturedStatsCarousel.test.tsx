import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { FeaturedStatsCarousel, type FeaturedStat } from './FeaturedStatsCarousel'

const items: FeaturedStat[] = [
  { id: 'a', stat: '$70B+', highlight: 'Delivered to farmers', description: 'through direct assistance.' },
  { id: 'b', stat: '3', highlight: 'Active cases', description: 'after nearly 120 days.' },
  { id: 'c', stat: '$2.5B', highlight: 'Saved annually', description: 'in labour costs.' },
  { id: 'd', stat: '40%', highlight: 'Fresh foods', description: 'in the first year.' },
]

function renderCarousel(initialIndex = 0) {
  render(<FeaturedStatsCarousel items={items} title="Results" initialIndex={initialIndex} seeAllLabel="See all 4 results" />)
  return screen.getByRole('region', { name: 'Results' })
}

/** The one slide not hidden from assistive technology. */
function activeSlide() {
  return screen.getByRole('group')
}

describe('FeaturedStatsCarousel', () => {
  it('shows the chosen card as the active one, with its position', () => {
    renderCarousel(1)

    expect(activeSlide()).toHaveAccessibleName('2 of 4')
    expect(activeSlide()).toHaveTextContent('Active cases')
    expect(screen.getByText('2 of 4', { selector: 'p' })).toBeInTheDocument()
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '2')
  })

  it('moves exactly one card per arrow, and loops at either end', async () => {
    const user = userEvent.setup()
    renderCarousel(0)

    await user.click(screen.getByRole('button', { name: 'Next' }))
    expect(activeSlide()).toHaveTextContent('Active cases')

    await user.click(screen.getByRole('button', { name: 'Previous' }))
    await user.click(screen.getByRole('button', { name: 'Previous' }))
    expect(activeSlide()).toHaveTextContent('Fresh foods')
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '4')
  })

  it('answers the arrow, Home and End keys', async () => {
    const user = userEvent.setup()
    const region = renderCarousel(0)

    region.focus()
    await user.keyboard('{ArrowRight}{ArrowRight}')
    expect(activeSlide()).toHaveTextContent('Saved annually')
    await user.keyboard('{Home}')
    expect(activeSlide()).toHaveTextContent('Delivered to farmers')
    await user.keyboard('{End}')
    expect(activeSlide()).toHaveTextContent('Fresh foods')
  })

  it('brings a peeking neighbour to the centre when it is clicked', async () => {
    const user = userEvent.setup()
    renderCarousel(0)

    await user.click(screen.getByText('in labour costs.', { exact: false }))
    expect(activeSlide()).toHaveTextContent('Saved annually')
  })

  it('moves one card on a swipe, and springs back from a short one', () => {
    const region = renderCarousel(0)
    const viewport = region.firstElementChild!

    const swipe = (from: number, to: number) => {
      fireEvent.pointerDown(viewport, { pointerId: 1, button: 0, clientX: from, clientY: 100 })
      fireEvent.pointerMove(viewport, { pointerId: 1, clientX: to, clientY: 100 })
      fireEvent.pointerUp(viewport, { pointerId: 1, clientX: to, clientY: 100 })
    }

    swipe(300, 180)
    expect(activeSlide()).toHaveTextContent('Active cases')
    swipe(300, 270)
    expect(activeSlide()).toHaveTextContent('Active cases')
    swipe(100, 220)
    expect(activeSlide()).toHaveTextContent('Delivered to farmers')
  })

  it('opens the full list, and a row in it becomes the active card', async () => {
    const user = userEvent.setup()
    renderCarousel(0)

    const seeAll = screen.getByRole('button', { name: 'See all 4 results' })
    expect(seeAll).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('list')).not.toBeInTheDocument()

    await user.click(seeAll)
    expect(screen.getByRole('button', { name: 'Hide the list' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getAllByRole('listitem')).toHaveLength(4)

    await user.click(screen.getByRole('button', { name: /40%\s*Fresh foods/ }))
    expect(activeSlide()).toHaveTextContent('Fresh foods')
  })
})
