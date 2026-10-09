import { DEFAULT_INPUTS, type BudgetInputs } from '../lib/calculate'
import { parseInputs, serializeInputs } from './api'

const full: BudgetInputs = {
  headcount: 10,
  nights: 3,
  lodgingRate: 100,
  foodRate: 50,
  venueFee: 1000,
  activities: 40,
  travel: 80,
  contingencyPct: 10,
  markupPct: 25,
}

describe('stored inputs', () => {
  it('round-trips every input, including markup', () => {
    expect(parseInputs(serializeInputs(full))).toEqual(full)
  })

  it('keeps the stored version at 1 (the change is additive)', () => {
    expect(serializeInputs(full)).toMatchObject({ version: 1, markupPct: 25 })
  })

  it('parses a budget saved before markup existed with markup 0 and everything else intact', () => {
    const legacy = {
      version: 1,
      headcount: 10,
      nights: 3,
      lodgingRate: 100,
      foodRate: 50,
      venueFee: 1000,
      activities: 40,
      travel: 80,
      contingencyPct: 10,
    }
    expect(parseInputs(legacy)).toEqual({ ...full, markupPct: 0 })
  })

  it('writes an explicit markup of 0 when a legacy budget is saved again', () => {
    expect(serializeInputs(parseInputs({ version: 1, headcount: 4 }))).toMatchObject({ headcount: 4, markupPct: 0 })
  })

  it.each([null, undefined, 'text', 42, [], { markupPct: 'lots' }, { markupPct: NaN }])(
    'falls back to defaults for malformed stored data: %j',
    (raw) => {
      expect(parseInputs(raw)).toEqual(DEFAULT_INPUTS)
    },
  )
})
