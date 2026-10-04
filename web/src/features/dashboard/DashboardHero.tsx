import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { ChevronRight, MapPin, Package, Receipt, Scale, ShieldCheck, Sprout, Truck, User, Workflow } from '@/components/icons'
import type { Role, UserSummary } from '@/features/auth/types'
import { roleLabels } from '@/features/auth/types'
import { cn } from '@/lib/utils'

/**
 * The photograph for each role's dashboard (docs/design/IMAGE-CREDITS.md): fields from above for the
 * people who oversee plots, harvest lanes for the farmer, a tractor hauling inputs for the dealer.
 */
const photos: Record<Role, { src: string; width: number; height: number; position: string }> = {
  FieldAgronomist: { src: '/img/fields-aerial-portrait.webp', width: 620, height: 1104, position: 'object-[50%_35%]' },
  CoopAdministrator: { src: '/img/fields-aerial-portrait.webp', width: 620, height: 1104, position: 'object-[50%_65%]' },
  Farmer: { src: '/img/harvest-lanes-portrait.webp', width: 620, height: 1240, position: 'object-[50%_45%]' },
  AgroDealer: { src: '/img/tractor-maize-portrait.webp', width: 720, height: 900, position: 'object-[40%_60%]' },
}

/** Where each role goes first, and second. */
const actions: Record<Role, { primary: { to: string; label: string }; secondary: { to: string; label: string } }> = {
  FieldAgronomist: { primary: { to: '/agent-runs', label: 'Review proposals' }, secondary: { to: '/cases?status=Submitted', label: 'New reports' } },
  Farmer: { primary: { to: '/cases', label: 'Follow my cases' }, secondary: { to: '/farms', label: 'My farms and plots' } },
  AgroDealer: { primary: { to: '/orders?view=Confirmed', label: 'Pack orders' }, secondary: { to: '/inventory', label: 'Check stock' } },
  CoopAdministrator: { primary: { to: '/rules', label: 'Edit the rules' }, secondary: { to: '/cases?status=AwaitingManualReview', label: 'Cases in review' } },
}

/**
 * Three short truths about how the system protects this role's work, floated over the photo. They
 * describe the product, not live data: the figures in the tiles below are the live data.
 */
const notes: Record<Role, { icon: ReactNode; title: string; text: string }[]> = {
  FieldAgronomist: [
    { icon: <ShieldCheck />, title: 'Checked by 11 rules', text: 'Every proposal is validated before it reaches you' },
    { icon: <User />, title: 'You decide', text: 'Nothing is prescribed until you approve it' },
    { icon: <Sprout />, title: 'Safe to harvest', text: 'Each prescription carries its pre-harvest date' },
  ],
  Farmer: [
    { icon: <ShieldCheck />, title: 'Checked for safety', text: 'Every treatment passes the safety rules' },
    { icon: <User />, title: 'A person approves', text: 'An agronomist signs off each prescription' },
    { icon: <Truck />, title: 'Collect with a code', text: 'Your inputs wait at the dealer for you' },
  ],
  AgroDealer: [
    { icon: <Package />, title: 'Stock held early', text: 'Packs are set aside as soon as a treatment is proposed' },
    { icon: <Receipt />, title: 'Pickup code', text: 'Hand an order over only on the farmer’s code' },
    { icon: <Truck />, title: 'Oldest first', text: 'Batches are drawn by earliest expiry' },
  ],
  CoopAdministrator: [
    { icon: <Scale />, title: 'Your rules decide', text: 'The validator applies the approvals you keep' },
    { icon: <Workflow />, title: 'Four agents', text: 'Diagnosis, action, validation and coordination' },
    { icon: <ShieldCheck />, title: 'People sign off', text: 'Agronomists approve every prescription' },
  ],
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
 * The dashboard's opening card: the greeting, where and when, and the two places this role goes
 * first on the left; the role's photograph on the right with three notes floating over it; and the
 * four live figures along the bottom, the right-hand ones lying over the photo's lower edge.
 */
export function DashboardHero({ user, districtName, children }: { user: UserSummary; districtName: string | null | undefined; children: ReactNode }) {
  const photo = photos[user.role]
  const action = actions[user.role]
  const now = new Date()
  // A dealer's account is named after the shop, so it is greeted in full.
  const name = user.role === 'AgroDealer' ? user.fullName : firstName(user.fullName)

  return (
    <section aria-labelledby="dashboard-title" className="relative isolate overflow-hidden rounded-2xl border border-border-subtle bg-surface-card shadow-raised">
      {/* The photo: a band across the top on narrow screens, the right 44% of the card on wide ones. */}
      <div className="relative h-52 sm:h-60 lg:absolute lg:inset-y-0 lg:right-0 lg:h-auto lg:w-[44%]">
        <img
          src={photo.src}
          alt=""
          width={photo.width}
          height={photo.height}
          className={cn('absolute inset-0 h-full w-full object-cover', photo.position)}
        />
        {/* A canopy scrim, deepest where the notes sit and where the photo meets the card. */}
        <div aria-hidden="true" className="absolute inset-0 bg-gradient-to-b from-brand-900/35 via-brand-900/10 to-brand-900/55 lg:bg-gradient-to-r lg:from-transparent lg:via-transparent lg:to-brand-900/30" />
        {/* On wide screens the photo's left edge fades into the card instead of stopping at a line. */}
        <div aria-hidden="true" className="absolute inset-y-0 left-0 hidden w-1/3 bg-gradient-to-r from-surface-card via-surface-card/60 to-transparent lg:block" />
        <ul aria-label="How AgriGuard protects your work" className="absolute top-4 right-4 hidden w-64 space-y-2.5 md:block">
          {notes[user.role].map((note) => (
            <li key={note.title} className="flex items-start gap-2.5 rounded-xl bg-surface-card/95 p-2.5 shadow-lifted">
              <span aria-hidden="true" className="mt-0.5 grid size-8 shrink-0 place-items-center rounded-lg bg-brand-50 text-brand-700">
                {note.icon}
              </span>
              <span className="min-w-0">
                <span className="block text-sm font-semibold text-stone-900">{note.title}</span>
                <span className="block text-xs leading-snug text-stone-600">{note.text}</span>
              </span>
            </li>
          ))}
        </ul>
      </div>

      <div className="relative px-5 pt-5 pb-2 sm:px-7 sm:pt-7 lg:min-h-[17rem] lg:w-[56%] lg:pr-10">
        <p className="inline-flex items-center gap-1.5 rounded-full border border-brand-200 bg-brand-50 py-1 pr-3 pl-2 text-xs font-medium text-brand-800">
          <MapPin size={14} />
          {now.toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long' })}
          {districtName ? ` · ${districtName}` : ''}
        </p>
        <h1 id="dashboard-title" className="font-display mt-3 text-4xl leading-[1.1] font-semibold text-stone-900 sm:text-[44px]">
          {greeting(now)}, <span className="text-brand-600">{name}</span>.
        </h1>
        <p className="mt-2 max-w-md text-[15px] text-stone-600">{roleLabels[user.role]} dashboard. What needs you today is below, oldest first.</p>
        <div className="mt-5 flex flex-wrap items-center gap-2">
          <Link
            to={action.primary.to}
            className="press inline-flex h-11 items-center gap-2 rounded-md bg-brand-600 px-5 text-[15px] font-medium text-white shadow-raised hover:bg-brand-700"
          >
            {action.primary.label}
            <ChevronRight size={18} />
          </Link>
          <Link to={action.secondary.to} className="inline-flex h-11 items-center rounded-md px-4 text-[15px] font-medium text-brand-700 hover:bg-brand-50">
            {action.secondary.label}
          </Link>
        </div>
      </div>

      {/* The live figures. On wide screens the right-hand tiles sit over the photo's lower edge. */}
      <div className="relative z-10 grid gap-4 p-5 sm:grid-cols-2 sm:p-7 xl:grid-cols-4">{children}</div>
    </section>
  )
}
