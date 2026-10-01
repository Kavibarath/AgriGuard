import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { z } from 'zod'
import { BrandMark } from '@/components/layout/BrandMark'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { ApiError, userMessage } from '@/lib/api'
import { login } from './api'
import { useAuthStore, useIsAuthenticated } from './auth-store'
import { roleLabels, type Role } from './types'

// Client-side rules mirror the API's LoginRequest annotations so obvious mistakes never leave the browser.
const schema = z.object({
  email: z.email('Enter a valid email address').max(256),
  password: z.string().min(1, 'Enter your password').max(256),
})

type FormValues = z.infer<typeof schema>

export function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const setSession = useAuthStore((s) => s.setSession)
  const isAuthenticated = useIsAuthenticated()

  // Where ProtectedRoute sent us from, so a deep link survives the login round trip.
  const from = (location.state as { from?: string } | null)?.from ?? '/dashboard'

  const { register, handleSubmit, setError, setValue, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: '', password: '' },
  })

  const mutation = useMutation({
    mutationFn: login,
    onSuccess: (response) => {
      setSession(response)
      navigate(from, { replace: true })
    },
    onError: (error) => {
      // 400 from the API carries per-field messages; put them on the fields, not in the banner.
      if (error instanceof ApiError && error.status === 400) {
        for (const [field, messages] of Object.entries(error.fieldErrors)) {
          const name = field.toLowerCase() as keyof FormValues
          if (name in schema.shape) setError(name, { message: messages[0] })
        }
      }
    },
  })

  if (isAuthenticated) return <Navigate to={from} replace />

  const bannerMessage =
    mutation.error && !(mutation.error instanceof ApiError && mutation.error.status === 400)
      ? userMessage(mutation.error)
      : null

  return (
    <main className="grid min-h-screen bg-surface-page lg:grid-cols-[45fr_55fr]">
      <section className="relative isolate flex min-h-44 flex-col justify-end overflow-hidden bg-brand-900 px-6 py-6 sm:px-10 lg:min-h-screen lg:py-12">
        <img
          src="/img/hill-terraces-portrait.webp"
          alt=""
          width={900}
          height={1200}
          className="absolute inset-0 -z-10 h-full w-full object-cover"
        />
        {/* A canopy scrim under the words: white text stays above 7:1 over any part of the photo. */}
        <div aria-hidden="true" className="absolute inset-0 -z-10 bg-gradient-to-t from-brand-900/95 via-brand-900/55 to-brand-900/15" />
        <div className="flex items-center gap-3">
          <BrandMark size={40} />
          <h1 className="font-display text-3xl font-semibold text-white lg:text-4xl">AgriGuard</h1>
        </div>
        <p className="mt-3 max-w-md text-base text-brand-50 lg:text-lg">
          Crop-health advice for smallholder farms. An agent drafts it, a rulebook checks it, and an agronomist signs it before any spray is issued.
        </p>
      </section>

      <section className="flex items-center justify-center px-4 py-10 sm:px-8">
        <div className="w-full max-w-sm space-y-6">
          <header className="space-y-1">
            <h2 className="font-display text-2xl font-semibold text-stone-900">Sign in to the advisory console</h2>
            <p className="text-sm text-stone-600">For agronomists, dealers and co-op administrators. Farmers use the phone app.</p>
          </header>

          <form noValidate className="space-y-4" onSubmit={handleSubmit((values) => mutation.mutate(values))}>
            {bannerMessage && <Alert tone="error">{bannerMessage}</Alert>}

            <Field
              label="Email"
              type="email"
              autoComplete="username"
              autoFocus
              error={errors.email?.message}
              {...register('email')}
            />
            <Field
              label="Password"
              type="password"
              autoComplete="current-password"
              error={errors.password?.message}
              {...register('password')}
            />

            <Button type="submit" size="lg" className="w-full" loading={mutation.isPending}>
              Sign in
            </Button>
          </form>

          {showDemoAccounts && (
            <section aria-labelledby="demo-heading" className="rounded-xl border border-dashed border-border-strong bg-surface-sunken/60 p-3">
              <h3 id="demo-heading" className="text-sm font-semibold text-stone-800">
                Demo accounts
              </h3>
              <p className="text-xs text-stone-600">Sample data for the assessment. Choosing one fills in the form.</p>
              <ul className="mt-2 grid grid-cols-2 gap-2">
                {demoAccounts.map((account) => (
                  <li key={account.email}>
                    <button
                      type="button"
                      aria-label={`Fill in the ${roleLabels[account.role]} demo account, ${account.email}`}
                      onClick={() => {
                        setValue('email', account.email, { shouldValidate: false })
                        setValue('password', DEMO_PASSWORD, { shouldValidate: false })
                      }}
                      className="w-full rounded-lg border border-border-subtle bg-surface-card px-2.5 py-2 text-left hover:border-brand-400 hover:bg-brand-50"
                    >
                      <span className="block text-sm font-semibold text-stone-900">{roleLabels[account.role]}</span>
                      <span className="block truncate text-xs text-stone-600">{account.email}</span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </div>
      </section>
    </main>
  )
}

/**
 * The seeded demo users (DemoUserSeeder, academic sample data). Offered only in development, or
 * where a demo deployment sets VITE_DEMO_ACCOUNTS=true, so a production build does not advertise them.
 */
const showDemoAccounts = import.meta.env.DEV || import.meta.env.VITE_DEMO_ACCOUNTS === 'true'
const DEMO_PASSWORD = 'AgriGuard!Demo1'
const demoAccounts: { role: Role; email: string }[] = [
  { role: 'FieldAgronomist', email: 'agronomist@agriguard.demo' },
  { role: 'AgroDealer', email: 'dealer@agriguard.demo' },
  { role: 'CoopAdministrator', email: 'admin@agriguard.demo' },
  { role: 'Farmer', email: 'farmer@agriguard.demo' },
]
