import '@testing-library/jest-dom/vitest'
import { configure } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { installAuthProvider } from '@/features/auth/api'
import { useAuthStore } from '@/features/auth/auth-store'
import { server } from './msw'

// findBy… and waitFor give up after 3 s, not the default 1 s: a full run on a busy machine (or a
// slow CI runner) renders some pages more slowly, and a test should fail on behaviour, not speed.
configure({ asyncUtilTimeout: 3000 })

// Any request without a handler is a test bug, not something to let through to a real server.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))

afterEach(() => {
  server.resetHandlers()
  useAuthStore.getState().clearSession()
  localStorage.clear()
})

afterAll(() => server.close())

installAuthProvider()

// jsdom has no object URLs; case photos are shown through them.
URL.createObjectURL ??= () => 'blob:test-photo'
URL.revokeObjectURL ??= () => {}

// jsdom cannot draw on a canvas either (it only warns). The spotlight's mask then stays hidden,
// which is what a test needs: the page underneath is what gets checked.
HTMLCanvasElement.prototype.getContext = (() => null) as typeof HTMLCanvasElement.prototype.getContext
