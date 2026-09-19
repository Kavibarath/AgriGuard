import { api, configureApiAuth } from '@/lib/api'
import { useAuthStore } from './auth-store'
import type { AuthResponse, UserSummary } from './types'

export interface LoginRequest {
  email: string
  password: string
}

// `auth: false`: a 401 here means "wrong password", not "expired token" — never trigger a refresh.
export const login = (request: LoginRequest) =>
  api<AuthResponse>('/api/auth/login', { method: 'POST', json: request, auth: false })

export const refresh = (refreshToken: string) =>
  api<AuthResponse>('/api/auth/refresh', { method: 'POST', json: { refreshToken }, auth: false })

export const logout = (refreshToken: string) =>
  api<void>('/api/auth/logout', { method: 'POST', json: { refreshToken }, auth: false })

export const me = () => api<UserSummary>('/api/auth/me')

let inflightRefresh: Promise<string | null> | null = null

/**
 * Single-flight refresh: ten queries failing with 401 at once must produce ONE refresh call.
 * The second call with the same refresh token would be treated as a replay by the API and
 * revoke every session.
 */
export function refreshSession(): Promise<string | null> {
  if (inflightRefresh) return inflightRefresh

  inflightRefresh = (async () => {
    const { refreshToken, setSession, clearSession } = useAuthStore.getState()
    if (!refreshToken) return null
    try {
      const response = await refresh(refreshToken)
      setSession(response)
      return response.accessToken
    } catch {
      clearSession()
      return null
    } finally {
      inflightRefresh = null
    }
  })()

  return inflightRefresh
}

/** Ends the session on the server (best effort) and locally (always). */
export async function signOut(): Promise<void> {
  const { refreshToken, clearSession } = useAuthStore.getState()
  clearSession()
  if (refreshToken) await logout(refreshToken).catch(() => undefined)
}

/** Call once at startup so `api()` can read and refresh the token without importing the store. */
export function installAuthProvider() {
  configureApiAuth({
    getAccessToken: () => useAuthStore.getState().accessToken,
    refresh: refreshSession,
  })
}
