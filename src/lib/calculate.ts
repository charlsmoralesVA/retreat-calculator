export interface BudgetInputs {
  headcount: number
  nights: number
  lodgingRate: number // per person per night
  foodRate: number // per person per day
  venueFee: number // fixed
  activities: number // per person
  travel: number // per person
  contingencyPct: number // percent, e.g. 10 for 10%
}

export type InputField = keyof BudgetInputs

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
  invalidFields: InputField[]
}

export const DEFAULT_INPUTS: BudgetInputs = {
  headcount: 0,
  nights: 0,
  lodgingRate: 0,
  foodRate: 0,
  venueFee: 0,
  activities: 0,
  travel: 0,
  contingencyPct: 0,
}

export const INPUT_FIELDS = Object.keys(DEFAULT_INPUTS) as InputField[]

// Food is charged per day; a retreat of N nights spans N + 1 days (arrival and departure).
export const foodDays = (nights: number): number => nights + 1

function isInvalid(field: InputField, value: number): boolean {
  if (!Number.isFinite(value) || value < 0) return true
  if (field === 'headcount' && !Number.isInteger(value)) return true
  return false
}

export function calculateBudget(inputs: BudgetInputs): BudgetResult {
  const invalidFields = INPUT_FIELDS.filter((f) => isInvalid(f, inputs[f]))
  // Invalid fields are excluded from the totals until corrected.
  const v = (f: InputField): number => (invalidFields.includes(f) ? 0 : inputs[f])

  const headcount = v('headcount')
  const lodging = headcount * v('nights') * v('lodgingRate')
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
    invalidFields,
  }
}
