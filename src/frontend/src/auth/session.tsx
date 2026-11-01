import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, call, sessionStore, setUnauthorizedHandler, type Schemas, type Scope } from '../api/client'

interface SessionValue {
  scope: Scope | null
  me: Schemas['MeDto'] | null
  loading: boolean
  can: (permission: string) => boolean
  signIn: (scope: Scope, token: string, expiresAt: string) => Promise<void>
  signOut: () => void
}

const SessionContext = createContext<SessionValue | null>(null)

export function SessionProvider({ children }: { children: ReactNode }) {
  const [scope, setScope] = useState<Scope | null>(() => sessionStore.get()?.scope ?? null)
  const [me, setMe] = useState<Schemas['MeDto'] | null>(null)
  const [loading, setLoading] = useState(true)

  const signOut = useCallback(() => {
    sessionStore.clear()
    setScope(null)
    setMe(null)
  }, [])

  const loadMe = useCallback(async (s: Scope | null) => {
    if (s !== 'tenant') {
      setMe(null)
      setLoading(false)
      return
    }
    try {
      setMe(await call(api.GET('/api/v1/me')))
    } catch {
      signOut()
    } finally {
      setLoading(false)
    }
  }, [signOut])

  useEffect(() => {
    setUnauthorizedHandler(signOut)
    void loadMe(scope)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const signIn = useCallback(async (s: Scope, token: string, expiresAt: string) => {
    sessionStore.set({ scope: s, token, expiresAt })
    setScope(s)
    setLoading(true)
    await loadMe(s)
  }, [loadMe])

  const value = useMemo<SessionValue>(() => ({
    scope,
    me,
    loading,
    can: (p) => !!me?.permissions.includes(p),
    signIn,
    signOut,
  }), [scope, me, loading, signIn, signOut])

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}

export function useSession() {
  const ctx = useContext(SessionContext)
  if (!ctx) throw new Error('useSession must be used inside SessionProvider')
  return ctx
}
