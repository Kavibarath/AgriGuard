/** `?a=1&b=2` from a query object, dropping empty values so the URL carries only what was chosen. */
export function queryString(query: object): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') continue
    params.set(key, String(value))
  }
  const serialised = params.toString()
  return serialised ? `?${serialised}` : ''
}
