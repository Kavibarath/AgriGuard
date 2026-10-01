import { Link } from 'react-router'

/** Full-page notice with a way back — used for 404 and forbidden routes. */
export function MessagePage({ title, children }: { title: string; children: string }) {
  return (
    <div className="mx-auto max-w-md space-y-3 py-16 text-center">
      <h1 className="font-display text-2xl font-semibold text-stone-900">{title}</h1>
      <p className="text-sm text-stone-600">{children}</p>
      <Link to="/dashboard" className="text-sm font-medium text-brand-700 underline">
        Back to dashboard
      </Link>
    </div>
  )
}
