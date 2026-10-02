import { render } from '@testing-library/react'
import { LivingCanopy } from './LivingCanopy'

/** A matchMedia that answers the pointer and reduced-motion queries as told. */
function stubMedia({ finePointer, reduce = false }: { finePointer: boolean; reduce?: boolean }) {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query.includes('pointer: fine') ? finePointer : query.includes('prefers-reduced-motion') ? reduce : false,
    addEventListener: () => {},
    removeEventListener: () => {},
  }))
}

describe('LivingCanopy', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('draws on a canvas that is hidden from assistive technology and lets clicks through', () => {
    stubMedia({ finePointer: true })
    const { container } = render(<LivingCanopy />)

    const canvas = container.querySelector('canvas')!
    expect(canvas).toHaveAttribute('aria-hidden', 'true')
    expect(canvas).toHaveClass('pointer-events-none')
  })

  it('leaves a touch screen’s header plain: there is no cursor to follow', () => {
    stubMedia({ finePointer: false })
    const { container } = render(<LivingCanopy />)

    expect(container.querySelector('canvas')).toBeNull()
  })
})
