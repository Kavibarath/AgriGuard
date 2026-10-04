# AgriGuard — Cinematic Hero Prompt

Paste everything below the line into a fresh session.

---

Rebuild the **hero section** of the AgriGuard public landing page (`web/src/features/home/HomePage.tsx`, route `/`) as a fullscreen cinematic section with a looping video background and an animated mobile menu. Everything below the hero (the "How it works" steps, "Who it is for" roles, footer) stays as it is.

This is an existing, tested page in a working app — **not a greenfield component**. Read `web/src/features/home/HomePage.tsx`, `web/src/features/home/HomePage.test.tsx`, `web/src/index.css` and `docs/design/TOKENS.md` before you write anything.

## Stack facts you must not get wrong

- **Tailwind v4, CSS-first.** There is **no `tailwind.config.js`** and no `@tailwind base/components/utilities`. Tokens live in `@theme static { … }` in `web/src/index.css`, which is imported with `@import "tailwindcss"`. Do not create a config file. Do not add `@tailwind` directives.
- **Fonts are already loaded** in `web/index.html`: **Inter** (sans) and **Source Serif 4** (display, via `--font-display` and the `.font-display` class). **Do not add Instrument Serif or any new family.**
- **One change to the font link is required.** The current Source Serif 4 URL has no italic axis, so italic text renders as a faux-slanted fake. Replace the `family=Source+Serif+4:…` part with the italic-capable axis list:
  ```
  family=Source+Serif+4:ital,opsz,wght@0,8..60,500;0,8..60,600;0,8..60,700;1,8..60,500;1,8..60,600
  ```
- **Do not install `lucide-react`.** The project has its own stroke icon set at `web/src/components/icons/index.tsx`. Add `ArrowRight`, `Play` and `Menu`/`Close` there in the existing style (1.5px stroke, `size` prop, `currentColor`) if they are missing.
- **Use existing tokens**, never raw hex: `brand-50…900`, `earth-*`, `surface-*`, `--shadow-raised/lifted/overlay`, and the existing `rise`, `rise-2…4` and `press` animation classes.
- Icons are inline SVG. No icon fonts, no emoji anywhere.

## Background video

**Do not hotlink any third-party CDN URL.** Self-host the asset so the graded demo cannot break on someone else's bandwidth.

- Place the file at `web/public/video/hero-field.mp4` (H.264/AAC, 1920×1080, **≤5 MB**, 8–15 s seamless loop) plus `web/public/video/hero-field.webm` if you can produce one. A poster frame goes at `web/public/img/hero-field-poster.webp`.
- **Subject:** smallholder tropical/South-Asian agriculture — rows of tomato, chilli, paddy or cabbage; a slow drift or dolly over green plots; hands inspecting leaves. No drone-over-wheat clichés, no industrial monoculture, no "AI technology" stock.
- **Source** from Coverr or Pexels (free commercial licence) and record the URL and licence in `docs/design/IMAGE-CREDITS.md`. If you cannot obtain a suitable clip, implement the poster image path and leave a clearly commented `TODO` naming the required file — do not substitute an unlicensed URL.
- Markup: `<video>` with `autoPlay muted loop playsInline preload="metadata"`, `poster` set, `aria-hidden="true"`, `tabIndex={-1}`, `className="absolute inset-0 h-full w-full object-cover"`.
- **`prefers-reduced-motion: reduce` must not play video.** Detect it with `useSyncExternalStore` over a `matchMedia('(prefers-reduced-motion: reduce)')` query and render the poster as a CSS `background-image` div instead of mounting the `<video>` at all.
- **Legibility scrim is mandatory.** Over the video, stack: a brand-tinted duotone wash (`bg-brand-900/55`) plus a vertical gradient `bg-gradient-to-b from-brand-900/80 via-brand-900/40 to-brand-900/85`. Text over video must hold 4.5:1 — verify it, and deepen the scrim rather than lightening the text.

## Layout

The section is `relative h-screen w-full overflow-hidden`, with the video layer absolutely positioned behind a `relative z-10 flex h-full flex-col` content column. Use `h-[100svh]` with an `h-screen` fallback so mobile browser chrome does not clip the content.

## Navbar

Padding `px-5 py-5 sm:px-10 md:py-6`, `flex items-center justify-between`.

- **Left:** the existing `<BrandMark />` wrapped in `<Link to="/">` whose accessible name is exactly **`AgriGuard`** (a test asserts this link and its `href="/"`), then desktop anchors, `hidden md:flex`, `gap-1`: **How it works** (`#how-it-works`), **Who it is for** (`#who-it-is-for`), **Safety** (`#safety`). Style `rounded-md px-3 py-2 text-sm font-medium text-brand-50 hover:bg-white/10 transition-colors`.
  **Only link to anchors that exist.** `#safety` has no section today — either add a short "Eleven rules" section with `id="safety"` or drop that link. A nav item that scrolls nowhere is worse than three items.
- **Right:** the primary CTA (`hidden md:inline-flex`) — white background, `text-brand-800`, `rounded-md px-4 h-10 text-sm font-semibold shadow-raised hover:bg-brand-50 press` — plus the hamburger button (`md:hidden`).
- **The CTA label and target are conditional and already implemented**: signed out → `Sign in` → `/login`; signed in → `Open your dashboard` → `/dashboard`, via `useIsAuthenticated()`. Keep that logic exactly. Tests assert both states.

**Hamburger:** three white `rounded-full` bars, 2px tall, outer bars `w-6`, middle `w-4`. Open state rotates the top/bottom bars +45°/−45° and translates them to meet, and fades the middle out. `cubic-bezier(0.76, 0, 0.24, 1)`, 500ms. Needs `aria-label` ("Open menu"/"Close menu"), `aria-expanded` and `aria-controls` pointing at the overlay id. Under reduced motion, swap states with no transition.

## Mobile menu overlay (`fixed inset-0 z-50 md:hidden`)

- **Render it only when open** — do not keep a hidden copy mounted. A second "Sign in" link sitting in the DOM breaks the signed-in test that asserts no sign-in link exists.
- Backdrop `bg-brand-900/95 backdrop-blur-xl`, fading in over 700ms with the same cubic-bezier. Brand green, not black.
- Header row mirrors the navbar: BrandMark plus a close button (the hamburger in its X state).
- Links stacked and left-aligned: `font-display text-4xl sm:text-5xl`, white, each `border-b border-white/10 py-4`, `hover:pl-4 transition-all`. Items: **How it works**, **Who it is for**, **Safety**. Each animates from `translate-y-8 opacity-0` to `translate-y-0 opacity-100` with delay `150 + index * 80`ms.
- Footer: the same conditional CTA as the navbar, full width, white background, `text-brand-800`, `rounded-md py-4`, fading in at 550ms.
- **Required behaviour:** close on `Escape`, close on navigation, trap focus inside the overlay while open, restore focus to the hamburger on close, and lock body scroll (`overflow-hidden` on `document.body`, restored on unmount).

## Hero content

Container `flex-1 flex flex-col justify-center px-5 sm:px-10`, inner `mx-auto w-full max-w-[1440px]`, text block `max-w-4xl`. Left-aligned, not centred — this is a product, not a poster.

- **Eyebrow:** keep the existing pill — `rounded-full border border-white/25 bg-white/10 px-3 py-1 text-sm font-medium text-brand-50` with the small `brand-300` dot — reading **Crop advisory for smallholder farms**. Apply `rise`.
- **Heading (`h1`, `id="home-title"`)**: `font-display rise rise-2 text-white text-4xl sm:text-5xl md:text-6xl lg:text-7xl leading-[1.05] font-semibold uppercase tracking-tight`, with the connector words in `italic lowercase` spans:

  ```
  CROP ADVICE you CAN
  TRUST, from FIRST LEAF SPOT
  to HARVEST
  ```
  Italic spans: `you`, `from`, `to`.

  **Write the text in normal sentence case in the JSX and uppercase it with the CSS `uppercase` class.** The test matches the accessible name against `/Crop advice you can trust/`, which is case-sensitive — typing literal capitals breaks it. Line breaks via `<br />` inside `hidden sm:inline` wrappers so the phrasing reflows cleanly on small screens.
- **Subtext (`p`)**: `rise rise-3 mt-5 max-w-xl text-base sm:text-lg text-brand-50 font-light leading-relaxed` — keep the existing sentence: "A farmer reports a problem from the field. AI agents draft a treatment, eleven safety rules check it, and an agronomist approves it before anything is sprayed."
- **Buttons row** `rise rise-4 mt-8 flex flex-col sm:flex-row items-stretch sm:items-center gap-3`:
  - **Primary** — the conditional CTA, `press inline-flex h-12 items-center gap-2 rounded-md bg-white px-6 text-base font-semibold text-brand-800 shadow-lifted hover:bg-brand-50`, with `<ArrowRight size={20} />` that translates 2px right on hover.
  - **Secondary** — `See how it works`, anchor to `#how-it-works`, `inline-flex h-12 items-center gap-2 rounded-md border border-white/40 px-5 text-base font-medium text-white hover:bg-white/10 hover:border-white/60`. Use the `Play` icon **only if** it opens a real video; for an in-page anchor use a downward chevron instead.
- **Facts strip at the bottom of the viewport** — keep the existing `<ul aria-label="AgriGuard in numbers">` with **11** safety rules, **4** AI agents, **1** person signing off. Values in `font-display`. This is what stops the hero reading as an empty agency splash, so keep it in the fold.
- **Remove** the "Move the pointer across the field to see it in colour" hint and the cursor-reveal spotlight from the hero — the video replaces it. If `components/spotlight/` ends up unused anywhere else, delete it and its tests rather than leaving dead code.

## Motion rules

Every transition in this section must be disabled under `prefers-reduced-motion: reduce`: no video, no parallax, no staggered menu entrance, no hover translate. Keep instant state changes only.

## Acceptance

Run and report all of these:

```
npm --prefix web run test
npm --prefix web run typecheck
npm --prefix web run lint
npm --prefix web run build
```

- **`HomePage.test.tsx` must pass unchanged.** It asserts: the `h1` accessible name matches `/Crop advice you can trust/`; a link named exactly `AgriGuard` with `href="/"`; headings `Eleven rules check it` and `Field agronomists`; that clicking the first `/^Sign in/` link navigates to `/login`; that a signed-in user sees no sign-in link and `Open your dashboard` links to `/dashboard`; and that **no element has role `img`** — so the video stays `aria-hidden`, and the poster is a CSS background, never an `<img>`.
- If you genuinely must change a test, change it in the same commit and say exactly what changed and why.
- Report the final video file size, and confirm the text-over-video contrast ratio you measured.
- Show before/after screenshots at 1440px, 768px and 390px, plus the mobile menu open.
