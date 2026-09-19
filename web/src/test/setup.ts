import '@testing-library/jest-dom/vitest'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { installAuthProvider } from '@/features/auth/api'
import { useAuthStore } from '@/features/auth/auth-store'
import { server } from './msw'

// Any request without a handler is a test bug, not something to let through to a real server.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))

afterEach(() => {
  server.resetHandlers()
  useAuthStore.getState().clearSession()
  localStorage.clear()
})

afterAll(() => server.close())

installAuthProvider()
