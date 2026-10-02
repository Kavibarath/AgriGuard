import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { Link, Navigate, useLocation, useNavigate } from 'react-router'
import { z } from 'zod'
import { ArrowLeft } from '@/components/icons'
import { BrandMark } from '@/components/layout/BrandMark'
import { FloatingLeaves, Furrows } from '@/components/motion/FieldDecor'
import { DuotoneBase, RevealLayer } from '@/components/spotlight/spotlight'
import { useSmoothedCursor } from '@/components/spotlight/use-spotlight'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Field } from '@/components/ui/field'
import { ApiError, userMessage } from '@/lib/api'
import { login } from './api'
import { useAuthStore, useIsAuthenticated } from './auth-store'

// Client-side rules mirror the API's LoginRequest annotations so obvious mistakes never leave the browser.
const schema = z.object({
  email: z.email('Enter a valid email address').max(256),
  password: z.string().min(1, 'Enter your password').max(256),
})

type FormValues = z.infer<typeof schema>

/** A tractor hauling past maize: the spotlight shows it in colour. */
const PHOTO = '/img/tractor-maize-square.webp'
const PHOTO_POSITION = '70% center'

export function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const setSession = useAuthStore((s) => s.setSession)
  const isAuthenticated = useIsAuthenticated()
  // On a touch screen the spotlight rests over the tractor, inside the photo panel.
  const cursor = useSmoothedCursor({ x: 0.22, y: 0.2 })

  // Where ProtectedRoute sent us from, so a deep link survives the login round trip.
  const from = (location.state as { from?: string } | null)?.from ?? '/dashboard'

  const { register, handleSubmit, setError, formState: { errors } } = useForm<FormValues>({
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
    // The photograph takes most of a wide screen; the form keeps a narrow column of its own.
    <main className="grid min-h-screen bg-surface-page lg:grid-cols-[minmax(0,1fr)_440px] xl:grid-cols-[minmax(0,1fr)_480px]">
      {/* The photo in canopy monochrome; the spotlight that follows the cursor shows it in colour. */}
      <section className="relative isolate flex min-h-56 flex-col justify-end overflow-hidden bg-brand-900 px-6 py-6 sm:px-10 lg:min-h-screen lg:py-12">
        <DuotoneBase image={PHOTO} position={PHOTO_POSITION} />
        <RevealLayer image={PHOTO} position={PHOTO_POSITION} cursorX={cursor.x} cursorY={cursor.y} />
        {/* Over the reveal as well, so the words stay above 7:1 even with the spotlight behind them. */}
        <div aria-hidden="true" className="pointer-events-none absolute inset-0 z-[35] bg-gradient-to-t from-brand-900/95 via-brand-900/45 to-brand-900/10" />
        <div className="relative z-40">
          <Link to="/" className="rise inline-flex items-center gap-3 rounded-md">
            <BrandMark size={40} />
            <h1 className="font-display text-3xl font-semibold text-white lg:text-4xl">AgriGuard</h1>
          </Link>
          <p className="rise rise-2 mt-3 max-w-md text-base text-brand-50 lg:text-lg">
            Crop-health advice for smallholder farms. An agent drafts it, a rulebook checks it, and an agronomist signs it before any spray is issued.
          </p>
        </div>
      </section>

      {/* A canopy-tinted ground with drifting furrows and leaves, and the form on a pane of glass. */}
      <section className="relative isolate flex items-center justify-center overflow-hidden bg-gradient-to-br from-brand-50 via-surface-page to-earth-50 px-4 py-10 sm:px-8">
        <Furrows className="-z-10 opacity-35" />
        <FloatingLeaves className="-z-10" />
        <div aria-hidden="true" className="absolute -top-24 -right-24 -z-10 size-72 rounded-full bg-brand-200/50 blur-3xl" />
        <div aria-hidden="true" className="absolute -bottom-24 -left-16 -z-10 size-64 rounded-full bg-earth-200/50 blur-3xl" />
        <div className="glass-light w-full max-w-sm space-y-6 rounded-2xl p-6 sm:p-7">
          <Link to="/" className="rise inline-flex items-center gap-1.5 rounded-sm text-sm font-medium text-brand-700 hover:underline">
            <ArrowLeft size={16} />
            Back to the AgriGuard home page
          </Link>
          <header className="rise rise-2 space-y-1">
            <h2 className="font-display text-2xl font-semibold text-stone-900">Sign in to the advisory console</h2>
            <p className="text-sm text-stone-600">For agronomists, dealers and co-op administrators. Farmers use the phone app.</p>
          </header>

          <form noValidate className="rise rise-3 space-y-4" onSubmit={handleSubmit((values) => mutation.mutate(values))}>
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
        </div>
      </section>
    </main>
  )
}
