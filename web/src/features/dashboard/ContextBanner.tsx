import type { UserSummary } from '@/features/auth/types'
import { roleLabels } from '@/features/auth/types'

/**
 * Which landscape a district looks like. The hill country gets its terraces, Matale the paddy under
 * Sigiriya, and the dry zone (and anyone without a district) the open paddy plain.
 */
function photoFor(districtName: string | null | undefined): { src: string; alt: string } {
  const name = districtName?.toLowerCase() ?? ''
  if (['nuwara eliya', 'kandy', 'badulla'].some((d) => name.includes(d)))
    return { src: '/img/banner-hill-country.webp', alt: 'Terraced paddy below wooded hills in the hill country' }
  if (name.includes('matale')) return { src: '/img/banner-sigiriya-paddy.webp', alt: 'Paddy fields below Sigiriya rock' }
  return { src: '/img/banner-dry-zone-paddy.webp', alt: 'Ripening paddy on the dry-zone plain' }
}

function greeting(now: Date): string {
  const hour = now.getHours()
  return hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening'
}

/** "Nimali" from "Dr. Nimali Fernando". */
function firstName(fullName: string): string {
  return fullName.replace(/^(Dr|Mr|Mrs|Ms)\.?\s+/i, '').split(/\s+/)[0] ?? fullName
}

/**
 * The dashboard's context band: 160px of the user's own landscape under a canopy scrim, carrying
 * the page title, who is signed in and where. Context, not a hero; the data starts right below it.
 */
export function ContextBanner({ user, districtName }: { user: UserSummary; districtName: string | null | undefined }) {
  const photo = photoFor(districtName)
  const now = new Date()
  return (
    <header className="relative isolate flex h-40 items-end overflow-hidden rounded-xl bg-brand-800 shadow-raised">
      <img
        src={photo.src}
        alt={photo.alt}
        width={1600}
        height={400}
        className="absolute inset-0 -z-10 h-full w-full object-cover"
      />
      {/* The scrim keeps white text at better than 7:1 over any part of the photo. */}
      <div aria-hidden="true" className="absolute inset-0 -z-10 bg-gradient-to-r from-brand-900/90 via-brand-900/60 to-brand-900/10" />
      <div className="px-5 pb-4 sm:px-6">
        <p className="text-sm font-medium text-brand-100">
          {now.toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long' })}
          {districtName ? ` — ${districtName}` : ''}
        </p>
        <h1 className="font-display mt-0.5 text-3xl leading-tight font-semibold text-white">
          {/* A dealer's account is named after the shop, so it is greeted in full. */}
          {greeting(now)}, {user.role === 'AgroDealer' ? user.fullName : firstName(user.fullName)}
        </h1>
        <p className="mt-0.5 text-sm text-brand-50">{roleLabels[user.role]} dashboard. What needs you today.</p>
      </div>
    </header>
  )
}
