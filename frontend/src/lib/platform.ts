import { queryOptions } from '@tanstack/react-query'
import { z } from 'zod'

const healthSchema = z.object({ status: z.literal('healthy'), service: z.literal('embm-efiscal-api') })

export const healthQuery = queryOptions({
  queryKey: ['api-health'],
  queryFn: async ({ signal }) => {
    const response = await fetch('/api/health', { signal, cache: 'no-store' })
    if (!response.ok) throw new Error('The API is unavailable.')
    return healthSchema.parse(await response.json())
  },
  staleTime: 15_000,
  retry: false,
})
