import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { Modal } from './Modal'

/** Mirrors real usage: an inline onClose, so its identity changes on every render. */
function Host({ onClosed }: { onClosed?: () => void } = {}) {
  const [open, setOpen] = useState(false)
  const [note, setNote] = useState('')

  return (
    <>
      <button onClick={() => setOpen(true)}>Open</button>
      <Modal
        open={open}
        onClose={() => {
          setOpen(false)
          onClosed?.()
        }}
        title="Advance to Flowering"
      >
        <form>
          <label htmlFor="date">Date reached</label>
          <input id="date" type="date" defaultValue="2026-09-22" />
          <label htmlFor="note">Note</label>
          <input id="note" value={note} onChange={(event) => setNote(event.target.value)} />
        </form>
      </Modal>
    </>
  )
}

describe('Modal', () => {
  it('keeps focus where the user put it while typing', async () => {
    // Regression: the focus effect depended on onClose, whose identity changes every render,
    // so each keystroke re-ran it and yanked focus back to the first field — text ended up
    // split across inputs.
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Open' }))

    await user.type(screen.getByLabelText('Note'), 'First flowers')

    expect(screen.getByLabelText('Note')).toHaveValue('First flowers')
    expect(screen.getByLabelText('Note')).toHaveFocus()
    expect(screen.getByLabelText('Date reached')).toHaveValue('2026-09-22')
  })

  it('moves focus into the dialog when it opens', async () => {
    const user = userEvent.setup()
    render(<Host />)

    await user.click(screen.getByRole('button', { name: 'Open' }))

    expect(screen.getByLabelText('Date reached')).toHaveFocus()
  })

  it('returns focus to the trigger when it closes', async () => {
    const user = userEvent.setup()
    render(<Host />)
    const trigger = screen.getByRole('button', { name: 'Open' })
    await user.click(trigger)

    await user.keyboard('{Escape}')

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(trigger).toHaveFocus()
  })

  it('is labelled by its heading for screen readers', async () => {
    const user = userEvent.setup()
    render(<Host />)

    await user.click(screen.getByRole('button', { name: 'Open' }))

    expect(screen.getByRole('dialog', { name: 'Advance to Flowering' })).toHaveAttribute('aria-modal', 'true')
  })
})
