import type { Session, SupabaseClient } from '@supabase/supabase-js'

// In-memory stand-in for the Supabase client, covering exactly the calls the app makes.
// It mimics row-level security by scoping every budgets query to the signed-in user.

interface Row {
  id: string
  user_id: string
  name: string
  inputs: unknown
  updated_at: string
}
type Result = { data: unknown; error: { message: string; code?: string } | null }
type AuthCb = (event: string, session: Session | null) => void

export interface FakeBackend {
  client: SupabaseClient
  rows: Row[]
  /** Make the next `count` budgets requests fail, like an unreachable backend. */
  failNext: (count?: number) => void
  confirmEmail: (email: string) => void
  /** Create an account directly, optionally signed in. */
  addUser: (email: string, password: string, opts?: { confirmed?: boolean; signedIn?: boolean }) => string
  signUpCalls: Array<{ email: string; password: string }>
}

export function createFakeBackend(): FakeBackend {
  const users = new Map<string, { id: string; password: string; confirmed: boolean }>()
  const rows: Row[] = []
  const listeners = new Set<AuthCb>()
  const signUpCalls: Array<{ email: string; password: string }> = []
  let session: Session | null = null
  let failures = 0
  let clock = 0
  let idCounter = 0

  const nowIso = () => new Date(Date.UTC(2026, 0, 1, 0, 0, ++clock)).toISOString()
  const setSession = (next: Session | null, event: string) => {
    session = next
    listeners.forEach((cb) => cb(event, next))
  }
  const makeSession = (email: string, id: string) =>
    ({ user: { id, email }, access_token: 't' }) as unknown as Session

  class Query {
    private op: 'select' | 'insert' | 'update' | 'delete' = 'select'
    private payload: Record<string, unknown> = {}
    private filters: Array<[string, unknown]> = []
    private orderBy: { column: string; ascending: boolean } | null = null
    private single_ = false
    private returning = false

    select() {
      if (this.op !== 'select') this.returning = true
      return this
    }
    insert(payload: Record<string, unknown>) {
      this.op = 'insert'
      this.payload = payload
      return this
    }
    update(payload: Record<string, unknown>) {
      this.op = 'update'
      this.payload = payload
      return this
    }
    delete() {
      this.op = 'delete'
      return this
    }
    eq(column: string, value: unknown) {
      this.filters.push([column, value])
      return this
    }
    order(column: string, opts?: { ascending?: boolean }) {
      this.orderBy = { column, ascending: opts?.ascending ?? true }
      return this
    }
    single() {
      this.single_ = true
      return this
    }
    then<T>(resolve: (r: Result) => T, reject?: (e: unknown) => T) {
      return Promise.resolve(this.exec()).then(resolve, reject)
    }

    private exec(): Result {
      if (failures > 0) {
        failures--
        return { data: null, error: { message: 'Failed to fetch' } }
      }
      const uid = session?.user.id
      if (!uid) return { data: null, error: { message: 'permission denied for table budgets', code: '42501' } }

      const visible = (r: Row) => r.user_id === uid && this.filters.every(([c, v]) => (r as never)[c] === v)

      if (this.op === 'insert') {
        const row: Row = {
          id: `budget-${++idCounter}`,
          user_id: uid,
          name: String(this.payload.name),
          inputs: this.payload.inputs,
          updated_at: nowIso(),
        }
        rows.push(row)
        return { data: this.returning ? this.shape(row) : null, error: null }
      }
      if (this.op === 'update') {
        const hit = rows.filter(visible)
        hit.forEach((r) => Object.assign(r, this.payload, { updated_at: nowIso() }))
        return { data: this.returning ? this.shapeMany(hit) : null, error: null }
      }
      if (this.op === 'delete') {
        rows.filter(visible).forEach((r) => rows.splice(rows.indexOf(r), 1))
        return { data: null, error: null }
      }
      let out = rows.filter(visible)
      if (this.orderBy) {
        const { column, ascending } = this.orderBy
        out = [...out].sort((a, b) => {
          const x = String((a as never)[column])
          const y = String((b as never)[column])
          return ascending ? x.localeCompare(y) : y.localeCompare(x)
        })
      }
      return { data: this.shapeMany(out), error: null }
    }

    private shape(r: Row) {
      const { user_id: _u, ...rest } = r
      return rest
    }
    private shapeMany(rs: Row[]) {
      if (this.single_) return rs.length ? this.shape(rs[0]) : null
      return rs.map((r) => this.shape(r))
    }
  }
  const auth = {
    getSession: async () => ({ data: { session }, error: null }),
    onAuthStateChange: (cb: AuthCb) => {
      listeners.add(cb)
      return { data: { subscription: { unsubscribe: () => listeners.delete(cb) } } }
    },
    signUp: async ({ email, password }: { email: string; password: string }) => {
      signUpCalls.push({ email, password })
      if (password.length < 8) return { data: {}, error: { message: 'weak', code: 'weak_password' } }
      // Like the real service, an existing email returns success and creates nothing.
      if (!users.has(email)) users.set(email, { id: `user-${users.size + 1}`, password, confirmed: false })
      return { data: {}, error: null }
    },
    signInWithPassword: async ({ email, password }: { email: string; password: string }) => {
      const u = users.get(email)
      if (!u || u.password !== password) {
        return { data: {}, error: { message: 'Invalid login credentials', code: 'invalid_credentials' } }
      }
      if (!u.confirmed) return { data: {}, error: { message: 'Email not confirmed', code: 'email_not_confirmed' } }
      setSession(makeSession(email, u.id), 'SIGNED_IN')
      return { data: { session }, error: null }
    },
    signOut: async () => {
      setSession(null, 'SIGNED_OUT')
      return { error: null }
    },
  }

  const client = { auth, from: () => new Query() } as unknown as SupabaseClient

  return {
    client,
    rows,
    signUpCalls,
    failNext: (count = 1) => {
      failures = count
    },
    confirmEmail: (email) => {
      const u = users.get(email)
      if (u) u.confirmed = true
    },
    addUser: (email, password, opts = {}) => {
      const id = `user-${users.size + 1}`
      users.set(email, { id, password, confirmed: opts.confirmed ?? true })
      if (opts.signedIn) setSession(makeSession(email, id), 'SIGNED_IN')
      return id
    },
  }
}
