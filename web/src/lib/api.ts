/**
 * Thin fetch wrapper for the AgriGuard API.
 *
 * - Prefixes VITE_API_BASE_URL, sends/receives JSON.
 * - Attaches the bearer token and, on a 401, refreshes it once and retries.
 * - Turns RFC 7807 problem responses into a typed ApiError so screens can show
 *   `detail`, map `errors` onto form fields, and switch on `code`.
 * - Stamps X-Correlation-Id so a failure can be traced in the API logs.
 */

export const API_BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5000').replace(/\/+$/, '')

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  /** Stable machine-readable identifier on 422 business-rule violations. */
  code?: string
  /** Field name → messages, on 400 validation failures. */
  errors?: Record<string, string[]>
  traceId?: string
  correlationId?: string
}

export class ApiError extends Error {
  readonly name = 'ApiError'
  readonly status: number
  readonly problem: ProblemDetails

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}`)
    this.status = status
    this.problem = problem
  }

  get fieldErrors(): Record<string, string[]> {
    return this.problem.errors ?? {}
  }
}

/** Thrown when the API cannot be reached at all (offline, CORS, DNS). */
export class NetworkError extends Error {
  readonly name = 'NetworkError'

  constructor(cause: unknown) {
    super('Could not reach the AgriGuard API. Check your connection.', { cause })
  }
}

/**
 * What to show a person for a failed call. 4xx problems carry a message written for the user;
 * 5xx `detail` is a stack trace in Development and empty in Production — never render it.
 */
export function userMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.status >= 500 ? 'The server had a problem. Try again in a moment.' : error.message
  }
  if (error instanceof NetworkError) return error.message
  return 'Something went wrong. Try again.'
}

export interface TokenProvider {
  getAccessToken(): string | null
  /** Obtain a fresh access token, or null if the session is gone. Must be single-flight. */
  refresh(): Promise<string | null>
}

let tokenProvider: TokenProvider = {
  getAccessToken: () => null,
  refresh: async () => null,
}

/** Wired once by the auth feature; kept as an indirection so `api` has no import of the store. */
export function configureApiAuth(provider: TokenProvider) {
  tokenProvider = provider
}

export interface RequestOptions extends Omit<RequestInit, 'body'> {
  /** Serialised as the JSON body. */
  json?: unknown
  /** Attach the bearer token and refresh on 401. Default true. */
  auth?: boolean
}

export async function api<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { json, auth = true, headers, ...init } = options

  const send = (accessToken: string | null) =>
    fetch(`${API_BASE_URL}${path}`, {
      ...init,
      headers: {
        Accept: 'application/json',
        'X-Correlation-Id': crypto.randomUUID().replaceAll('-', ''),
        ...(json !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
        ...headers,
      },
      body: json !== undefined ? JSON.stringify(json) : undefined,
    }).catch((cause: unknown) => {
      throw new NetworkError(cause)
    })

  let response = await send(auth ? tokenProvider.getAccessToken() : null)

  // Access tokens live 15 minutes; a 401 on an authenticated call most likely means "expired".
  // One refresh, one retry — a second 401 is a real rejection and is surfaced.
  if (response.status === 401 && auth) {
    const fresh = await tokenProvider.refresh()
    if (fresh) response = await send(fresh)
  }

  if (!response.ok) {
    const problem = await parseProblem(response)
    throw new ApiError(response.status, problem)
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

async function parseProblem(response: Response): Promise<ProblemDetails> {
  const text = await response.text()
  try {
    return text ? (JSON.parse(text) as ProblemDetails) : { status: response.status }
  } catch {
    return { status: response.status, title: response.statusText }
  }
}
