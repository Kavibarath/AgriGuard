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

With `prefers-reduced-motion: reduce`, all of it stops: there is no lift, no press travel, no drift
and no parallax. The motion classes exist only inside a `no-preference` media query. State changes
still happen, instantly.

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
- **Photos.** Only two: the login panel and the home header strip (see `IMAGE-CREDITS.md`). Empty
  states use a drawn seedling in `brand-300`.
- **Launch.** Android 8+ shows the AgriGuard mark as an adaptive launcher icon (a vector, so no
  bitmaps), and the launch screen is the mark on the warm page, which the Flutter splash continues.
