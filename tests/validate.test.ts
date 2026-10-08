import { describe, expect, it } from 'vitest';
import { validate } from '../src/calculator/validate';
import { baseInput } from './fixtures';

function errorsFor(overrides: Parameters<typeof baseInput>[0]) {
  const result = validate(baseInput(overrides));
  return result.ok ? {} : result.errors;
}

describe('validate', () => {
  it('accepts a valid input', () => {
    expect(validate(baseInput())).toEqual({ ok: true });
  });

  it('rejects zero attendees', () => {
    expect(errorsFor({ attendees: 0 }).attendees).toMatch(/at least one attendee/i);
  });

  it('rejects fractional attendees and non-numeric values', () => {
    expect(errorsFor({ attendees: 2.5 }).attendees).toMatch(/whole number/i);
    expect(errorsFor({ attendees: NaN }).attendees).toBeDefined();
  });

  it('rejects fractional and zero nights', () => {
    expect(errorsFor({ nights: 2.5 }).nights).toMatch(/whole number/i);
    expect(errorsFor({ nights: 0 }).nights).toBeDefined();
  });

  it.each(['venueAmount', 'transportationAmount', 'staff', 'miscellaneous'] as const)(
    'rejects a negative %s',
    (field) => {
      expect(errorsFor({ [field]: -1 })[field]).toMatch(/zero or more/i);
    },
  );

  it('accepts zero amounts', () => {
    expect(validate(baseInput({ staff: 0, miscellaneous: 0, venueAmount: 0 }))).toEqual({
      ok: true,
    });
  });

  it('ignores occupancy in per-person mode', () => {
    expect(validate(baseInput({ lodgingMode: 'perPerson', occupancy: NaN }))).toEqual({ ok: true });
  });

  it('requires valid occupancy in per-room mode', () => {
    expect(errorsFor({ lodgingMode: 'perRoom', occupancy: 0 }).occupancy).toBeDefined();
    expect(errorsFor({ lodgingMode: 'perRoom', occupancy: 1.5 }).occupancy).toBeDefined();
  });

  it('rejects contingency outside 0-100 and accepts the bounds', () => {
    expect(errorsFor({ contingencyPct: 120 }).contingencyPct).toBeDefined();
    expect(errorsFor({ contingencyPct: -1 }).contingencyPct).toBeDefined();
    expect(validate(baseInput({ contingencyPct: 0 }))).toEqual({ ok: true });
    expect(validate(baseInput({ contingencyPct: 100 }))).toEqual({ ok: true });
  });

  it('rejects a negative custom activity price', () => {
    const activities = [{ id: 'x', name: 'X', price: -5, perPerson: false }];
    expect(errorsFor({ activities }).activities).toBeDefined();
  });

  it('reports multiple errors together', () => {
    const errors = errorsFor({ attendees: 0, staff: -100 });
    expect(errors.attendees).toBeDefined();
    expect(errors.staff).toBeDefined();
  });

  it('accepts an empty retreat name', () => {
    expect(validate(baseInput({ name: '' }))).toEqual({ ok: true });
  });
});
