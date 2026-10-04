# AgriGuard — UI/UX Restyle Prompt

Paste everything below the line into a fresh session. It is written as instructions to a build agent.

---

## Your role

You are redesigning the visual layer of **AgriGuard AI**, a crop-advisory and produce supply-chain platform for smallholder farmers. Both applications are already built and working. **This is a restyle, not a rebuild.** Do not invent new screens, rename routes, or change data flow. Make what exists look and feel premium.

Read `docs/PROJECT-PLAN.md` first for the domain, then work through the repo.

## What already exists

**React web app** (`web/`) — Vite + React 19 + TypeScript, Tailwind **v4** (CSS-first `@theme` in `web/src/index.css`; there is no `tailwind.config.js`), TanStack Query, Zustand, React Hook Form + Zod, React Router. Tests are Vitest + Testing Library + MSW and they currently pass.

- Shared UI: `web/src/components/ui/` — `button`, `field`, `select`, `textarea`, `alert`, `Modal`, `DataTable`, `StatTile`, `StatusBadge`, `status-tones.ts`, `EmptyState`, `Spinner`, `AsyncBoundary`
- Charts: `web/src/components/charts/` (`BarChart`, `HBarList`), map: `components/map/TileMap`
- Features: `auth`, `dashboard`, `cases`, `agent-runs`, `registry`, `inventory`, `rules`, `harvest`, `intelligence`
- Routes: `/login`, `/dashboard`, `/intelligence`, `/farms`, `/farms/:id`, `/plots/:id`, `/cases`, `/cases/:id`, `/agent-runs`, `/agent-runs/:id`, `/harvest`, `/collection-planner`, `/inventory`, `/orders`, `/rules`

**Flutter app** (`mobile/`) — Material 3, `ColorScheme.fromSeed(Color(0xFF2F855A))` in `mobile/lib/app/app.dart`, go_router, Riverpod.

- Screens: splash, login, home, cases (list / new / detail), farms, plot detail, new plot, harvest, orders
- Device features in use: camera photo capture, GPS plot tagging, offline case outbox

**Four roles, and they see different things:** Farmer (mobile), Field Agronomist (web approval console + mobile triage), Agro-Dealer (web inventory), Co-op Administrator (web rules and analytics).

## Hard constraints — breaking any of these fails the task

1. **All existing tests must still pass.** Run `npm --prefix web run test`, `npm --prefix web run typecheck`, `npm --prefix web run lint`, `npm --prefix web run build`, and `cd mobile && flutter analyze && flutter test`. Tests query by accessible role, label and visible text — if you change a button's text or an `aria-label`, update the test in the same commit and say so.
2. **No API, DTO, route, or query-key changes.** Presentation only.
3. **Keep every loading, empty, success and error state.** Restyle them; never delete one. `AsyncBoundary`, `EmptyState` and `Spinner` stay in use.
4. **Keep role-based gating exactly as it is.** A disabled approve control must stay disabled and keep explaining why.
5. **Status must never be communicated by colour alone.** Every status badge and validation verdict needs an icon or text label too. This is a safety product: a colour-blind agronomist must be able to tell "rejected" from "approved".
6. **Do not touch the chart series tokens** (`--color-series-*`, `--color-diverge-*`) in `index.css`. They were chosen as a colour-blind-separable set. You may restyle axes, gridlines, legends, tooltips and containers.
7. **No new heavyweight dependencies.** Tailwind + CSS + small SVG is the toolkit. No UI kit, no animation library, no 3D library. If you believe one is genuinely required, ask first.

## Design direction

**Agronomic institute meets modern SaaS.** Credible, calm, and precise — this software tells someone how much pesticide to put on food. It should feel like an instrument, not a marketing site. Warm and organic, never clinical; premium, never flashy.

The two reference images provided show the intended *visual density and card system* — compact cards, real photography, generous data per screen. Borrow the craft, not the content: AgriGuard's web app is a back-office console, not a farmer dashboard.

### Colour palette

Extend `@theme` in `web/src/index.css`. **Keep `--color-brand-500: #2f855a` as the primary** — it is already the Flutter seed colour and both apps must match.

```css
/* Canopy — primary. Brand, navigation, primary actions, healthy states. */
--color-brand-50:  #f2f8f3;
--color-brand-100: #dcf1de;
--color-brand-200: #bce3c2;
--color-brand-300: #8fcca0;
--color-brand-400: #58a97c;
--color-brand-500: #2f855a;   /* keep — shared with Flutter */
--color-brand-600: #276749;
--color-brand-700: #22543d;
--color-brand-800: #1a4230;
--color-brand-900: #133025;

/* Earth — secondary. Soil, harvest, inventory, dealer surfaces, section dividers. */
--color-earth-50:  #faf6f0;
--color-earth-100: #f0e7da;
--color-earth-200: #e0cdb4;
--color-earth-300: #c9ac86;
--color-earth-400: #a9835a;
--color-earth-500: #8b6340;
--color-earth-600: #6f4e33;
--color-earth-700: #563d28;
--color-earth-800: #3e2c1d;

/* Surfaces — warm white, never pure #fff on the page background. */
--color-surface-page:   #fdfcf9;
--color-surface-card:   #ffffff;
--color-surface-sunken: #f6f4ee;
--color-surface-inset:  #efece3;
--color-border-subtle:  #e7e3d8;
--color-border-strong:  #d5cfc0;

/* Semantic — status and validation verdicts. */
--color-success: #276749;
--color-warning: #b45309;   /* PHI windows, spray-weather cautions, expiring stock */
--color-danger:  #b42318;   /* hard rejects: V5 pre-harvest interval, V2 unapproved product */
--color-info:    #2a78d6;
```

**Ratio discipline:** roughly 60% warm neutral surfaces, 30% green, 10% earth-brown. Brown is an accent for soil/harvest/inventory contexts and dividers — it is not a second primary. Never place brown text on green or green text on brown.

### Typography

- **UI, data, tables, forms:** Inter (already loaded). Tabular numerals on every number: `font-variant-numeric: tabular-nums` — dose figures and stock counts must align in columns.
- **Page titles and section headings:** add one serif display face — **Fraunces** or **Source Serif 4** — via Google Fonts, with a real fallback stack. Use it for `h1`/`h2` and stat-tile values only. This single move carries most of the "premium" feel.
- Scale: 30/24/20/16/14/12px. Body 14px in dense console views, 16px on mobile. Line height 1.5 body, 1.2 headings.
- Never set body text below 12px. Never use all-caps for anything longer than a 3-word label.

### Density — the most important rule here

The brief is explicitly "clean but not too much white space". Modern SaaS defaults are far too airy for this.

- Content max-width **1440px**; the app shell is a fixed 240px sidebar plus fluid content.
- **Dashboard and index pages must fill a 12-column grid.** Stat tiles 4-up on desktop, main panels in 2- or 3-column arrangements. A full-width single-column page is a failure.
- Card padding **16–20px**, not 32. Section gap **24px**. Grid gutter **16px**.
- Table rows **44px** tall, 12px horizontal cell padding, sticky header, zebra striping with `--color-surface-sunken`.
- Page header: title, one-line context sentence, and actions on a single band **96px** tall — no giant hero space on internal pages.
- Every panel should earn its area. If a card holds one number, it belongs in a stat tile row, not a card of its own.

### Depth and motion

Use these deliberately and sparingly. The mapping below is the agreed treatment:

| Effect | Where | Treatment |
|---|---|---|
| **Raised cards** | Stat tiles, crop health, weather, validation verdict | Resting `0 1px 2px rgb(19 48 37 / .06), 0 1px 3px rgb(19 48 37 / .04)`; hover lifts to a 4px/12px shadow with `translateY(-2px)` over 160ms |
| **Layered map** | `TileMap`, collection planner, outbreak intelligence | Plot polygons at distinct elevations; selected plot lifts with a brand-600 ring and stronger shadow |
| **3D crop illustration** | Login page only | A single layered SVG crop/field scene with 2–3 parallax layers tracking the cursor at ≤8px offset. Never on internal pages |
| **Pressed buttons** | Primary actions ("Approve", "Request AI advice") | `translateY(1px)` plus shadow reduction on `:active`, 80ms |
| **Floating status icons** | Weather panel, spray-window widget | Leaf / water-drop / sun SVGs drifting 3–6px on a 6–8s ease-in-out loop |

Shadows must be tinted with the deep green (`rgb(19 48 37 / …)`), never neutral black — black shadows on warm surfaces look muddy.

**`prefers-reduced-motion: reduce` must disable all of it:** no parallax, no float, no hover translate. Keep only instant state changes. This is non-negotiable and is a marking-relevant accessibility point.

### Imagery

Photography is what will lift this from "student project" to "product". Rules:

- **Store assets locally** in `web/public/img/` — no hotlinking. Convert to **WebP**, target ≤200 KB each, and set explicit `width`/`height` to avoid layout shift.
- **Source:** Unsplash or Pexels (free commercial licence, no attribution required). **Record every image's source URL and licence in `docs/design/IMAGE-CREDITS.md`** — you need this for the report's references section.
- **Subject matter:** smallholder tropical/South-Asian agriculture — tomato, chilli, paddy, cabbage plots; hands inspecting leaves; terraced hill farms; produce collection. Match the seeded crops. Avoid North-American industrial monoculture, drone-over-wheat clichés, and generic "AI/technology" stock.
- **Do not present AI-generated or stock people as real AgriGuard users**, and never imply a photographed person endorses the system.
- **Treatment:** a subtle brand-900 duotone or a `linear-gradient` scrim at 40–60% opacity wherever text sits over a photo. Corner radius 12px. Photos are context, never decoration competing with data.
- **Where to use them:**
  - Login: split layout, 45% photographic panel with the product name and one line of positioning
  - Dashboard: a slim 160px context banner, not a hero
  - Crop and pathogen reference cards: small 64px thumbnails
  - Empty states: a simple line illustration in brand-300, not a photo
  - Flutter: only the login screen and the home header strip — photos cost bytes on field connections

### Iconography

One consistent set of **stroke** icons at 1.5px weight, inline SVG (no icon font, no emoji anywhere in the product UI). 20px in navigation and buttons, 16px inline with text. Crop-health, weather and validation icons should feel like one family.

## Screen-by-screen

### React

- **`/login`** — Split screen: photographic panel left, form right. The one place with the 3D crop illustration and a marketing-grade first impression. Show the four demo role logins as compact, clearly-labelled test accounts.
- **App shell** — Fixed sidebar, deep brand-800 ground, grouped navigation that reflects the user's role, the AgriGuard mark at top, user chip with role badge at bottom. Top bar carries breadcrumb, global search and a notification bell. If no layout component exists yet, create one — this is the single highest-impact change.
- **`/dashboard`** — Role-aware. Stat tile row 4-up, then a 2/3 + 1/3 split: primary work queue beside weather and spray-window. Fill the grid.
- **`/agent-runs` and `/agent-runs/:id`** — **The marquee screen; spend the most effort here.** The detail page needs four clearly separated regions: the plan tree with per-step agent role and status; the chronological tool-call timeline with durations and retries; the **validation verdict card listing all 11 rules** with pass/fail/severity — treat this as a safety report, with hard rejects (V5 pre-harvest interval, V2 unapproved product) visually unmistakable; and the decision panel (Approve / Reject / Request revision) styled as the consequential action it is, with a mandatory reason field on reject and revise. Give each of the four agents a consistent colour and icon identity used everywhere they appear.
- **`/cases`, `/cases/:id`** — Dense triage queue with filters; detail shows the leaf photo prominently, the GPS pin on the map, symptom chips, and a vertical status timeline.
- **`/farms`, `/plots/:id`** — Registry with a crop-cycle stage timeline and the plot safety profile: make pre-harvest-interval-blocked dates read as a clear calendar warning.
- **`/inventory`, `/orders`** — Earth-toned dealer surfaces. Expiry proximity and low stock must be scannable at a glance; batch tables with tabular numerals.
- **`/rules`** — The regulatory rules editor. This gets demonstrated live at the viva when a rule is changed and the agent re-run, so it must look authoritative: grouped fields, units on every input, inline explanation of what each limit does.
- **`/harvest`, `/collection-planner`** — Slot capacity as visual fill bars; overbooking impossible and visibly so.
- **`/intelligence`** — Outbreak map with a green-to-amber-to-red sequential ramp, beside a ranked pathogen list.

### Flutter

Rebuild the theme in `mobile/lib/app/app.dart` from the same palette — a full `ColorScheme` (keep `#2F855A` as primary), `CardTheme`, `InputDecorationTheme`, `FilledButtonTheme`, `AppBarTheme`, `NavigationBarTheme`, plus text themes matching the web scale. Then apply it across the screens.

Field conditions drive every decision here:

- **Outdoor sunlight readability:** stronger contrast than the web app, minimum 16px body text.
- **Touch targets ≥48dp**, primary actions in the lower third for one-handed use, generous spacing between destructive and routine actions.
- **New case flow** is the showcase: a clear stepper (photo → location → symptoms → notes → review), a large camera capture target, live GPS accuracy feedback, and symptom selection as tappable chips grouped by plant part.
- **Offline outbox must be visible and reassuring** — a persistent banner showing queued reports and retry state. Farmers on poor connections need to trust it.
- **Case status timeline** must clearly show the pending-approval wait, so the farmer understands a human is reviewing.
- **Prescription detail** is the safety-critical screen: dose, total quantity, spray date, and the earliest safe harvest date as an unmissable warning block.

## Accessibility

- WCAG AA: 4.5:1 for body text, 3:1 for large text and UI boundaries. **Verify brand-500 on white and white on brand-500** — adjust shade, never the requirement.
- Visible focus rings on every interactive element: 2px brand-600 with a 2px offset. Never remove outlines.
- Icon-only buttons need `aria-label`. Modals trap focus and restore it on close. Tables use proper `<th scope>`.
- Dark mode is **out of scope** unless everything else is finished — a half-done dark mode looks worse than none.

## Do not

- Emoji as UI icons; purple/indigo SaaS gradients; glassmorphism; neon or "AI-style" glows
- Full-bleed hero images on internal pages; decorative photos that push data below the fold
- Animation on data updates that makes numbers hard to read
- More than two font families; centred body text; text over busy photography without a scrim
- Removing information to make a page look cleaner — density is a requirement, not a compromise

## How to work

Do this in reviewable stages, and **stop for approval after stage 2** so the direction can be corrected cheaply:

1. **Tokens** — palette, typography, shadows, spacing in `web/src/index.css`; the Flutter theme file. Plus a `/design-system` route or a short `docs/design/TOKENS.md` showing swatches, type scale and components in every state.
2. **App shell and one vertical slice** — sidebar, top bar, page header, then `/agent-runs/:id` end to end as the proof of the direction.
3. **Remaining web pages**, feature by feature.
4. **Flutter theme and screens.**
5. **Imagery, depth and motion passes**, then the reduced-motion audit.

After every stage run the full check suite and report what passed. Show before/after screenshots of each restyled page.

## Done when

- Every check passes: web tests, typecheck, lint, build; `flutter analyze` and `flutter test`
- No API, route or behaviour changes; role gating and all four async states intact
- Contrast verified; reduced-motion honoured; no status conveyed by colour alone
- `docs/design/IMAGE-CREDITS.md` lists every image with source and licence
- Web and mobile are visibly the same product
