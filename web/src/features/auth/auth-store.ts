import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { AuthResponse, UserSummary } from './types'

/**
 * Client-side session state (Zustand, per ADR 1: server data lives in TanStack Query, this is
 * the small amount of genuinely client-owned state).
 *
 * Persisted to localStorage so a page reload keeps the user signed in. The access token is
 * short-lived and the refresh token is revocable server-side, which bounds the damage if
 * storage is ever read by injected script.
 */
export interface AuthState {
  accessToken: string | null
  refreshToken: string | null
  user: UserSummary | null
  setSession: (response: AuthResponse) => void
  clearSession: () => void
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      refreshToken: null,
      user: null,
      setSession: ({ accessToken, refreshToken, user }) => set({ accessToken, refreshToken, user }),
      clearSession: () => set({ accessToken: null, refreshToken: null, user: null }),
    }),
    {
      name: 'agriguard.auth',
      partialize: ({ accessToken, refreshToken, user }) => ({ accessToken, refreshToken, user }),
    },
  ),
)

/** Convenience selectors — subscribe to one field so unrelated updates don't re-render. */
export const useCurrentUser = () => useAuthStore((s) => s.user)
export const useIsAuthenticated = () => useAuthStore((s) => s.accessToken !== null)
