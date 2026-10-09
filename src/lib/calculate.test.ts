import { calculateBudget, DEFAULT_INPUTS, type BudgetInputs } from './calculate'

const worked: BudgetInputs = {
  headcount: 10,
  nights: 3,
  lodgingRate: 100,
  foodRate: 50,
  venueFee: 1000,
  activities: 40,
  travel: 80,
  contingencyPct: 10,
}

describe('calculateBudget', () => {
  it('returns zeros for the default inputs', () => {
    const r = calculateBudget(DEFAULT_INPUTS)
    expect(r.subtotal).toBe(0)
    expect(r.total).toBe(0)
    expect(r.invalidFields).toEqual([])
  })

  it('matches the worked example from the spec', () => {
    const r = calculateBudget(worked)
    expect(r.lodging).toBe(3000)
    expect(r.food).toBe(2000)
    expect(r.venue).toBe(1000)
    expect(r.activities).toBe(400)
    expect(r.travel).toBe(800)
    expect(r.subtotal).toBe(7200)
    expect(r.contingency).toBe(720)
    expect(r.total).toBe(7920)
    expect(r.perPerson).toBe(792)
    expect(r.invalidFields).toEqual([])
  })

  it('reports per-person cost as null for zero headcount', () => {
    const r = calculateBudget({ ...worked, headcount: 0 })
    expect(r.perPerson).toBeNull()
    expect(r.total).toBe(1000 + 100) // venue fee plus 10% contingency
  })

  it('flags a negative number and excludes it from totals', () => {
    const r = calculateBudget({ ...worked, travel: -80 })
    expect(r.invalidFields).toEqual(['travel'])
    expect(r.travel).toBe(0)
    expect(r.subtotal).toBe(6400)
  })

  it('flags a non-integer headcount and treats it as zero', () => {
    const r = calculateBudget({ ...worked, headcount: 2.5 })
    expect(r.invalidFields).toEqual(['headcount'])
    expect(r.perPerson).toBeNull()
    expect(r.lodging).toBe(0)
  })

  it('flags NaN and infinite values', () => {
    const r = calculateBudget({ ...worked, foodRate: NaN, venueFee: Infinity })
    expect(r.invalidFields).toEqual(['foodRate', 'venueFee'])
    expect(Number.isFinite(r.total)).toBe(true)
  })

  it('charges one day of food for a zero-night retreat', () => {
    const r = calculateBudget({ ...DEFAULT_INPUTS, headcount: 4, foodRate: 25 })
    expect(r.food).toBe(100)
  })
})
