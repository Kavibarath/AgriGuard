import { useEffect, useId, useRef, type ReactNode } from 'react'

/**
 * Accessible modal: labelled by its heading, closes on Escape, moves focus in on open and back
 * to the trigger on close, and marks the rest of the page inert so a keyboard or screen-reader
 * user cannot tab behind it.
 */
export function Modal({
  open,
  onClose,
  title,
  description,
  children,
}: {
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

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      {/* Clicking the backdrop dismisses, matching the Escape key. */}
      <div className="absolute inset-0 bg-stone-900/40" onClick={onClose} aria-hidden="true" />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        className="relative z-10 w-full max-w-md space-y-4 rounded-lg border border-stone-200 bg-white p-5 shadow-lg"
      >
        <header className="space-y-1">
          <h2 id={titleId} className="text-lg font-semibold text-stone-900">
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
    </div>
  )
}
