import { useEffect, useId, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'

/**
 * Accessible modal: labelled by its heading, closes on Escape, moves focus in on open and back
 * to the trigger on close, and marks the rest of the page inert so a keyboard or screen-reader
 * user cannot tab behind it.
 *
 * Rendered into document.body through a portal. Opened from inside a card that animates in (a
 * transform makes its own stacking context), a fixed z-50 overlay would only rank within that
 * card, and a later sticky table header could paint over the dialog.
 */
export function Modal({
  open,
  onClose,
  title,
  description,
  children,
  size = 'md',
}: {
  /** `lg` for forms with grouped fields, such as the rules editor. */
  size?: 'md' | 'lg'
  open: boolean
  onClose: () => void
  title: string
  description?: string
  children: ReactNode
}) {
  const titleId = useId()
  const descriptionId = useId()
  const panelRef = useRef<HTMLDivElement>(null)
  const previouslyFocused = useRef<HTMLElement | null>(null)

  // Callers pass an inline arrow for onClose, so its identity changes on every render. Kept in
  // a ref and out of the effect's dependencies: otherwise the effect re-ran on each keystroke
  // and pulled focus back to the first field, scattering typed text across the form.
  const onCloseRef = useRef(onClose)
  useEffect(() => {
    onCloseRef.current = onClose
  })

  useEffect(() => {
    if (!open) return

    previouslyFocused.current = document.activeElement as HTMLElement | null
    // First field, or the panel itself when the dialog is only a confirmation.
    const firstField = panelRef.current?.querySelector<HTMLElement>('input, select, textarea, button')
    firstField?.focus()

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCloseRef.current()
    }
    document.addEventListener('keydown', onKeyDown)

    return () => {
      document.removeEventListener('keydown', onKeyDown)
      previouslyFocused.current?.focus()
    }
  }, [open])

  if (!open) return null

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      {/* Clicking the backdrop dismisses, matching the Escape key. */}
      <div className="absolute inset-0 bg-brand-900/45" onClick={onClose} aria-hidden="true" />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        className={`relative z-10 max-h-[calc(100vh-2rem)] w-full overflow-y-auto ${size === 'lg' ? 'max-w-2xl' : 'max-w-md'} space-y-4 rounded-xl border border-border-subtle bg-surface-card p-5 shadow-overlay`}
      >
        <header className="space-y-1">
          <h2 id={titleId} className="font-display text-xl font-semibold text-stone-900">
            {title}
          </h2>
          {description && (
            <p id={descriptionId} className="text-sm text-stone-600">
              {description}
            </p>
          )}
        </header>
        {children}
      </div>
    </div>,
    document.body,
  )
}
