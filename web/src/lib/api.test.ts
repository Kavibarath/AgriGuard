import { authResponse, farmer, http, HttpResponse, problem, server } from '@/test/msw'
import { useAuthStore } from '@/features/auth/auth-store'
import { me } from '@/features/auth/api'
import { api, API_BASE_URL, ApiError, NetworkError } from './api'

describe('api()', () => {
  it('refreshes once on 401 and retries with the new token', async () => {
    useAuthStore.getState().setSession({ ...authResponse(farmer), accessToken: 'expired' })
    let refreshCalls = 0
    server.use(
      http.post(`${API_BASE_URL}/api/auth/refresh`, () => {
        refreshCalls++
        return HttpResponse.json(authResponse(farmer, '-rotated'))
      }),
    )

    const user = await me()

    expect(user.email).toBe(farmer.email)
    expect(refreshCalls).toBe(1)
    expect(useAuthStore.getState().accessToken).toBe('access-Farmer-rotated')
  })

  it('collapses concurrent 401s into a single refresh', async () => {
    useAuthStore.getState().setSession({ ...authResponse(farmer), accessToken: 'expired' })
    let refreshCalls = 0
    server.use(
      http.post(`${API_BASE_URL}/api/auth/refresh`, async () => {
        refreshCalls++
        await new Promise((r) => setTimeout(r, 20))
        return HttpResponse.json(authResponse(farmer, '-rotated'))
      }),
    )

    await Promise.all([me(), me(), me()])

    // A second refresh with the same token would be treated as a replay by the API.
    expect(refreshCalls).toBe(1)
  })

  it('clears the session when the refresh token is rejected', async () => {
    useAuthStore.getState().setSession({ ...authResponse(farmer), accessToken: 'expired', refreshToken: 'revoked' })

    await expect(me()).rejects.toMatchObject({ status: 401 })
    expect(useAuthStore.getState().user).toBeNull()
  })

  it('does not attempt a refresh for unauthenticated calls', async () => {
    let refreshCalls = 0
    server.use(http.post(`${API_BASE_URL}/api/auth/refresh`, () => {
      refreshCalls++
      return problem(401, 'x', 'x')
    }))

    await expect(api('/api/auth/login', { method: 'POST', json: { email: 'a@b.c', password: 'x' }, auth: false }))
      .rejects.toBeInstanceOf(ApiError)
    expect(refreshCalls).toBe(0)
  })

  it('exposes problem details, field errors and business-rule codes', async () => {
    server.use(
      http.get(`${API_BASE_URL}/api/thing`, () =>
        problem(422, 'Business rule violated', 'Cannot move from Sown to Harvested.', { code: 'ILLEGAL_STAGE_TRANSITION' }),
      ),
      http.post(`${API_BASE_URL}/api/thing`, () =>
        problem(400, 'Validation failed', 'One or more validation errors occurred.', { errors: { areaHectares: ['Must be greater than 0.'] } }),
      ),
    )

    const rule = await api('/api/thing', { auth: false }).catch((e: unknown) => e)
    expect(rule).toBeInstanceOf(ApiError)
    expect((rule as ApiError).problem.code).toBe('ILLEGAL_STAGE_TRANSITION')
    expect((rule as ApiError).message).toBe('Cannot move from Sown to Harvested.')

    const validation = await api('/api/thing', { method: 'POST', json: {}, auth: false }).catch((e: unknown) => e)
    expect((validation as ApiError).fieldErrors).toEqual({ areaHectares: ['Must be greater than 0.'] })
  })

  it('wraps network failures in a readable error', async () => {
    server.use(http.get(`${API_BASE_URL}/api/offline`, () => HttpResponse.error()))

    await expect(api('/api/offline', { auth: false })).rejects.toBeInstanceOf(NetworkError)
  })

  it('sends a correlation id and returns undefined for 204', async () => {
    let correlationId: string | null = null
    server.use(http.delete(`${API_BASE_URL}/api/thing/1`, ({ request }) => {
      correlationId = request.headers.get('X-Correlation-Id')
      return new HttpResponse(null, { status: 204 })
    }))

    await expect(api('/api/thing/1', { method: 'DELETE', auth: false })).resolves.toBeUndefined()
    expect(correlationId).toMatch(/^[0-9a-f]{32}$/)
  })
})
