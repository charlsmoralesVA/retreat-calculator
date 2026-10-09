import { calculateBudget, DEFAULT_INPUTS, toCents, type BudgetInputs } from './calculate'

const worked: BudgetInputs = {
  headcount: 10,
  nights: 3,
  lodgingRate: 100,
  foodRate: 50,
  venueFee: 1000,
  activities: 40,
  travel: 80,
  contingencyPct: 10,
  markupPct: 0,
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

describe('markup validation', () => {
  it.each([-1, NaN, Infinity])('flags markup %s and treats it as no markup', (markupPct) => {
    const r = calculateBudget({ ...worked, markupPct })
    expect(r.invalidFields).toEqual(['markupPct'])
    expect(r.clientPrice).toBeNull()
  })

  it('accepts a markup above 100%', () => {
    const r = calculateBudget({ ...worked, markupPct: 250 })
    expect(r.invalidFields).toEqual([])
    expect(r.clientPrice?.total).toBe(27720) // 7,920 x 3.5
  })
})

describe('toCents', () => {
  it.each([
    [1.005, 101], // 1.005 * 100 is 100.49999999999999 in binary floating point
    [2.675, 268],
    [0.285, 29],
    [1.004, 100],
    [1.006, 101],
    [0, 0],
    [1785.7142857142858, 178571],
  ])('rounds %s dollars to %s cents (halves up)', (dollars, cents) => {
    expect(toCents(dollars)).toBe(cents)
  })
})

describe('client price', () => {
  it('worked example without rounding loss', () => {
    const r = calculateBudget({ ...worked, markupPct: 25 })
    expect(r.total).toBe(7920)
    expect(r.clientPrice).toEqual({ perPerson: 990, total: 9900, marginAmount: 1980, marginPct: 20 })
  })

  it('worked example with rounding: 10,000 over 7 attendees at 25%', () => {
    // Venue fee is the only cost, so total cost is exactly 10,000.
    const r = calculateBudget({ ...DEFAULT_INPUTS, headcount: 7, venueFee: 10000, markupPct: 25 })
    expect(r.total).toBe(10000)
    expect(r.clientPrice?.perPerson).toBe(1785.71)
    expect(r.clientPrice?.total).toBe(12499.97)
    expect(r.clientPrice?.marginAmount).toBeCloseTo(2499.97, 10)
    expect(r.clientPrice?.marginPct).toBeCloseTo(20, 2)
    expect(r.clientPrice!.marginPct!.toFixed(1)).toBe('20.0')
  })

  it('is null at zero markup and when markup is unset', () => {
    expect(calculateBudget(worked).clientPrice).toBeNull()
    expect(calculateBudget(DEFAULT_INPUTS).clientPrice).toBeNull()
  })

  it('gives null figures at zero headcount', () => {
    const r = calculateBudget({ ...worked, headcount: 0, markupPct: 25 })
    expect(r.clientPrice).toEqual({ perPerson: null, total: null, marginAmount: null, marginPct: null })
  })

  it('gives null figures for an invalid (fractional) headcount', () => {
    const r = calculateBudget({ ...worked, headcount: 2.5, markupPct: 25 })
    expect(r.invalidFields).toEqual(['headcount'])
    expect(r.clientPrice).toEqual({ perPerson: null, total: null, marginAmount: null, marginPct: null })
  })

  it('has no margin percent when the quoted total is zero', () => {
    const r = calculateBudget({ ...DEFAULT_INPUTS, headcount: 5, markupPct: 25 })
    expect(r.clientPrice).toEqual({ perPerson: 0, total: 0, marginAmount: 0, marginPct: null })
  })

  it('leaves every cost figure unchanged by markup', () => {
    const without = calculateBudget(worked)
    const withMarkup = calculateBudget({ ...worked, markupPct: 40 })
    const { clientPrice: _a, ...a } = without
    const { clientPrice: _b, ...b } = withMarkup
    expect(b).toEqual(a)
  })

  it('computes from the total cost including contingency', () => {
    const noContingency = calculateBudget({ ...worked, contingencyPct: 0, markupPct: 25 })
    const withContingency = calculateBudget({ ...worked, contingencyPct: 10, markupPct: 25 })
    expect(noContingency.clientPrice?.total).toBe(9000) // 7,200 x 1.25
    expect(withContingency.clientPrice?.total).toBe(9900) // 7,920 x 1.25
  })

  it('rounds an exact half cent up', () => {
    // 0.01 cost x 1.5 / 3 people = 0.005 dollars each, which must round up to one cent.
    const r = calculateBudget({ ...DEFAULT_INPUTS, headcount: 3, venueFee: 0.01, markupPct: 50 })
    expect(r.clientPrice?.perPerson).toBe(0.01)
    expect(r.clientPrice?.total).toBe(0.03)
  })

  it('per-person price times headcount always equals the quoted total, in whole cents', () => {
    for (const headcount of [1, 2, 3, 7, 11, 13, 24, 99]) {
      for (const venueFee of [0.01, 99.99, 1234.56, 10000, 98765.43]) {
        for (const markupPct of [1, 7.5, 12.345, 25, 100, 333]) {
          const c = calculateBudget({ ...DEFAULT_INPUTS, headcount, venueFee, markupPct }).clientPrice!
          expect(Math.round(c.perPerson! * 100) * headcount).toBe(Math.round(c.total! * 100))
          // And the rounded price is within half a cent of the exact marked-up price.
          const exact = (venueFee * (1 + markupPct / 100)) / headcount
          expect(Math.abs(c.perPerson! - exact)).toBeLessThanOrEqual(0.005 + 1e-9)
        }
      }
    }
  })
})
