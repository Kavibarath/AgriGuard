import { QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router'
import { useState } from 'react'
import { createQueryClient } from './query-client'
import { createAppRouter } from './router'

export function App() {
  const [queryClient] = useState(createQueryClient)
  const [router] = useState(createAppRouter)

  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
}
