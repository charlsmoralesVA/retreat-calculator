import { createContext, useContext, type ReactNode } from 'react'
import type { SupabaseClient } from '@supabase/supabase-js'

const BackendContext = createContext<SupabaseClient | null>(null)

export function BackendProvider({ client, children }: { client: SupabaseClient; children: ReactNode }) {
  return <BackendContext.Provider value={client}>{children}</BackendContext.Provider>
}

export function useClient(): SupabaseClient {
  const client = useContext(BackendContext)
  if (!client) throw new Error('useClient must be used inside <BackendProvider>')
  return client
}
