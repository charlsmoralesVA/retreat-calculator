import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import type { Session, User } from '@supabase/supabase-js'
import { useClient } from '../lib/BackendContext'

interface AuthState {
  user: User | null
  loading: boolean
  /** Resolves to an error message to show the user, or null on success. */
  signUp: (email: string, password: string) => Promise<string | null>
  signIn: (email: string, password: string) => Promise<string | null>
  signOut: () => Promise<string | null>
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const client = useClient()
  const [session, setSession] = useState<Session | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let active = true
    // The client restores a persisted session; reloads keep the user signed in.
    client.auth.getSession().then(({ data }) => {
      if (!active) return
      setSession(data.session)
      setLoading(false)
    })
    const { data } = client.auth.onAuthStateChange((_event, next) => {
      if (!active) return
      setSession(next)
      setLoading(false)
    })
    return () => {
      active = false
      data.subscription.unsubscribe()
    }
  }, [client])

  const signUp = useCallback(
    async (email: string, password: string) => {
      const { error } = await client.auth.signUp({
        email: email.trim(),
        password,
        // Send the confirmation link back to wherever the app is running.
        options: { emailRedirectTo: window.location.origin },
      })
      // An already-registered email returns success without an error, so the caller shows the
      // same "check your email" message either way and never reveals whether an account exists.
      return error ? 'Could not create the account. Please try again.' : null
    },
    [client],
  )

  const signIn = useCallback(
    async (email: string, password: string) => {
      const { error } = await client.auth.signInWithPassword({ email: email.trim(), password })
      if (!error) return null
      if (error.code === 'email_not_confirmed') {
        return 'Please confirm your email address before logging in. Check your inbox for the link.'
      }
      if (error.code === 'invalid_credentials') return 'Invalid email or password.'
      return 'Could not log in. Please try again.'
    },
    [client],
  )

  const signOut = useCallback(async () => {
    const { error } = await client.auth.signOut()
    return error ? 'Could not log out. Please try again.' : null
  }, [client])

  const value = useMemo<AuthState>(
    () => ({ user: session?.user ?? null, loading, signUp, signIn, signOut }),
    [session, loading, signUp, signIn, signOut],
  )
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}
