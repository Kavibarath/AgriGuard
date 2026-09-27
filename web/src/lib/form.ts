import { z } from 'zod'

/**
 * A number typed into an <input type="number">. An empty box is "missing", not zero:
 * z.coerce.number() would turn '' into 0 and let an unfilled quantity through.
 *
 * `rules` adds the range checks: amount('Enter a price.', (n) => n.min(0)).
 */
export const amount = (required: string, rules: (n: z.ZodNumber) => z.ZodNumber = (n) => n) =>
  z.preprocess(
    (value) => (value === '' || value === null || value === undefined ? undefined : Number(value)),
    rules(z.number({ message: required })),
  )
