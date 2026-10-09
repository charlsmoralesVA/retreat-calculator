export type LodgingMode = 'perPerson' | 'perRoom'

export interface BudgetInputs {
  headcount: number
  nights: number
  lodgingMode: LodgingMode // how lodgingRate is charged
  lodgingRate: number // per person per night, or per room per night in perRoom mode
  roomOccupancy: number // people per room; only used (and validated) in perRoom mode
  foodRate: number // per person per day
  venueFee: number // fixed
  activities: number // per person
  travel: number // per person
  contingencyPct: number // percent, e.g. 10 for 10%
  markupPct: number // percent on total cost; 0 means no client price
}

/** The numeric inputs, in form order. Everything that loops over inputs uses this list. */
export const INPUT_FIELDS = [
  'headcount',
  'nights',
  'lodgingRate',
  'roomOccupancy',
  'foodRate',
  'venueFee',
  'activities',
  'travel',
  'contingencyPct',
  'markupPct',
] as const

export type InputField = (typeof INPUT_FIELDS)[number]

// Compile-time guard: adding a numeric input to BudgetInputs without listing it above is an error.
type NumericKey = { [K in keyof BudgetInputs]: BudgetInputs[K] extends number ? K : never }[keyof BudgetInputs]
const _everyNumericInputIsListed: [Exclude<NumericKey, InputField>] extends [never] ? true : never = true
void _everyNumericInputIsListed

/** What the client is quoted. Fields are null when they cannot be computed (no headcount). */
export interface ClientPrice {
  perPerson: number | null // rounded to the cent
  total: number | null // perPerson x headcount, so the quote multiplies back exactly
  marginAmount: number | null // quoted total minus total cost
  marginPct: number | null // margin amount as a percent of the quoted total
}

export interface BudgetResult {
  lodging: number
  food: number
  venue: number
  activities: number
  travel: number
  subtotal: number
  contingency: number
  total: number
  perPerson: number | null // null when headcount is zero or invalid
  roomsNeeded: number | null // null unless lodging is per room with a valid people-per-room value
  clientPrice: ClientPrice | null // null unless markup is above zero
  invalidFields: InputField[]
}

export const DEFAULT_INPUTS: BudgetInputs = {
  headcount: 0,
  nights: 0,
  lodgingMode: 'perPerson',
  lodgingRate: 0,
  roomOccupancy: 0,
  foodRate: 0,
  venueFee: 0,
  activities: 0,
  travel: 0,
  contingencyPct: 0,
  markupPct: 0,
}

// Food is charged per day; a retreat of N nights spans N + 1 days (arrival and departure).
export const foodDays = (nights: number): number => nights + 1

function isInvalid(field: InputField, value: number, mode: LodgingMode): boolean {
  if (field === 'roomOccupancy') {
    // Only meaningful when pricing per room; a stale value must never raise a hidden error.
    return mode === 'perRoom' && (!Number.isInteger(value) || value < 1)
  }
  if (!Number.isFinite(value) || value < 0) return true
  if (field === 'headcount' && !Number.isInteger(value)) return true
  return false
}

/**
 * Dollars to whole cents, with exact halves rounding up. Binary floats make values like
 * 1.005 * 100 come out as 100.49999999999999, so normalise that noise before rounding.
 */
export const toCents = (dollars: number): number => Math.round(Number((dollars * 100).toPrecision(12)))

function calculateClientPrice(total: number, markupPct: number, headcount: number): ClientPrice | null {
  if (markupPct <= 0) return null
  if (headcount <= 0) return { perPerson: null, total: null, marginAmount: null, marginPct: null }

  // Round the per-person price first; the quoted total is defined from that rounded price.
  const perPersonCents = toCents(((total * (100 + markupPct)) / 100) / headcount)
  const quotedCents = perPersonCents * headcount
  const quoted = quotedCents / 100
  const marginAmount = quoted - total
  return {
    perPerson: perPersonCents / 100,
    total: quoted,
    marginAmount,
    marginPct: quotedCents > 0 ? (marginAmount / quoted) * 100 : null,
  }
}

export function calculateBudget(inputs: BudgetInputs): BudgetResult {
  const perRoom = inputs.lodgingMode === 'perRoom'
  const invalidFields = INPUT_FIELDS.filter((f) => isInvalid(f, inputs[f], inputs.lodgingMode))
  // Invalid fields are excluded from the totals until corrected.
  const v = (f: InputField): number => (invalidFields.includes(f) ? 0 : inputs[f])

  const headcount = v('headcount')
  // A partly filled room is charged in full, so rooms are rounded up.
  const occupancy = v('roomOccupancy')
  const roomsNeeded = perRoom && occupancy >= 1 ? Math.ceil(headcount / occupancy) : null
  const lodgingUnits = perRoom ? (roomsNeeded ?? 0) : headcount
  const lodging = lodgingUnits * v('nights') * v('lodgingRate')
  const food = headcount * foodDays(v('nights')) * v('foodRate')
  const venue = v('venueFee')
  const activities = headcount * v('activities')
  const travel = headcount * v('travel')
  const subtotal = lodging + food + venue + activities + travel
  const contingency = subtotal * (v('contingencyPct') / 100)
  const total = subtotal + contingency

  return {
    lodging,
    food,
    venue,
    activities,
    travel,
    subtotal,
    contingency,
    total,
    perPerson: headcount > 0 ? total / headcount : null,
    roomsNeeded,
    clientPrice: calculateClientPrice(total, v('markupPct'), headcount),
    invalidFields,
  }
}
