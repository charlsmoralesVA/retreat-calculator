import { createClient, type SupabaseClient } from '@supabase/supabase-js'

export function createSupabaseClient(url: unknown, anonKey: unknown): SupabaseClient {
  if (typeof url !== 'string' || url === '') {
    throw new Error('Missing VITE_SUPABASE_URL. Copy .env.example to .env.local and fill it in.')
  }
  if (typeof anonKey !== 'string' || anonKey === '') {
    throw new Error('Missing VITE_SUPABASE_ANON_KEY. Copy .env.example to .env.local and fill it in.')
  }
  return createClient(url, anonKey)
}
