import type { ReactNode } from 'react'
import { FeaturedStatsCarousel, type FeaturedStat } from '@/components/carousel/FeaturedStatsCarousel'
import { Camera, LensLeaf, ShieldCheck, User } from '@/components/icons'
import { FloatingLeaves, Furrows } from '@/components/motion/FieldDecor'
import { Reveal } from '@/components/motion/Reveal'
import { useIsAuthenticated } from '@/features/auth/auth-store'
import { HomeFooter } from './HomeFooter'
import { HomeHero } from './HomeHero'

const steps: { icon: ReactNode; title: string; text: string }[] = [
  {
    icon: <Camera size={24} />,
    title: 'Report from the field',
    text: 'A farmer photographs the problem, ticks what they see, and the phone adds where they are standing. With no signal, the report waits on the phone.',
  },
  {
    icon: <LensLeaf size={24} />,
    title: 'Four agents draft advice',
    text: 'The agents rank the likely pest or disease, weigh the coming weather, and propose a product, a dose and a spray day.',
  },
  {
    icon: <ShieldCheck size={24} />,
    title: 'Eleven rules check it',
    text: 'Every proposal is tested against the approved products, the dose limits, the spray weather and the days before harvest. A failed hard stop ends the run.',
  },
  {
    icon: <User size={24} />,
    title: 'An agronomist decides',
    text: 'Nothing is prescribed until a person approves it. Then the stock is set aside, and the farmer collects it with a pickup code.',
  },
]

const roles: { image: string; position: string; role: string; where: string; text: string }[] = [
  {
    image: '/img/harvest-lanes-portrait.webp',
    position: 'center 40%',
    role: 'Farmers',
    where: 'Phone app',
    text: 'Report a problem, follow it to a prescription, see the safe harvest date and book collection.',
  },
  {
    image: '/img/fields-aerial-portrait.webp',
    position: 'center 30%',
    role: 'Field agronomists',
    where: 'Web console',
    text: 'Review each proposal with its evidence and safety report, then approve, reject or ask for a revision.',
  },
  {
    image: '/img/tractor-maize-portrait.webp',
    position: '35% center',
    role: 'Agro-dealers',
    where: 'Web console',
    text: 'Keep stock by batch, pack approved orders, and hand them over on the farmer’s pickup code.',
  },
  {
    // The hills without the people working them: nobody pictured stands for a user.
    image: '/img/banner-hill-country.webp',
    position: 'center',
    role: 'Co-op administrators',
    where: 'Web console',
    text: 'Keep the table of approved products and limits that every proposal is checked against, and watch outbreaks.',
  },
]

/**
 * The eleven rules of the prescription safety validator (PrescriptionSafetyValidator.cs), in its own
 * words. A hard stop ends the run; anything else goes back to the agent to revise. Every figure is
 * the validator's own (V4's 2% tolerance, V8's 40% / 0.5 mm / 15 km/h / 32 °C), so change them together.
 */
const rules: { code: string; name: string; hardStop: boolean; stat: string; text: string }[] = [
  { code: 'V1', name: 'Proposal is well formed', hardStop: true, stat: '4 checks', text: 'A product is chosen, the dose and the quantity are above zero, and the spray day is not in the past.' },
  { code: 'V2', name: 'Product is approved for this crop', hardStop: true, stat: 'Listed only', text: 'Only a product on the co-op’s table for this crop, and not withdrawn from it, can be proposed at all.' },
  { code: 'V3', name: 'Dose is within the label range', hardStop: false, stat: 'Min–max', text: 'The dose per hectare sits inside the range approved for this product on this crop.' },
  { code: 'V4', name: 'Quantity matches dose × area', hardStop: false, stat: '2%', text: 'The quantity ordered is the dose times the plot’s area, give or take 2% for pack sizes.' },
  { code: 'V5', name: 'Pre-harvest interval is respected', hardStop: true, stat: '0 days', text: 'The spray day plus the product’s waiting period must fall on or before the planned harvest. This is the rule that keeps residue off food.' },
  { code: 'V6', name: 'Seasonal application limit is not exceeded', hardStop: true, stat: 'Per ingredient', text: 'Sprays are counted by active ingredient, so two brands of one chemical cannot double the season’s allowance.' },
  { code: 'V7', name: 'Resistance-management interval has passed', hardStop: false, stat: 'Minimum gap', text: 'Enough days must pass since the same ingredient was last sprayed: repeating it too soon breeds resistance.' },
  { code: 'V8', name: 'Weather suits spraying', hardStop: false, stat: '40%', text: 'A 40% chance of at least 0.5 mm of rain before the spray is rainfast, wind from 15 km/h, or heat above 32 °C moves the spray day.' },
  { code: 'V9', name: 'Dealer stock covers the order', hardStop: false, stat: 'In date', text: 'The dealer holds enough, from a batch that does not expire before the spray day.' },
  { code: 'V10', name: 'The farmer may treat this plot with this product', hardStop: true, stat: 'Owner only', text: 'The plot belongs to the farmer the advice is for, and a restricted product needs the farm’s permit.' },
  { code: 'V11', name: 'Order is within the farmer’s credit limit', hardStop: false, stat: 'Credit limit', text: 'Where the co-op sets a limit, the estimated cost of the order stays within it.' },
]

const ruleCards: FeaturedStat[] = rules.map((rule) => ({
  id: rule.code,
  stat: rule.stat,
  highlight: rule.name,
  description: rule.text,
  // The words say it; the colour only repeats it.
  eyebrow: (
    <>
      {rule.code} · <span className={rule.hardStop ? 'text-danger' : undefined}>{rule.hardStop ? 'Hard stop' : 'Can be revised'}</span>
    </>
  ),
}))

/**
 * The public introduction to AgriGuard, before signing in: the name and the way in, a field on
 * video, how the system works, who uses it, the eleven safety rules, and the credits. The way in
 * appears once, at the top.
 */
export function HomePage() {
  const enter = useIsAuthenticated() ? { to: '/dashboard', label: 'Open your dashboard' } : { to: '/login', label: 'Sign in' }

  return (
    <div className="min-h-screen bg-surface-page">
      <HomeHero enter={enter} />

      <section id="how-it-works" aria-labelledby="how-heading" className="relative isolate scroll-mt-4 overflow-hidden bg-gradient-to-b from-brand-50 via-surface-page to-earth-50">
        {/* A field from above, drifting slowly, and a few leaves: the ground the steps stand on. */}
        <Furrows className="-z-10 opacity-40 [mask-image:linear-gradient(to_bottom,transparent,black_25%,black_75%,transparent)]" />
        <FloatingLeaves className="-z-10" />
        <div className="mx-auto max-w-[1440px] px-5 py-20 sm:px-10">
          <Reveal>
            <p className="text-sm font-semibold text-brand-700">How it works</p>
            <h2 id="how-heading" className="font-display mt-1 max-w-2xl text-4xl leading-tight font-semibold text-stone-900">
              From a leaf spot to a safe prescription, with a person in charge.
            </h2>
          </Reveal>
          <Reveal className="relative mt-12">
            {/* The growth line: drawn once from the first step to the last as the steps arrive. */}
            <svg aria-hidden="true" viewBox="0 0 1000 40" preserveAspectRatio="none" className="pointer-events-none absolute -top-6 left-[6%] hidden h-10 w-[88%] xl:block">
              <path d="M0 30 C 160 0, 340 40, 500 20 S 840 0, 1000 24" pathLength="1" fill="none" stroke="var(--color-brand-400)" strokeWidth="2" strokeDasharray="1" className="grow-line" />
            </svg>
            <ol className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              {steps.map((step, i) => (
                <Reveal as="li" key={step.title} delay={120 * i} className="glass-light glass-lift rounded-2xl p-6">
                  <div className="flex items-center justify-between">
                    <span aria-hidden="true" className="grid size-12 place-items-center rounded-xl bg-gradient-to-br from-brand-500 to-brand-700 text-white shadow-[inset_0_1px_0_rgb(255_255_255/0.25),0_6px_16px_rgb(19_48_37/0.25)]">
                      {step.icon}
                    </span>
                    <span className="font-display text-canopy-gradient text-4xl font-semibold">{String(i + 1).padStart(2, '0')}</span>
                  </div>
                  <h3 className="mt-5 text-lg font-semibold text-stone-900">{step.title}</h3>
                  <p className="mt-1.5 text-[15px] text-stone-600">{step.text}</p>
                </Reveal>
              ))}
            </ol>
          </Reveal>
        </div>
      </section>

      <section id="who-it-is-for" aria-labelledby="who-heading" className="bg-surface-sunken">
        <div className="mx-auto max-w-[1440px] scroll-mt-4 px-5 py-16 sm:px-10">
          <p className="text-sm font-semibold text-brand-700">Who it is for</p>
          <h2 id="who-heading" className="font-display mt-1 max-w-2xl text-4xl leading-tight font-semibold text-stone-900">
            One record, from the farm to the dealer’s counter.
          </h2>
          <ul className="mt-10 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            {roles.map((r, i) => (
              <Reveal as="li" key={r.role} delay={100 * i} className="card-raised glass-lift relative isolate flex min-h-80 flex-col justify-end overflow-hidden rounded-2xl bg-brand-900 p-5 text-white">
                <img src={r.image} alt="" className="absolute inset-0 -z-10 h-full w-full object-cover" style={{ objectPosition: r.position }} />
                <div aria-hidden="true" className="absolute inset-0 -z-10 bg-gradient-to-t from-brand-900/95 via-brand-900/55 to-brand-900/5" />
                <p className="text-xs font-semibold tracking-wide text-brand-200">{r.where}</p>
                <h3 className="font-display mt-1 text-2xl font-semibold">{r.role}</h3>
                <p className="mt-1.5 text-sm text-brand-50">{r.text}</p>
              </Reveal>
            ))}
          </ul>
        </div>
      </section>

      {/*
        * Editorial and quiet on purpose: the plain page, one figure at a time, opening on V5. The
        * carousel is the named region, so the section is not labelled a second time.
        */}
      <section id="safety" className="relative isolate scroll-mt-4 overflow-hidden border-t border-border-subtle bg-surface-page py-24">
        {/* The field's furrows and leaves from "How it works", fainter, so the cards stay the subject. */}
        <Furrows className="-z-10 opacity-35 [mask-image:linear-gradient(to_bottom,transparent,black_20%,black_80%,transparent)]" />
        <FloatingLeaves className="-z-10 opacity-80" />
        <Reveal>
          <FeaturedStatsCarousel
            items={ruleCards}
            titleId="safety-heading"
            kicker="Safety"
            title="Eleven rules stand between a proposal and the field."
            intro="Checked in code, not by the AI: when an agent proposes a treatment, and again when an agronomist approves it. A hard stop ends the run; anything else goes back to the agent to revise."
            initialIndex={4}
            seeAllLabel="See all 11 rules"
          />
        </Reveal>
      </section>

      <HomeFooter />
    </div>
  )
}
