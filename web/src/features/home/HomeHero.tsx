import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { Link } from 'react-router'
import { ArrowRight, User } from '@/components/icons'
import { BrandMark } from '@/components/layout/BrandMark'
import { LivingCanopy } from '@/components/motion/LivingCanopy'
import { SPOTLIGHT_R, useSmoothedCursor } from '@/components/spotlight/use-spotlight'
import { usePrefersReducedMotion } from '@/lib/motion'
import { useHideOnScroll } from '@/lib/use-hide-on-scroll'
import { cn } from '@/lib/utils'

/**
 * Smallholder brassica plots, a slow pan (Pexels 7983392; docs/design/IMAGE-CREDITS.md). Served from
 * this app, never a CDN, so the demo does not depend on anyone else's bandwidth.
 */
const VIDEO = '/video/hero-field.mp4'
/** The video's own first frame, so the hand-over from poster to video does not jump. */
const POSTER = '/img/hero-field-poster.webp'

/**
 * The clip is a pan, so its last frame is not its first. This long before the end the video fades
 * out onto the poster (its first frame); the loop then restarts on that same frame and fades back
 * in, so the seam is a dissolve rather than a cut.
 */
const LOOP_FADE_SECONDS = 0.9

/** The in-page sections the menus lead to, in page order. */
const sections = [
  { href: '#how-it-works', label: 'How it works' },
  { href: '#who-it-is-for', label: 'Who it is for' },
  { href: '#safety', label: 'Safety' },
]

const MENU_ID = 'home-menu'

/** The one easing of the menu: the bars, the backdrop and the links. */
const MENU_EASE = 'motion-safe:ease-[cubic-bezier(0.76,0,0.24,1)]'

export interface EnterLink {
  to: string
  label: string
}

/**
 * The landing page's opening screen, centred like a public-service front page: the name on a plain
 * band, the navigation under it with the one way in (a menu on phones), then a field on video under
 * a canopy scrim carrying the promise and the three numbers that make it a product, not a splash.
 * Together they fill the first screen.
 */
export function HomeHero({ enter }: { enter: EnterLink }) {
  const [menuOpen, setMenuOpen] = useState(false)
  const menuButton = useRef<HTMLButtonElement>(null)
  // The name in the white band: the leaves there keep clear of it.
  const brand = useRef<HTMLAnchorElement>(null)
  const header = useRef<HTMLElement>(null)
  const headerSpace = useRef<HTMLDivElement>(null)
  const { visible, atTop, reveal } = useHideOnScroll()

  // The header is fixed, so it takes no room; its placeholder matches its height, before the first
  // paint and whenever it changes (a breakpoint, the web font arriving). Transforms do not change
  // offsetHeight, so hiding it moves nothing below.
  useLayoutEffect(() => {
    const el = header.current
    const space = headerSpace.current
    if (!el || !space) return
    const fit = () => {
      space.style.height = `${el.offsetHeight}px`
    }
    fit()
    const resize = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(fit)
    resize?.observe(el)
    return () => resize?.disconnect()
  }, [])

  // Escape and the close button give focus back to the button that opened the menu. Following a
  // link does not: the browser moves on to the section it leads to.
  const closeMenu = useCallback((restoreFocus: boolean) => {
    setMenuOpen(false)
    if (restoreFocus) menuButton.current?.focus()
  }, [])

  return (
    // At least the visible height (svh where supported, so phone browser bars do not hide the
    // numbers); taller only when a short landscape screen needs to scroll rather than clip.
    <div id="top" className="flex min-h-screen flex-col supports-[height:100svh]:min-h-svh">
      {/* Holds the fixed header's place, so the hero starts just below it as before. */}
      <div ref={headerSpace} aria-hidden="true" className="shrink-0" />
      <header
        ref={header}
        // Keyboard focus moving into a hidden header (Shift+Tab from the page) brings it back.
        onFocus={reveal}
        className={cn(
          // Tailwind 4's translate utilities set the CSS `translate` property, not `transform`, so
          // that is the property to transition. Above the page; below the phone menu (z-50).
          'fixed inset-x-0 top-0 z-40 bg-surface-card will-change-[translate] motion-safe:transition-[translate,opacity,box-shadow] motion-safe:duration-300 motion-safe:ease-out',
          visible ? 'translate-y-0 opacity-100' : 'pointer-events-none -translate-y-full opacity-0',
          // Once the page is under it, a soft shadow sets it apart from what scrolls beneath.
          !atTop && 'shadow-[0_8px_24px_-14px_rgb(19_48_37/0.35)]',
        )}
      >
        <div className="relative flex justify-center px-5 py-6 sm:py-8">
          <LivingCanopy avoid={brand} />
          <Link ref={brand} to="/" className="relative flex items-center gap-3 rounded-md sm:gap-5">
            <span className="[&>svg]:size-11 sm:[&>svg]:size-[68px]">
              <BrandMark size={68} />
            </span>
            <span className="font-display text-4xl font-semibold tracking-tight text-brand-900 sm:text-5xl">AgriGuard</span>
          </Link>
        </div>

        <div className="border-y border-border-subtle bg-surface-sunken">
          <div className="mx-auto flex max-w-[1440px] items-center justify-between gap-4 px-5 py-2 sm:px-10 md:justify-center md:gap-8 md:py-0">
            <button
              ref={menuButton}
              type="button"
              aria-label={menuOpen ? 'Close menu' : 'Open menu'}
              aria-expanded={menuOpen}
              aria-controls={MENU_ID}
              onClick={() => setMenuOpen((open) => !open)}
              className="-ml-2 flex h-11 items-center gap-3 rounded-md px-2 text-[15px] font-medium text-stone-900 hover:bg-surface-inset md:hidden"
            >
              <MenuBars open={menuOpen} tone="dark" />
              Menu
            </button>
            <nav aria-label="Main" className="hidden md:block">
              <ul className="flex items-center gap-1 lg:gap-4">
                {[{ href: '#top', label: 'Home' }, ...sections].map((s) => (
                  <li key={s.href}>
                    <a
                      href={s.href}
                      aria-current={s.href === '#top' ? 'page' : undefined}
                      className={cn(
                        'relative block px-4 py-4 text-base font-medium lg:text-[17px] text-stone-800 transition-colors hover:text-brand-800',
                        // An underline marks the page you are on, like a tab; others show it on hover.
                        'after:absolute after:inset-x-3 after:bottom-0 after:h-[3px] after:rounded-full after:bg-transparent after:transition-colors hover:after:bg-brand-300',
                        s.href === '#top' && 'text-stone-900 after:bg-brand-700 hover:after:bg-brand-700',
                      )}
                    >
                      {s.label}
                    </a>
                  </li>
                ))}
              </ul>
            </nav>
            {/* The one way in on the page; outside the nav, which only leads to sections. */}
            <Link
              to={enter.to}
              className="press inline-flex h-10 items-center gap-2 rounded-md bg-brand-700 px-4 text-sm font-semibold text-white shadow-raised hover:bg-brand-800"
            >
              <User size={18} />
              {enter.label}
            </Link>
          </div>
        </div>
      </header>

      <section
        aria-labelledby="home-title"
        className="relative isolate flex flex-1 flex-col overflow-hidden bg-brand-900 text-white"
      >
        <HeroBackdrop />

        <div className="relative z-10 flex flex-1 flex-col items-center justify-center px-5 py-16 text-center sm:px-10">
          <div className="scroll-recede mx-auto w-full max-w-5xl">
            <p className="rise glass inline-flex items-center gap-2 rounded-full px-3 py-1 text-sm font-medium text-brand-50">
              <span aria-hidden="true" className="size-1.5 rounded-full bg-brand-300" />
              Crop advisory for smallholder farms
            </p>
            {/*
             * Sentence case in the markup, capitals by CSS: the accessible name (and the test)
             * reads "Crop advice you can trust…", and screen readers do not spell out capitals.
             */}
            <h1
              id="home-title"
              className="font-display rise rise-2 mt-6 text-4xl leading-[1.05] font-semibold tracking-tight text-balance text-white uppercase sm:text-5xl md:text-6xl lg:text-7xl"
            >
              Crop advice <Connector>you</Connector> can
              <LineBreak />
              trust, <Connector>from</Connector> first leaf spot
              <LineBreak />
              <Connector>to</Connector> harvest
            </h1>
            <p className="font-display rise rise-3 mx-auto mt-6 max-w-2xl text-lg leading-relaxed text-pretty text-brand-50 sm:text-xl">
              A farmer reports a problem from the field. AI agents draft a treatment, eleven safety rules check it, and an agronomist approves it before anything is sprayed.
            </p>

            <ul aria-label="AgriGuard in numbers" className="rise rise-4 mx-auto mt-12 grid max-w-3xl grid-cols-3 divide-x divide-white/25">
              <Fact value="11" label="Safety rules on every proposal" />
              <Fact value="4" label="AI agents, each with one job" />
              <Fact value="1" label="Person signs off every prescription" />
            </ul>
          </div>
        </div>
      </section>

      {menuOpen && <MobileMenu enter={enter} onClose={closeMenu} />}
    </div>
  )
}

/** A connecting word in the headline: italic and lower case among the capitals. */
function Connector({ children }: { children: string }) {
  return <span className="italic lowercase">{children}</span>
}

/**
 * A line break where both lines fit (lg and up); on narrower screens the headline wraps on its own,
 * because a forced break there strands a word ("SPOT") on a line by itself.
 */
function LineBreak() {
  return (
    <>
      {' '}
      <span className="hidden lg:inline">
        <br />
      </span>
    </>
  )
}

/** One of the three numbers: a large figure over its meaning, with a rule between neighbours. */
function Fact({ value, label }: { value: string; label: string }) {
  return (
    <li className="px-2 sm:px-8">
      <span className="font-display block text-5xl leading-none font-semibold text-brand-200 sm:text-6xl">{value}</span>{' '}
      <span className="font-display mx-auto mt-3 block max-w-48 text-sm leading-snug text-brand-50 sm:text-lg">{label}</span>
    </li>
  )
}

/**
 * The video, or with reduced motion only its first frame as a still: the video is never even
 * mounted then. Under both, the scrim that keeps the words above 4.5:1 over the brightest frame.
 */
function HeroBackdrop() {
  const reducedMotion = usePrefersReducedMotion()
  const [nearEnd, setNearEnd] = useState(false)
  // On a touch screen the spotlight rests over the plants on the right, clear of the headline.
  const cursor = useSmoothedCursor({ x: 0.74, y: 0.48 })

  return (
    <div aria-hidden="true" className="absolute inset-0">
      {/* A CSS background, never an <img>: the still is decoration and is not announced. */}
      <div className="absolute inset-0 bg-cover bg-center" style={{ backgroundImage: `url(${POSTER})` }} />
      {!reducedMotion && (
        <video
          autoPlay
          muted
          loop
          playsInline
          preload="metadata"
          poster={POSTER}
          aria-hidden="true"
          tabIndex={-1}
          onTimeUpdate={(e) => {
            const video = e.currentTarget
            setNearEnd(video.duration - video.currentTime < LOOP_FADE_SECONDS)
          }}
          className={cn('absolute inset-0 h-full w-full object-cover transition-opacity duration-500 ease-in-out', nearEnd && 'opacity-0')}
        >
          {/* MP4 (H.264) only: every current browser plays it, and no WebM was made. */}
          <source src={VIDEO} type="video/mp4" />
        </video>
      )}
      <DesaturateOutsideSpotlight x={cursor.x} y={cursor.y} />
      {/* A canopy wash, then a vertical gradient deepest under the navigation and the numbers. */}
      <div className="absolute inset-0 bg-brand-900/55" />
      <div className="absolute inset-0 bg-gradient-to-b from-brand-900/80 via-brand-900/40 to-brand-900/85" />
    </div>
  )
}

/**
 * The cursor spotlight over the video: everything is drained to grey except a soft circle around
 * the cursor, where the field keeps its colour. One pane with `backdrop-filter: grayscale` and a
 * radial mask with a hole at the cursor (the same stops as the sign-in spotlight, inverted), so
 * the video is decoded once. The scrim above it is unchanged, so the words keep their contrast.
 */
/** Outside the spotlight: no colour and less light, so the circle of colour stands out. */
const DRAINED = 'grayscale(1) brightness(0.62) contrast(1.05)'

function DesaturateOutsideSpotlight({ x, y }: { x: number; y: number }) {
  const pane = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const el = pane.current
    if (!el) return
    const rect = el.getBoundingClientRect()
    const at = `${Math.round(x - rect.left)}px ${Math.round(y - rect.top)}px`
    const mask = `radial-gradient(circle ${SPOTLIGHT_R}px at ${at}, transparent 0%, transparent 40%, rgb(0 0 0 / 0.25) 60%, rgb(0 0 0 / 0.6) 75%, rgb(0 0 0 / 0.88) 88%, black 100%)`
    el.style.maskImage = mask
    el.style.webkitMaskImage = mask
  }, [x, y])

  return (
    <div
      ref={pane}
      className="absolute inset-0"
      style={{ backdropFilter: DRAINED, WebkitBackdropFilter: DRAINED }}
    />
  )
}

/**
 * Three bars that turn into a cross: the outer two rotate and meet, the middle one fades. With
 * reduced motion the states swap instantly.
 */
function MenuBars({ open, tone = 'light' }: { open: boolean; tone?: 'light' | 'dark' }) {
  const bar = cn(
    'absolute left-0 h-0.5 rounded-full motion-safe:transition-[transform,opacity] motion-safe:duration-500',
    tone === 'light' ? 'bg-white' : 'bg-stone-900',
    MENU_EASE,
  )
  return (
    <span aria-hidden="true" className="relative block h-[18px] w-6">
      <span className={cn(bar, 'top-0 w-6', open && 'translate-y-2 rotate-45')} />
      <span className={cn(bar, 'top-2 w-4', open && 'opacity-0')} />
      <span className={cn(bar, 'top-4 w-6', open && '-translate-y-2 -rotate-45')} />
    </span>
  )
}

/**
 * The phone menu: a full-screen canopy panel with the sections in large type and the way in at the
 * bottom. Mounted only while open (a hidden copy would put a second "Sign in" in the page). While
 * it is open the page behind does not scroll, Tab stays inside it, and Escape closes it.
 */
function MobileMenu({ enter, onClose }: { enter: EnterLink; onClose: (restoreFocus: boolean) => void }) {
  const panel = useRef<HTMLDivElement>(null)
  const closeButton = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    closeButton.current?.focus()
    document.body.classList.add('overflow-hidden')

    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault()
        onClose(true)
        return
      }
      if (e.key !== 'Tab' || !panel.current) return
      const focusable = [...panel.current.querySelectorAll<HTMLElement>('a[href], button:not([disabled])')]
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      const inside = panel.current.contains(document.activeElement)
      if (e.shiftKey && (!inside || document.activeElement === first)) {
        e.preventDefault()
        last?.focus()
      } else if (!e.shiftKey && (!inside || document.activeElement === last)) {
        e.preventDefault()
        first?.focus()
      }
    }
    document.addEventListener('keydown', onKeyDown)

    // The menu exists only below the md breakpoint: widening the window closes it.
    const wide = window.matchMedia?.('(min-width: 48rem)')
    const onWide = (e: MediaQueryListEvent) => {
      if (e.matches) onClose(false)
    }
    wide?.addEventListener('change', onWide)

    return () => {
      document.removeEventListener('keydown', onKeyDown)
      wide?.removeEventListener('change', onWide)
      document.body.classList.remove('overflow-hidden')
    }
  }, [onClose])

  return (
    <div
      ref={panel}
      id={MENU_ID}
      role="dialog"
      aria-modal="true"
      aria-label="Menu"
      className="menu-fade fixed inset-0 z-50 flex flex-col overflow-y-auto bg-brand-900/95 backdrop-blur-xl md:hidden [&_:focus-visible]:outline-brand-200"
    >
      {/* The name, and the bars turned into a cross to close. */}
      <div className="flex items-center justify-between px-5 py-5 sm:px-10">
        <span className="flex items-center gap-2.5">
          <BrandMark size={36} />
          <span className="font-display text-2xl font-semibold text-white">AgriGuard</span>
        </span>
        <button
          ref={closeButton}
          type="button"
          aria-label="Close menu"
          aria-expanded
          aria-controls={MENU_ID}
          onClick={() => onClose(true)}
          className="grid size-11 place-items-center rounded-md hover:bg-white/10"
        >
          <MenuBars open />
        </button>
      </div>

      <nav aria-label="Menu" className="flex-1 px-5 pt-6 sm:px-10">
        <ul>
          {sections.map((s, i) => (
            <li key={s.href} className="menu-item border-b border-white/10" style={{ animationDelay: `${150 + i * 80}ms` }}>
              <a
                href={s.href}
                onClick={() => onClose(false)}
                className={cn(
                  'font-display block py-4 text-4xl text-white hover:text-brand-200 sm:text-5xl motion-safe:transition-all motion-safe:duration-300 motion-safe:hover:pl-4',
                  MENU_EASE,
                )}
              >
                {s.label}
              </a>
            </li>
          ))}
        </ul>
      </nav>

      <div className="menu-fade px-5 pt-8 pb-8 sm:px-10" style={{ animationDelay: '550ms' }}>
        <Link
          to={enter.to}
          onClick={() => onClose(false)}
          className="press flex w-full items-center justify-center gap-2 rounded-md bg-white py-4 text-base font-semibold text-brand-800 shadow-lifted hover:bg-brand-50"
        >
          {enter.label}
          <ArrowRight size={20} />
        </Link>
      </div>
    </div>
  )
}
