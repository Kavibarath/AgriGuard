import { Link } from 'react-router'

/** Full-page notice with a way back — used for 404 and forbidden routes. */
export function MessagePage({ title, children }: { title: string; children: string }) {
  return (
    <main className="mx-auto max-w-md space-y-3 p-8 text-center">
      <h1 className="text-xl font-semibold">{title}</h1>
      <p className="text-sm text-stone-600">{children}</p>
      <Link to="/dashboard" className="text-sm text-brand-600 underline">
        Back to dashboard
      </Link>
    </main>
  )
}
