import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { API_BASE_URL } from '@/lib/api'
import type { AuthResponse, UserSummary } from '@/features/auth/types'
import { registryHandlers } from './registry-handlers'

export const agronomist: UserSummary = {
  id: '01a0aaaf-a87a-736a-a322-4f4782a9b594',
  email: 'agronomist@agriguard.demo',
  fullName: 'Dr. Nimali Fernando',
  role: 'FieldAgronomist',
  districtId: '0199e7c2-3b1e-7f0a-9c1d-0d5f5a1c2b3e',
}

export const farmer: UserSummary = {
  id: '01a0aaaf-a87a-736a-a322-4f4782a9b595',
  email: 'farmer@agriguard.demo',
  fullName: 'Sunil Perera',
  role: 'Farmer',
  districtId: agronomist.districtId,
}

export const PASSWORD = 'AgriGuard!Demo1'

export const authResponse = (user: UserSummary, suffix = ''): AuthResponse => ({
  accessToken: `access-${user.role}${suffix}`,
  accessTokenExpiresAtUtc: new Date(Date.now() + 15 * 60_000).toISOString(),
  refreshToken: `refresh-${user.role}${suffix}`,
  refreshTokenExpiresAtUtc: new Date(Date.now() + 14 * 86_400_000).toISOString(),
  user,
})

const problem = (status: number, title: string, detail: string, extra: object = {}) =>
  HttpResponse.json({ status, title, detail, ...extra }, { status, headers: { 'Content-Type': 'application/problem+json' } })

/** Default handlers behave like the real API for the two demo accounts. */
export const handlers = [
  http.post(`${API_BASE_URL}/api/auth/login`, async ({ request }) => {
    const { email, password } = (await request.json()) as { email: string; password: string }
    const user = [agronomist, farmer].find((u) => u.email === email.toLowerCase())
    if (!user || password !== PASSWORD) return problem(401, 'Authentication failed', 'Invalid credentials.')
    return HttpResponse.json(authResponse(user))
  }),

  http.post(`${API_BASE_URL}/api/auth/refresh`, async ({ request }) => {
    const { refreshToken } = (await request.json()) as { refreshToken: string }
    const user = [agronomist, farmer].find((u) => refreshToken.startsWith(`refresh-${u.role}`))
    if (!user) return problem(401, 'Authentication failed', 'Invalid refresh token.')
    return HttpResponse.json(authResponse(user, '-rotated'))
  }),

  http.post(`${API_BASE_URL}/api/auth/logout`, () => new HttpResponse(null, { status: 204 })),

  http.get(`${API_BASE_URL}/api/auth/me`, ({ request }) => {
    const token = request.headers.get('Authorization')?.replace('Bearer ', '')
    const user = [agronomist, farmer].find((u) => token?.startsWith(`access-${u.role}`))
    return user ? HttpResponse.json(user) : problem(401, 'Unauthorized', '')
  }),
]

// Base handlers cover auth and the registry; a test overrides just what it is about with
// server.use(). They are part of the server's base set (not a server.use call at import time)
// so that resetHandlers() between tests restores them rather than removing them.
export const server = setupServer(...handlers, ...registryHandlers)
export { http, HttpResponse, problem }
