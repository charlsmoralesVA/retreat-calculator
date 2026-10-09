import type { SupabaseClient } from '@supabase/supabase-js'
import { DEFAULT_INPUTS, INPUT_FIELDS, type BudgetInputs } from '../lib/calculate'

export interface SavedBudget {
  id: string
  name: string
  inputs: BudgetInputs
  updatedAt: string
}

// Bump when the stored shape changes; old rows are normalised on load.
const INPUTS_VERSION = 1
const COLUMNS = 'id,name,inputs,updated_at'

export const serializeInputs = (inputs: BudgetInputs) => ({ version: INPUTS_VERSION, ...inputs })

/** Rebuild inputs from stored JSON, defaulting anything missing or malformed. */
export function parseInputs(raw: unknown): BudgetInputs {
  const src = (typeof raw === 'object' && raw !== null ? raw : {}) as Record<string, unknown>
  const out = { ...DEFAULT_INPUTS }
  for (const f of INPUT_FIELDS) {
    const v = src[f]
    if (typeof v === 'number' && Number.isFinite(v)) out[f] = v
  }
  // Only the exact value 'perRoom' selects per-room pricing; anything else (missing, malformed,
  // unknown) is per person, which is what budgets saved before lodging mode existed used.
  out.lodgingMode = src.lodgingMode === 'perRoom' ? 'perRoom' : 'perPerson'
  return out
}

interface Row {
  id: string
  name: string
  inputs: unknown
  updated_at: string
}

const toBudget = (r: Row): SavedBudget => ({
  id: r.id,
  name: r.name,
  inputs: parseInputs(r.inputs),
  updatedAt: r.updated_at,
})

function must<T>(data: T | null, error: { message: string } | null): T {
  if (error) throw new Error(error.message)
  if (data === null) throw new Error('No data returned')
  return data
}

export async function listBudgets(client: SupabaseClient): Promise<SavedBudget[]> {
  const { data, error } = await client.from('budgets').select(COLUMNS).order('updated_at', { ascending: false })
  return must<Row[]>(data, error).map(toBudget)
}

export async function createBudget(client: SupabaseClient, name: string, inputs: BudgetInputs) {
  const { data, error } = await client
    .from('budgets')
    .insert({ name, inputs: serializeInputs(inputs) })
    .select(COLUMNS)
    .single()
  return toBudget(must<Row>(data, error))
}

export async function updateBudget(
  client: SupabaseClient,
  id: string,
  patch: { name?: string; inputs?: BudgetInputs },
) {
  const body: Record<string, unknown> = {}
  if (patch.name !== undefined) body.name = patch.name
  if (patch.inputs !== undefined) body.inputs = serializeInputs(patch.inputs)
  const { data, error } = await client.from('budgets').update(body).eq('id', id).select(COLUMNS).single()
  return toBudget(must<Row>(data, error))
}

export async function deleteBudget(client: SupabaseClient, id: string) {
  const { error } = await client.from('budgets').delete().eq('id', id)
  if (error) throw new Error(error.message)
}
