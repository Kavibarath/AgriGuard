import { useId, useRef, useState, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react'
import { ChevronLeft, ChevronRight } from '@/components/icons'
import { cn } from '@/lib/utils'

export interface FeaturedStat {
  id: string
  /** The headline figure, set large in the serif face. */
  stat: string
  /** The phrase marked like a highlighter, opening the card's sentence. */
  highlight: string
  /** The rest of the sentence, after the highlight. */
  description: string
  /** A small line above the figure (a code, a category). */
  eyebrow?: ReactNode
}

/** Swipes shorter than this (px) spring back instead of moving a card. */
const SWIPE_THRESHOLD = 60

/**
 * Where a card sits for its distance from the active one: how many steps sideways, how small, how
 * faint. Three or more away it is out of sight, so the jump when the loop wraps is never seen.
 */
function placement(offset: number) {
  const distance = Math.abs(offset)
  const steps = [0, 1, 1.72][distance] ?? 2.4
  return {
    steps: Math.sign(offset) * steps,
    scale: [1, 0.9, 0.8][distance] ?? 0.75,
    opacity: [1, 1, 0.45][distance] ?? 0,
    blur: [0, 1, 2][distance] ?? 2,
  }
}

/**
 * An editorial carousel of headline figures: the active card in the middle, its neighbours peeking
 * in on either side. Arrows move one card; so do swipes, the arrow keys, and clicking a neighbour.
 * It loops, so there is always something on both sides. Under reduced motion it changes instantly
 * (the transition is gated in index.css).
 */
export function FeaturedStatsCarousel({
  items,
  title,
  kicker,
  intro,
  initialIndex = 0,
  seeAllLabel = `See all ${items.length} results`,
  titleId: titleIdProp,
}: {
  items: FeaturedStat[]
  title: string
  /** A short line above the title. */
  kicker?: string
  intro?: ReactNode
  initialIndex?: number
  seeAllLabel?: string
  /** For a surrounding section's aria-labelledby. */
  titleId?: string
}) {
  const generatedId = useId()
  const titleId = titleIdProp ?? `${generatedId}-title`
  const listId = `${generatedId}-all`
  const count = items.length
  const [active, setActive] = useState(() => Math.min(Math.max(initialIndex, 0), count - 1))
  const [showAll, setShowAll] = useState(false)
  const [dragX, setDragX] = useState(0)
  const drag = useRef<{ pointerId: number; x: number; y: number; moving: boolean } | null>(null)
  const swallowClick = useRef(false)

  const go = (to: number) => setActive(((to % count) + count) % count)
  const step = (by: number) => go(active + by)

  /** The shortest way round the loop from the active card: −2 is two to the left. */
  const offsetOf = (i: number) => {
    let offset = i - active
    if (offset > count / 2) offset -= count
    if (offset < -count / 2) offset += count
    return offset
  }

  function onKeyDown(e: KeyboardEvent) {
    const moves: Record<string, () => void> = {
      ArrowLeft: () => step(-1),
      ArrowRight: () => step(1),
      Home: () => go(0),
      End: () => go(count - 1),
    }
    const move = moves[e.key]
    if (!move) return
    e.preventDefault()
    move()
  }

  function onPointerDown(e: PointerEvent<HTMLDivElement>) {
    swallowClick.current = false
    if (e.button !== 0) return
    drag.current = { pointerId: e.pointerId, x: e.clientX, y: e.clientY, moving: false }
  }

  function onPointerMove(e: PointerEvent<HTMLDivElement>) {
    const d = drag.current
    if (!d || d.pointerId !== e.pointerId) return
    const dx = e.clientX - d.x
    if (!d.moving) {
      if (Math.abs(dx) < 8) return
      // Mostly vertical: the page is being scrolled, not the cards.
      if (Math.abs(e.clientY - d.y) > Math.abs(dx)) {
        drag.current = null
        return
      }
      // Captured only once it is a drag, so a plain click still reaches the card under it.
      d.moving = true
      e.currentTarget.setPointerCapture?.(e.pointerId)
    }
    setDragX(dx)
  }

  function onPointerEnd(e: PointerEvent<HTMLDivElement>) {
    const d = drag.current
    drag.current = null
    if (!d?.moving || d.pointerId !== e.pointerId) return
    const dx = e.clientX - d.x
    if (Math.abs(dx) >= SWIPE_THRESHOLD) step(dx < 0 ? 1 : -1)
    setDragX(0)
    swallowClick.current = true
  }

  const current = items[active]

  return (
    <div>
      <header className="mx-auto max-w-3xl px-5 text-center">
        {kicker && <p className="text-sm font-semibold tracking-wide text-stone-600">{kicker}</p>}
        <h2 id={titleId} className="font-display mt-2 text-4xl leading-tight font-semibold text-balance text-stone-900 sm:text-[2.75rem]">
          {title}
        </h2>
        <span aria-hidden="true" className="mx-auto mt-6 block h-[3px] w-16 rounded-full bg-earth-300" />
        {intro && <p className="mx-auto mt-6 max-w-xl text-base text-pretty text-stone-600">{intro}</p>}
      </header>

      <div
        role="region"
        aria-roledescription="carousel"
        aria-labelledby={titleId}
        tabIndex={0}
        onKeyDown={onKeyDown}
        className="mt-12 rounded-3xl focus-visible:outline-offset-4"
      >
        {/* The viewport: full width, clipped, with room above and below for the active card's shadow. */}
        <div
          className={cn(
            'touch-pan-y overflow-hidden py-8 lg:[mask-image:linear-gradient(to_right,transparent,black_7%,black_93%,transparent)]',
            dragX !== 0 && 'select-none',
          )}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerEnd}
          onPointerCancel={onPointerEnd}
          onClickCapture={(e) => {
            if (!swallowClick.current) return
            swallowClick.current = false
            e.stopPropagation()
          }}
        >
          {/*
           * Every card shares one grid cell, so the stage is as tall as the tallest card, and each is
           * moved sideways from the centre. The step is the distance to a neighbour; on a phone the
           * row starts a little left so the next card shows at the right edge.
           */}
          <div
            className={cn(
              'grid justify-items-center [--carousel-origin:-6vw] [--carousel-step:80vw] sm:[--carousel-origin:0px] sm:[--carousel-step:24rem] lg:[--carousel-step:25.5rem]',
              dragX !== 0 && 'is-dragging',
            )}
          >
            {items.map((item, i) => {
              const offset = offsetOf(i)
              const isActive = offset === 0
              const { steps, scale, opacity, blur } = placement(offset)
              return (
                <div
                  key={item.id}
                  role="group"
                  aria-roledescription="slide"
                  aria-label={`${i + 1} of ${count}`}
                  aria-hidden={!isActive}
                  data-active={isActive || undefined}
                  onClick={isActive ? undefined : () => go(i)}
                  className={cn(
                    'carousel-card col-start-1 row-start-1 flex w-[76vw] flex-col items-center justify-center rounded-[22px] px-7 py-12 text-center sm:w-[30rem] sm:px-12 sm:py-14 lg:w-[34rem]',
                    isActive
                      ? 'border border-border-subtle bg-surface-card shadow-[0_20px_44px_-12px_rgb(19_48_37/0.16),0_2px_6px_rgb(19_48_37/0.05)]'
                      : 'cursor-pointer border border-transparent bg-surface-inset',
                  )}
                  style={{
                    transform: `translateX(calc(var(--carousel-origin) + var(--carousel-step) * ${steps} + ${dragX}px)) scale(${scale})`,
                    opacity,
                    filter: blur ? `blur(${blur}px)` : undefined,
                    zIndex: 10 - Math.abs(offset),
                    visibility: opacity === 0 ? 'hidden' : undefined,
                  }}
                >
                  {item.eyebrow && <div className="text-xs font-semibold tracking-[0.08em] text-stone-500 uppercase">{item.eyebrow}</div>}
                  <p className="font-display mt-4 text-[2.625rem] leading-none font-semibold tracking-tight text-stone-900 sm:text-6xl">{item.stat}</p>
                  <p className="mt-6 max-w-md text-base leading-relaxed text-pretty text-stone-700 sm:text-[17px]">
                    <mark className="rounded-[3px] bg-warning-200/80 box-decoration-clone px-1 font-semibold text-stone-900">{item.highlight}.</mark>{' '}
                    {item.description}
                  </p>
                </div>
              )
            })}
          </div>
        </div>

        <div className="mt-4 flex items-center justify-center gap-6 sm:gap-10">
          <CarouselButton label="Previous" onClick={() => step(-1)}>
            <ChevronLeft size={22} />
          </CarouselButton>
          <div className="flex w-36 flex-col items-center gap-3">
            <div
              role="progressbar"
              aria-label="Position in the carousel"
              aria-valuemin={1}
              aria-valuemax={count}
              aria-valuenow={active + 1}
              aria-valuetext={`${active + 1} of ${count}`}
              className="h-[3px] w-full overflow-hidden rounded-full bg-stone-300/70"
            >
              <div className="carousel-progress h-full rounded-full bg-stone-900" style={{ width: `${((active + 1) / count) * 100}%` }} />
            </div>
            <p className="font-display text-base text-stone-700">
              {active + 1} of {count}
            </p>
          </div>
          <CarouselButton label="Next" onClick={() => step(1)}>
            <ChevronRight size={22} />
          </CarouselButton>
        </div>

        {/* For screen readers: what the arrows just brought into view. */}
        <p className="sr-only" aria-live="polite" aria-atomic="true">
          {`${active + 1} of ${count}: ${current.stat}. ${current.highlight}.`}
        </p>
      </div>

      <div className="mt-8 text-center">
        <button
          type="button"
          aria-expanded={showAll}
          aria-controls={listId}
          onClick={() => setShowAll((v) => !v)}
          className="text-base font-semibold text-stone-900 underline decoration-stone-900/40 decoration-2 underline-offset-[6px] hover:decoration-stone-900"
        >
          {showAll ? 'Hide the list' : seeAllLabel}
        </button>
      </div>

      <ol id={listId} hidden={!showAll} className="mx-auto mt-10 grid max-w-5xl gap-x-10 px-5 sm:grid-cols-2 sm:px-10 lg:grid-cols-3">
        {items.map((item, i) => (
          <li key={item.id} className="border-t border-border-subtle">
            <button
              type="button"
              onClick={() => go(i)}
              aria-current={i === active || undefined}
              className="group flex w-full items-baseline gap-4 py-4 text-left"
            >
              <span className="font-display w-28 shrink-0 text-xl font-semibold text-stone-900">{item.stat}</span>
              <span>
                <span className="block font-medium text-stone-900 group-hover:underline">{item.highlight}</span>
                {item.eyebrow && <span className="mt-0.5 block text-xs text-stone-500">{item.eyebrow}</span>}
              </span>
            </button>
          </li>
        ))}
      </ol>
    </div>
  )
}

function CarouselButton({ label, onClick, children }: { label: string; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      aria-label={label}
      onClick={onClick}
      className="press grid size-14 shrink-0 place-items-center rounded-full border border-stone-400/70 bg-transparent text-stone-900 hover:border-stone-600 hover:bg-surface-card"
    >
      {children}
    </button>
  )
}
