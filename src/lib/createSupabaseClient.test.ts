import { createSupabaseClient } from './createSupabaseClient'

describe('createSupabaseClient', () => {
  it('throws a clear error when the URL is missing', () => {
    expect(() => createSupabaseClient(undefined, 'key')).toThrow(/VITE_SUPABASE_URL/)
  })

  it('throws a clear error when the anon key is missing', () => {
    expect(() => createSupabaseClient('http://127.0.0.1:54321', '')).toThrow(/VITE_SUPABASE_ANON_KEY/)
  })

  it('creates a client when both values are present', () => {
    expect(createSupabaseClient('http://127.0.0.1:54321', 'key')).toBeDefined()
  })
})
