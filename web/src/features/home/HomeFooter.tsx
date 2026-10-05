import type { ReactNode } from 'react'
import { ExternalLink } from '@/components/icons'
import { BrandMark } from '@/components/layout/BrandMark'

const onThisPage = [
  { href: '#how-it-works', label: 'How it works' },
  { href: '#who-it-is-for', label: 'Who it is for' },
  { href: '#safety', label: 'The eleven safety rules' },
]

const users = [
  { role: 'Farmers', where: 'phone app' },
  { role: 'Field agronomists', where: 'web console' },
  { role: 'Agro-dealers', where: 'web console' },
  { role: 'Co-op administrators', where: 'web console' },
]

/**
 * What the product uses that someone else made. Each is credited where its terms ask (OpenStreetMap
 * requires it) or where it is only fair; the photographs are listed in docs/design/IMAGE-CREDITS.md.
 */
const credits = [
  { href: 'https://open-meteo.com/', label: 'Weather forecasts: Open-Meteo' },
  // MET Norway's data licence (CC BY 4.0) asks for this credit; it answers when Open-Meteo cannot.
  { href: 'https://api.met.no/', label: 'Weather fallback: MET Norway' },
  { href: 'https://www.openstreetmap.org/copyright', label: 'Map tiles: © OpenStreetMap contributors' },
  { href: 'https://www.pexels.com/video/green-plants-on-the-field-7983392/', label: 'Field video: Andi Farruku on Pexels' },
]

/**
 * The page's closing band, in the deep canopy green: the name, then columns of where to go, who it
 * is for, and the credits, then a way back up. Nothing here signs anyone in; that stays at the top.
 */
export function HomeFooter() {
  return (
    <footer className="bg-brand-900 text-white [&_:focus-visible]:outline-brand-200">
      <div className="mx-auto max-w-[1440px] px-5 pt-16 pb-10 sm:px-10">
        <div className="flex items-center gap-4">
          <BrandMark size={52} />
          <span className="font-display text-4xl font-semibold tracking-tight sm:text-[2.75rem]">AgriGuard</span>
        </div>

        <div className="mt-12 grid gap-x-10 gap-y-12 sm:grid-cols-2 lg:grid-cols-[1fr_1fr_1.35fr_1.15fr]">
          <FooterColumn title="On this page">
            {onThisPage.map((l) => (
              <li key={l.href}>
                <a href={l.href} className={linkClass}>
                  {l.label}
                </a>
              </li>
            ))}
          </FooterColumn>

          <FooterColumn title="Who uses it">
            {users.map((u) => (
              <li key={u.role} className="text-brand-100">
                {u.role} <span className="text-brand-300">· {u.where}</span>
              </li>
            ))}
          </FooterColumn>

          <FooterColumn title="Credits">
            {credits.map((c) => (
              <li key={c.href}>
                <a href={c.href} target="_blank" rel="noreferrer" className={linkClass}>
                  {c.label}
                  <ExternalLink size={16} className="ml-1.5 inline-block align-[-2px]" />
                  <span className="sr-only"> (opens in a new tab)</span>
                </a>
              </li>
            ))}
            <li className="text-brand-100">Photographs: listed in the project’s image credits</li>
          </FooterColumn>

          <div>
            <h2 className="font-display text-[1.75rem] leading-tight font-semibold">Crop advice, checked for safety</h2>
            <p className="mt-5 text-base leading-relaxed text-brand-100 sm:text-[17px]">
              Farmers use the AgriGuard phone app; agronomists, dealers and administrators use this web console.
            </p>
            <a
              href="#top"
              className="press mt-6 flex h-12 w-full max-w-sm items-center justify-center rounded-md bg-brand-200 text-[15px] font-semibold text-brand-900 hover:bg-brand-100"
            >
              Back to the top
            </a>
          </div>
        </div>

        <div className="mt-16 flex flex-wrap items-center justify-between gap-3 border-t border-white/10 pt-6 text-sm text-brand-200">
          <span>© 2026 AgriGuard</span>
          <span>Built with ASP.NET Core, React, Flutter and LangGraph</span>
        </div>
      </div>
    </footer>
  )
}

const linkClass = 'text-base text-brand-200 underline-offset-4 sm:text-[17px] hover:text-white hover:underline'

function FooterColumn({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div>
      <h2 className="font-display text-[1.75rem] leading-tight font-semibold">{title}</h2>
      <ul className="mt-5 space-y-3.5 text-base sm:text-[17px]">{children}</ul>
    </div>
  )
}
