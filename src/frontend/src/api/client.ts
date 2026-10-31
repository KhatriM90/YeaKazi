import createClient, { type Middleware } from 'openapi-fetch'
import type { components, paths } from './schema'

export type Schemas = components['schemas']

/** Problem details returned by the API (RFC 7807 + stable code + correlation id). */
export interface ApiProblem {
  status?: number
  title?: string
  code?: string
  correlationId?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly problem: ApiProblem
  constructor(problem: ApiProblem) {
    super(problem.title ?? 'Request failed')
    this.problem = problem
  }
  get code() {
    return this.problem.code
  }
  fieldErrors(): Record<string, string> {
    const out: Record<string, string> = {}
    for (const [k, v] of Object.entries(this.problem.errors ?? {})) {
      out[k] = v.join(' ')
    }
    return out
  }
}

export type Scope = 'tenant' | 'platform'

const STORAGE_KEY = 'yeakazi.session'

export interface StoredSession {
  scope: Scope
  token: string
  expiresAt: string
}

/** Tokens live in sessionStorage only (cleared with the tab) and never in localStorage. */
export const sessionStore = {
  get(): StoredSession | null {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    try {
      const s = JSON.parse(raw) as StoredSession
      return new Date(s.expiresAt).getTime() > Date.now() ? s : null
    } catch {
      return null
    }
  },
  set(s: StoredSession) {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(s))
  },
  clear() {
    sessionStorage.removeItem(STORAGE_KEY)
  },
}

let onUnauthorized: () => void = () => {}
export function setUnauthorizedHandler(handler: () => void) {
  onUnauthorized = handler
}

const auth: Middleware = {
  onRequest({ request }) {
    const s = sessionStore.get()
    if (s) request.headers.set('Authorization', `Bearer ${s.token}`)
    request.headers.set('X-Correlation-Id', crypto.randomUUID().replaceAll('-', ''))
    return request
  },
  onResponse({ response }) {
    if (response.status === 401 && sessionStore.get()) {
      sessionStore.clear()
      onUnauthorized()
    }
    return response
  },
}

/** Typed client generated from docs/openapi/yeakazi-api.json (npm run gen:api). */
export const api = createClient<paths>({ baseUrl: '' })
api.use(auth)

/** Unwraps an openapi-fetch result, throwing ApiError for problem responses. */
export async function call<T>(promise: Promise<{ data?: T; error?: unknown; response: Response }>): Promise<T> {
  const { data, error, response } = await promise
  if (!response.ok) {
    const problem = (error ?? {}) as ApiProblem
    throw new ApiError({ status: response.status, title: problem.title ?? response.statusText, ...problem })
  }
  return data as T
}

export function messageFor(err: unknown): string {
  if (err instanceof ApiError) {
    const id = err.problem.correlationId ? ` (reference ${err.problem.correlationId.slice(0, 8)})` : ''
    if (err.problem.status === 403) return `You do not have permission for this action.${id}`
    return `${err.problem.title ?? 'Request failed'}${id}`
  }
  return 'Something went wrong. Check your connection and try again.'
}
