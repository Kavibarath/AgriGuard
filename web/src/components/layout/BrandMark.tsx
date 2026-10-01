/**
 * The AgriGuard mark: a leaf inside a shield — crop advice held to a safety rule. Drawn in the
 * same 1.5 stroke as the icon set, on a canopy tile so it reads on light and dark grounds.
 */
export function BrandMark({ size = 30 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" aria-hidden="true" focusable="false">
      <rect width="32" height="32" rx="8" fill="#2f855a" />
      <path d="M16 5.5 8 8.4v6.3c0 5 3.4 9.4 8 10.8 4.6-1.4 8-5.8 8-10.8V8.4Z" fill="none" stroke="#f2f8f3" strokeWidth="1.5" strokeLinejoin="round" />
      <path d="M12 19.5c0-4.6 3.2-7.8 7.8-7.8 0 4.6-3.2 7.8-7.8 7.8Z" fill="#bce3c2" />
      <path d="m12 19.5 4.4-4.4" stroke="#1a4230" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  )
}
