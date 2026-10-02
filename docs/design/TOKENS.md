# AgriGuard design tokens

One system for the web console (`web/src/index.css`) and the phone app
(`mobile/lib/app/theme.dart`). The live reference, with every component in every state, is the
dev-only route `/design-system` (run `npm --prefix web run dev`; it is left out of production builds).

The direction is "agronomic institute meets modern SaaS": calm, precise and warm, because the
software tells someone how much pesticide to put on food. One element is allowed to be striking:
the safety report on the agent-run page. Everything around it stays quiet.

## Colour

| Role | Token | Hex | Use |
|---|---|---|---|
| Canopy (primary) | `brand-500` | `#2f855a` | The Flutter seed and brand fills. Text on white only at large sizes (4.5:1). |
| | `brand-600` | `#276749` | Primary buttons, links, focus ring (6.7:1 with white). |
| | `brand-800` | `#1a4230` | Sidebar ground. |
| | `brand-50…900` | | Tints for selected rows, the decision panel, advice. |
| Earth (secondary) | `earth-500` | `#8b6340` | Soil, harvest, inventory, symptom chips, dividers. About 10% of any screen. |
| Surfaces | `surface-page` | `#fdfcf9` | The page. Never pure white. |
| | `surface-card` | `#ffffff` | Cards and tables. |
| | `surface-sunken` | `#f6f4ee` | Zebra rows, table headers, inset blocks. |
| | `border-subtle` / `border-strong` | `#e7e3d8` / `#d5cfc0` | Card hairlines / field borders. |
| Success | `success` | `#276749` | Passed, done, approved. |
| Warning | `warning` | `#b45309` | PHI windows, spray-weather cautions, expiring stock, "not checked". |
| Danger | `danger` | `#b42318` | Hard stops (V2 unapproved product, V5 pre-harvest interval), errors. |
| Info | `info` | `#2a78d6` | In progress. Icons and borders only; text uses `info-800` `#1a4a82`. |

Each semantic colour has `-50` (fill), `-200` (ring) and `-800` (text on the fill).

These rules hold throughout:

- Brown text never sits on green, and green text never sits on brown.
- The chart series tokens (`--color-series-*`, `--color-diverge-*`) are unchanged. They were
  chosen as a colour-blind-separable set.

**Contrast, checked:**

| Pair | Ratio |
|---|---|
| White on brand-500 | 4.54:1 (passes only just, so primary buttons use brand-600, 6.7:1) |
| brand-500 on the page | 4.4:1 (fails for body text, so links and text use brand-600/700) |
| warning on white | 5.0:1 |
| danger on white | 6.6:1 |
| info on white | 4.4:1 (so info text is info-800, 8.9:1) |
| stone-600 on surface-sunken | 6.9:1 (the lightest text used on tinted rows) |

## The four agents

Each agent keeps one hue and one glyph wherever it appears, and its name is always written beside
the glyph. The source is `web/src/features/agent-runs/agents.ts`.

| Agent | Glyph | Ink |
|---|---|---|
| Coordinator | compass | `#22543d` |
| Diagnosis | lens over a leaf | `#1a5c9e` |
| Action | sprayer | `#8b6340` |
| Validation | shield with a tick | `#475569` |

## Type

- **Inter** is used for the interface, data, tables and forms. Tabular figures are on everywhere
  (`font-variant-numeric: tabular-nums` on `html`), so doses and stock counts line up.
- **Source Serif 4** (`.font-display`) is used only for page titles, panel headings and headline
  figures (stat tiles, the do-not-harvest date).

| Size | Role |
|---|---|
| 30 | Page title |
| 24 | Section |
| 20 | Panel heading |
| 16 | Phone body |
| 14 | Console body |
| 12 | Caption (the smallest used) |

Line height is 1.5 for body text and 1.2 for headings. Labels are sentence case, never all caps.

On the phone, the body text is 16 (bodyMedium) to 17, and captions are 14. Titles use the system
serif (Noto Serif on Android), so no font file is downloaded in the field.

## Space and density

- The shell is a fixed 240px sidebar with a fluid content column up to 1440px.
- The top bar is 56px, and the page header band is about 96px.
- Cards have 16–20px padding, 16px gutters and 24px between sections.
- Table rows are 44px, with 12px horizontal cell padding, a sticky header and zebra rows in
  `surface-sunken`.
- Radius steps down with the element: 12 for panels and photos, 8 for inner blocks, 6 for
  controls, and fully round for status chips.

## Depth and motion

Shadows are tinted with the deep canopy green, never neutral black:

| Token | Use |
|---|---|
| `shadow-raised` | At rest: stat tiles, the safety report, the decision panel. |
| `shadow-lifted` | Hover on interactive raised cards (`.card-raised.is-interactive`, which also lifts 2px). |
| `shadow-overlay` | Modals and the mobile drawer. |

Pressed primary buttons (`.press`) move down 1px and lose their shadow over 80ms. The weather
glyphs drift 3–6px on a 7–8s loop (`.drift`, `.drift-slow`).

Map pins (`TileMap`) are stacked by weight, so where they crowd together the most serious one is
on top: severe, then danger, warning, active, success and neutral. Each has a canopy-tinted drop
shadow, and the selected pin is lifted above all of them with a ring.

### The home page hero

The public home page (`/`) opens on a full-screen video of smallholder plots (`HomeHero.tsx`),
self-hosted in `web/public/video/`:

- It is muted, loops and plays inline. It is `aria-hidden`, takes no focus, and has a poster: its
  own first frame.
- The clip is a pan, so the page fades it onto the poster just before it ends, and back in as it
  restarts. The loop dissolves instead of cutting.
- The scrim keeps every word above 4.5:1 (measured: 6.1:1 or better).
- The headline is set in capitals by CSS, with the connecting words in true Source Serif italics.
  The font link loads the italic axis, so the italics are not slanted fakes.
- On phones the navigation is a menu. The bars turn into a cross (500ms,
  `cubic-bezier(0.76, 0, 0.24, 1)`), and a canopy panel fades in over 700ms with each link rising
  in on its own delay (`.menu-fade`, `.menu-item`).
- The menu is mounted only while open. Escape and the close button shut it and give focus back to
  the menu button, Tab stays inside it, and the page behind does not scroll.
- On this dark ground the focus ring is `brand-200`, as in the sidebar.

### Glass, reveal and the field motifs

- **Liquid glass** (`.glass` on dark grounds, `.glass-light` on the page; `GlassCard` on the
  phone): a blurred, saturated pane with a sheen from the top-left, a bright top edge and a canopy
  shadow. Used on the hero's numbers and secondary button, the "How it works" steps, the safety
  rules, the sign-in form and the phone's home tiles.
- **Buttons** are lit from above: primary is a canopy gradient (brand-600 to brand-700, white
  6.7:1 or better) with a hairline of light on the top edge; on the phone a translucent sheen
  over the fill keeps the tap ripple visible.
- **Scroll reveal** (`Reveal`): sections and cards rise 28px and fade in the first time they scroll
  into view, staggered; on the phone, list cards do the same as they are built. The hero's words
  ease back as the page scrolls away (scroll-linked where supported).
- **Field motifs** (`components/motion/FieldDecor.tsx`, `FieldPattern` on the phone): furrows that
  drift sideways and a few floating leaves, behind "How it works", the safety band and the
  sign-in form. The phone's pattern is still, so no animation runs for ever.
- **The video spotlight**: outside a 260px circle around the cursor the video is drained of colour
  and dimmed (`backdrop-filter`, masked); inside it the field is in colour. The scrim above is
  unchanged, so the words keep their contrast.

With reduced motion: no reveal (everything is in place), no drift, no float, no growth line, no
lift; the spotlight still follows the cursor directly.

### The spotlight (sign-in)

The sign-in page shows a photograph in canopy monochrome. A soft circle that follows the cursor
shows it in true colour (`components/spotlight`):

- The radius is `SPOTLIGHT_R = 260`.
- The cursor is eased: each frame the spotlight closes a tenth of the gap.
- The mask is a radial gradient drawn on a hidden canvas and used as the layer's `mask-image`.
  It is drawn at a quarter of the layer's size: the edge is soft, so the result looks the same,
  and encoding it every frame costs a sixteenth as much.
- Touch screens have no cursor, so there the spotlight rests at a fixed point.
- Scrims above the colour layer keep the words readable even with the spotlight behind them.

On arrival, the home and sign-in blocks rise into place one after another (`.rise`, `.rise-2…4`,
640ms). In-page links on the home page scroll smoothly.

The phone has the same idea (`mobile/lib/app/spotlight_photo.dart`): a finger takes the cursor's
place, and on opening the spotlight sweeps once across the photo and rests. The welcome and
sign-in screens also rise in.

### Reduced motion

With `prefers-reduced-motion: reduce` (the web) or "Remove animations" (Android), every effect
above stops, and state changes still happen, instantly:

- No lift, no press travel, no drift and no smooth scrolling. The web's motion classes exist only
  inside a `no-preference` media query, and a final `reduce` rule stops anything else.
- No entrance animation.
- No video: on the home page the `<video>` is never mounted, and its first frame stands as a still
  (`usePrefersReducedMotion` in `lib/motion.ts`). The phone menu appears and closes without a fade,
  a stagger or a morph, and its links do not shift on hover.
- The spotlight does not trail: it sits exactly under the cursor or finger. It is direct
  manipulation, not animation.
- On the phone, there is no opening sweep and no route transition: a new screen simply appears
  (`NoTransitionPage`).
- The exception is a loading spinner (`.motion-essential`). It is how the page says it is still
  working, so it keeps turning, slowly (1.6s a turn).

Tests check this: `use-spotlight.test.ts` (no trailing), `HomeHero.test.tsx` (no video) and
`login_screen_test.dart` (nothing moves, nothing slides in).

## Status is never colour alone

- Every `StatusBadge` shows a word and an icon shape: a tick for done, a cross for danger, a
  triangle for warning, a clock for in progress, and a dashed ring for neutral or unknown.
- Every `Alert` has a shaped icon: an octagon for errors, a triangle for warnings, a circle for
  info and a tick for success.
- In the safety report, a failed hard stop gets an octagon, a red rule down the row and the words
  "Hard stop: ends the run". A revisable failure gets a triangle and "The agent can revise this".
  A rule not checked gets a dashed ring and "Not checked".

## Focus

Every interactive element shows a 2px `brand-600` outline, offset 2px, and outlines are never
removed. On the dark sidebar the outline is `brand-200`, so it stays visible.

## Icons

The icons in `web/src/components/icons` are drawn for this product: 24-unit stroke glyphs at 1.5
weight, inline SVG, and always `aria-hidden` next to a word. They are 20px in navigation and
buttons, and 16px inline. The product uses no icon font and no emoji.

## The phone

The phone shares the palette and type roles through `mobile/lib/app/theme.dart`, and its building
blocks are in `mobile/lib/app/agri_widgets.dart`. Field use drives the differences from the console.

- **Reading in sunlight.** Body text is 16 to 17, secondary text is darker than on the web
  (`inkMuted` `#57534E`, 7.4:1 on the page), and field borders use `outline` `#78716C` (4.6:1).
  Primary buttons fill with `brand-700`, because white on it is 8.7:1.
- **Reach.** Every target is at least 48dp. A screen's main action (Report a crop problem, Next,
  Report problem, Get AI advice, Add plot) sits in a `BottomActionBar` under the thumb. That bar is
  laid out in the body, so it rides above the keyboard. Destructive actions (Cancel a booking,
  Discard a refused report) stand apart from routine ones and use danger ink.
- **Status.** `StatusPill` and `ToneIcon` use the web's shapes (tick, cross, triangle, clock and
  dashed ring), and a `Notice` uses the alert shapes (octagon, triangle, circle and tick). On the
  phone, "Awaiting agronomist review" is *in progress* (a clock), not a warning, because the farmer
  has nothing to do but wait.
- **Depth.** `AgriCard` carries the canopy-tinted `AgriShadows.raised`, and the bottom bar carries
  `lifted`.
- **Timelines.** A case's progress and a crop's stages are vertical timelines with a ticked node
  for each step behind, a "Now" pill for the current step, and a dashed ring for each step to come.
  The wait for the agronomist is drawn as a person, with "A person is checking it".
- **The prescription.** "Do not harvest before" is a danger block with a 2px rule, an octagon, the
  date in large serif type and the days left. It never turns green: the date assumes the spray went
  on the prescribed day.
- **Photos.** Only on the welcome and sign-in screens (under the spotlight) and behind the home
  screen, where one photo per role fills the screen under a canopy scrim and the cards scroll
  over it (see `IMAGE-CREDITS.md`). Empty states use a drawn seedling in `brand-300`.
- **Signed out.** The app opens on a welcome screen (`/welcome`) that leads to sign-in, and signing
  out returns there.
- **Launch.** Android 8+ shows the AgriGuard mark as an adaptive launcher icon (a vector, so no
  bitmaps), and the launch screen is the mark on the warm page, which the Flutter splash continues.
