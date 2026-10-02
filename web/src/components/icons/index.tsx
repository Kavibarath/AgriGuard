import type { ReactNode } from 'react'

/**
 * AgriGuard's icon set: one family of 24-unit stroke glyphs at 1.5 weight, drawn for this product
 * (no icon font, no emoji). 20px in navigation and buttons, 16px inline with text. Every icon is
 * decorative (aria-hidden): the words next to it carry the meaning, so status is never an icon or a
 * colour alone.
 */
export interface IconProps {
  size?: 16 | 20 | 24 | number
  className?: string
}

function Svg({ size = 20, className, children }: IconProps & { children: ReactNode }) {
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="none"
      stroke="currentColor"
      strokeWidth={1.5}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      className={className}
    >
      {children}
    </svg>
  )
}

type Icon = (props: IconProps) => ReactNode

// ── Status ──────────────────────────────────────────────────────────────────

export const CheckCircle: Icon = (p) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="9" />
    <path d="m8.25 12.25 2.5 2.5 5-5.25" />
  </Svg>
)

export const XCircle: Icon = (p) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="9" />
    <path d="m9 9 6 6M15 9l-6 6" />
  </Svg>
)

/** A hard stop: an octagon, like the road sign. Used for rules that end a run. */
export const StopOctagon: Icon = (p) => (
  <Svg {...p}>
    <path d="M8.3 3h7.4L21 8.3v7.4L15.7 21H8.3L3 15.7V8.3Z" />
    <path d="m9.25 9.25 5.5 5.5M14.75 9.25l-5.5 5.5" />
  </Svg>
)

export const AlertTriangle: Icon = (p) => (
  <Svg {...p}>
    <path d="M10.3 4.2 2.9 17.1A2 2 0 0 0 4.6 20h14.8a2 2 0 0 0 1.7-2.9L13.7 4.2a2 2 0 0 0-3.4 0Z" />
    <path d="M12 9.5v4M12 16.75v.01" />
  </Svg>
)

export const InfoCircle: Icon = (p) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="9" />
    <path d="M12 11v5M12 7.75v.01" />
  </Svg>
)

/** Not checked / unknown: a dashed ring, deliberately unlike a tick. */
export const DashedCircle: Icon = (p) => (
  <Svg {...p}>
    <path d="M12 3a9 9 0 0 1 3.2.6M19.1 6.5a9 9 0 0 1 1.6 3M21 12a9 9 0 0 1-.9 3.9M17.8 19a9 9 0 0 1-3 1.7M12 21a9 9 0 0 1-3.2-.6M4.9 17.5a9 9 0 0 1-1.6-3M3 12a9 9 0 0 1 .9-3.9M6.2 5a9 9 0 0 1 3-1.7" />
    <path d="M12 12h.01" />
  </Svg>
)

/** In progress. */
export const Clock: Icon = (p) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="9" />
    <path d="M12 7.5V12l3 2" />
  </Svg>
)

/** A filled point: current / in play. */
export const Dot: Icon = (p) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="4" fill="currentColor" stroke="none" />
  </Svg>
)

export const Minus: Icon = (p) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="9" />
    <path d="M8.5 12h7" />
  </Svg>
)

// ── Navigation ──────────────────────────────────────────────────────────────

export const Home: Icon = (p) => (
  <Svg {...p}>
    <path d="M3.5 10.5 12 4l8.5 6.5" />
    <path d="M5.5 9v10.5h13V9" />
    <path d="M10 19.5v-5h4v5" />
  </Svg>
)

/** Crop cases: a clipboard with a leaf. */
export const Clipboard: Icon = (p) => (
  <Svg {...p}>
    <path d="M9 4.5H6.5a1 1 0 0 0-1 1v14a1 1 0 0 0 1 1h11a1 1 0 0 0 1-1v-14a1 1 0 0 0-1-1H15" />
    <rect x="9" y="3" width="6" height="3" rx="1" />
    <path d="M9 16.5c0-3.3 2.7-6 6-6 0 3.3-2.7 6-6 6ZM9 16.5l3-3" />
  </Svg>
)

/** Agent runs: a branching plan. */
export const Workflow: Icon = (p) => (
  <Svg {...p}>
    <rect x="3.5" y="4" width="6" height="5" rx="1" />
    <rect x="14.5" y="4" width="6" height="5" rx="1" />
    <rect x="9" y="15" width="6" height="5" rx="1" />
    <path d="M6.5 9v2.5a1 1 0 0 0 1 1h9a1 1 0 0 0 1-1V9M12 12.5V15" />
  </Svg>
)

/** Farms & plots: fields seen from above. */
export const Fields: Icon = (p) => (
  <Svg {...p}>
    <path d="M3.5 6.5 9 4l6 2.5L20.5 4v13.5L15 20l-6-2.5L3.5 20Z" />
    <path d="M9 4v13.5M15 6.5V20" />
  </Svg>
)

/** Harvests: a basket. */
export const Basket: Icon = (p) => (
  <Svg {...p}>
    <path d="M3.5 10.5h17l-1.8 8.2a1 1 0 0 1-1 .8H6.3a1 1 0 0 1-1-.8Z" />
    <path d="m8 10.5 3-6M16 10.5l-3-6M9 14v2.5M12 14v2.5M15 14v2.5" />
  </Svg>
)

/** Collection planner: a truck. */
export const Truck: Icon = (p) => (
  <Svg {...p}>
    <path d="M3 6.5h10.5v10H3ZM13.5 10H17l3.5 3.5v3h-7" />
    <circle cx="7" cy="17.5" r="1.75" />
    <circle cx="17" cy="17.5" r="1.75" />
  </Svg>
)

/** Disease intelligence: a pulse over a district. */
export const Pulse: Icon = (p) => (
  <Svg {...p}>
    <path d="M3 12h4l2-5 4 10 2-5h6" />
  </Svg>
)

/** Inventory: a sack of inputs. */
export const Package: Icon = (p) => (
  <Svg {...p}>
    <path d="M4 7.5 12 3.5l8 4v9l-8 4-8-4Z" />
    <path d="M4 7.5l8 4 8-4M12 11.5v9M8 5.5l8 4" />
  </Svg>
)

/** Orders: a receipt. */
export const Receipt: Icon = (p) => (
  <Svg {...p}>
    <path d="M6 3.5h12v17l-2-1.25-2 1.25-2-1.25-2 1.25-2-1.25-2 1.25Z" />
    <path d="M9 8h6M9 11.5h6M9 15h3.5" />
  </Svg>
)

/** Regulatory rules: a balance. */
export const Scale: Icon = (p) => (
  <Svg {...p}>
    <path d="M12 4v16M8 20h8M5 7h14M12 4.5 5 7M12 4.5 19 7" />
    <path d="m5 7-2.5 6a2.5 2.5 0 0 0 5 0ZM19 7l-2.5 6a2.5 2.5 0 0 0 5 0Z" />
  </Svg>
)

export const Search: Icon = (p) => (
  <Svg {...p}>
    <circle cx="10.5" cy="10.5" r="6" />
    <path d="m15 15 5 5" />
  </Svg>
)

export const Bell: Icon = (p) => (
  <Svg {...p}>
    <path d="M6 16.5V11a6 6 0 1 1 12 0v5.5l1.5 2h-15Z" />
    <path d="M10 20.5a2 2 0 0 0 4 0" />
  </Svg>
)

export const SignOut: Icon = (p) => (
  <Svg {...p}>
    <path d="M14 4.5H6.5a1 1 0 0 0-1 1v13a1 1 0 0 0 1 1H14" />
    <path d="M10.5 12h10M17 8.5l3.5 3.5-3.5 3.5" />
  </Svg>
)

export const ChevronLeft: Icon = (p) => (
  <Svg {...p}>
    <path d="m14.5 6-6 6 6 6" />
  </Svg>
)

export const ChevronRight: Icon = (p) => (
  <Svg {...p}>
    <path d="m9.5 6 6 6-6 6" />
  </Svg>
)

export const ChevronDown: Icon = (p) => (
  <Svg {...p}>
    <path d="m6 9.5 6 6 6-6" />
  </Svg>
)

export const ArrowLeft: Icon = (p) => (
  <Svg {...p}>
    <path d="M19 12H5.5M11 6 5 12l6 6" />
  </Svg>
)

export const ArrowRight: Icon = (p) => (
  <Svg {...p}>
    <path d="M5 12h13.5M13 6l6 6-6 6" />
  </Svg>
)

/** A link that leaves AgriGuard for another site. */
export const ExternalLink: Icon = (p) => (
  <Svg {...p}>
    <path d="M13.5 5.5H18.5V10.5M18.5 5.5 11 13M10 6.5H6.5a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1V14" />
  </Svg>
)

export const Menu: Icon = (p) => (
  <Svg {...p}>
    <path d="M4 7h16M4 12h16M4 17h16" />
  </Svg>
)

export const Close: Icon = (p) => (
  <Svg {...p}>
    <path d="m6 6 12 12M18 6 6 18" />
  </Svg>
)

// ── Field, crop and weather (one family with the status glyphs) ─────────────

export const Leaf: Icon = (p) => (
  <Svg {...p}>
    <path d="M5 19c0-8.3 6.2-14 14.5-14 0 8.3-6.2 14-14.5 14Z" />
    <path d="M5 19 13.5 10.5" />
  </Svg>
)

export const Sprout: Icon = (p) => (
  <Svg {...p}>
    <path d="M12 20.5V11" />
    <path d="M12 11c0-3.6-2.9-6.5-6.5-6.5 0 3.6 2.9 6.5 6.5 6.5ZM12 13.5c0-3.3 2.7-6 6-6 0 3.3-2.7 6-6 6Z" />
    <path d="M7.5 20.5h9" />
  </Svg>
)

export const Droplet: Icon = (p) => (
  <Svg {...p}>
    <path d="M12 3.5s-6 6.4-6 10.5a6 6 0 0 0 12 0c0-4.1-6-10.5-6-10.5Z" />
  </Svg>
)

export const Sun: Icon = (p) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="4" />
    <path d="M12 2.5v2M12 19.5v2M2.5 12h2M19.5 12h2M5.3 5.3l1.4 1.4M17.3 17.3l1.4 1.4M5.3 18.7l1.4-1.4M17.3 6.7l1.4-1.4" />
  </Svg>
)

export const CloudRain: Icon = (p) => (
  <Svg {...p}>
    <path d="M7 15.5a4.5 4.5 0 1 1 1-8.9A5.5 5.5 0 0 1 18.5 9a3.25 3.25 0 0 1-.5 6.5Z" />
    <path d="m9 18-1 2.5M13 18l-1 2.5M17 18l-1 2.5" />
  </Svg>
)

export const MapPin: Icon = (p) => (
  <Svg {...p}>
    <path d="M12 21s-6.5-5.8-6.5-11a6.5 6.5 0 0 1 13 0c0 5.2-6.5 11-6.5 11Z" />
    <circle cx="12" cy="10" r="2.25" />
  </Svg>
)

export const Calendar: Icon = (p) => (
  <Svg {...p}>
    <rect x="3.5" y="5" width="17" height="15" rx="1.5" />
    <path d="M3.5 9.5h17M8 3v4M16 3v4" />
  </Svg>
)

export const Camera: Icon = (p) => (
  <Svg {...p}>
    <path d="M4 8a1 1 0 0 1 1-1h2.5L9 4.5h6L16.5 7H19a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1Z" />
    <circle cx="12" cy="12.5" r="3.5" />
  </Svg>
)

export const User: Icon = (p) => (
  <Svg {...p}>
    <circle cx="12" cy="8" r="3.75" />
    <path d="M4.5 20a7.5 7.5 0 0 1 15 0" />
  </Svg>
)

// ── The four agents ─────────────────────────────────────────────────────────

/** Coordinator: plans the route through the case. */
export const Compass: Icon = (p) => (
  <Svg {...p}>
    <circle cx="12" cy="12" r="9" />
    <path d="m15.5 8.5-2.1 4.9-4.9 2.1 2.1-4.9Z" />
  </Svg>
)

/** Diagnosis: a lens over a leaf. */
export const LensLeaf: Icon = (p) => (
  <Svg {...p}>
    <circle cx="10.5" cy="10.5" r="6.5" />
    <path d="m15.25 15.25 5 5" />
    <path d="M7.5 13.5c0-3.3 2.2-5.5 5.5-5.5 0 3.3-2.2 5.5-5.5 5.5ZM7.5 13.5l2.5-2.5" />
  </Svg>
)

/** Action: a sprayer. */
export const Sprayer: Icon = (p) => (
  <Svg {...p}>
    <path d="M8 9.5h7v10a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1Z" />
    <path d="M9.5 9.5V7h4v2.5M13.5 7h3l1.5-1.5" />
    <path d="M19.5 3.5v.01M21 6v.01M19.5 8.5v.01" />
  </Svg>
)

/** Validation: a shield with a tick. */
export const ShieldCheck: Icon = (p) => (
  <Svg {...p}>
    <path d="M12 3 5 5.5V11c0 4.4 3 8.2 7 9.5 4-1.3 7-5.1 7-9.5V5.5Z" />
    <path d="m9 12 2.2 2.2L15.5 10" />
  </Svg>
)
