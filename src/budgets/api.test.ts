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
  lodgingMode: 'perPerson',
  roomOccupancy: 0,
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

describe('stored lodging mode', () => {
  const room: BudgetInputs = { ...full, lodgingMode: 'perRoom', lodgingRate: 180, roomOccupancy: 2 }

  it('round-trips per-room mode with its people per room', () => {
    expect(parseInputs(serializeInputs(room))).toEqual(room)
    expect(serializeInputs(room)).toMatchObject({ version: 1, lodgingMode: 'perRoom', roomOccupancy: 2 })
  })

  it('round-trips per-person mode', () => {
    expect(parseInputs(serializeInputs(full))).toEqual(full)
  })

  it('parses a budget stored with no lodging mode as per person, with everything else intact', () => {
    const { lodgingMode: _m, roomOccupancy: _o, ...legacy } = serializeInputs(full) as Record<string, unknown>
    expect(legacy).not.toHaveProperty('lodgingMode')
    expect(parseInputs(legacy)).toEqual({ ...full, lodgingMode: 'perPerson', roomOccupancy: 0 })
  })

  it.each(['room', 'PERROOM', 'perperson', '', 5, null, undefined, true, {}])(
    'treats an unrecognised stored mode (%j) as per person',
    (lodgingMode) => {
      const parsed = parseInputs({ ...serializeInputs(room), lodgingMode })
      expect(parsed.lodgingMode).toBe('perPerson')
      expect(parsed.lodgingRate).toBe(180) // other inputs are untouched
    },
  )

  it('writes the explicit per-person mode, at version 1, when a legacy budget is saved again', () => {
    const resaved = serializeInputs(parseInputs({ version: 1, headcount: 4, lodgingRate: 90 }))
    expect(resaved).toMatchObject({ version: 1, lodgingMode: 'perPerson', headcount: 4, lodgingRate: 90 })
  })
})

