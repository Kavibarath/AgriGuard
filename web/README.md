# AgriGuard web (React)

Back-office console for agronomists, dealers and administrators. Vite + React 19 + TypeScript,
React Router, TanStack Query (server state), Zustand (session), Tailwind, react-hook-form + zod.

```bash
npm ci
npm run dev        # http://localhost:5173, expects the API on http://localhost:5000
npm test           # Vitest + Testing Library + MSW (no API needed)
npm run typecheck
npm run lint
npm run build
```

Point at another API with `VITE_API_BASE_URL` (see `.env.example`). Demo accounts are in
`docs/handover/jwt-auth.md`.

## Layout

```
src/
  app/          App shell, router, query client
  components/   shared UI primitives (Button, Field, Alert, MessagePage)
  features/
    auth/       login page, session store, route guard, policy mirror
    dashboard/  role-aware landing page
  lib/          api() fetch wrapper, utils
  test/         MSW handlers and render helpers
```

Each feature owner adds `src/features/<area>/` with its pages, queries and tests.
