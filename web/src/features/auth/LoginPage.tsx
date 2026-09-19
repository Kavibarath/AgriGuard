import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { z } from 'zod'
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

export function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const setSession = useAuthStore((s) => s.setSession)
  const isAuthenticated = useIsAuthenticated()

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
    <main className="flex min-h-screen items-center justify-center p-4">
      <div className="w-full max-w-sm space-y-6 rounded-lg border border-stone-200 bg-white p-6 shadow-sm">
        <header className="space-y-1">
          <h1 className="text-xl font-semibold text-brand-700">AgriGuard</h1>
          <p className="text-sm text-stone-600">Sign in to the advisory console</p>
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

          <Button type="submit" className="w-full" loading={mutation.isPending}>
            Sign in
          </Button>
        </form>
      </div>
    </main>
  )
}
