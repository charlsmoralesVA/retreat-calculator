import type { BudgetInput } from './types';

export type ValidatedField =
  | 'attendees'
  | 'nights'
  | 'occupancy'
  | 'venueAmount'
  | 'transportationAmount'
  | 'staff'
  | 'miscellaneous'
  | 'contingencyPct'
  | 'activities';

export type ValidationErrors = Partial<Record<ValidatedField, string>>;

export type ValidationResult = { ok: true } | { ok: false; errors: ValidationErrors };

const isWholeNumberAtLeast1 = (n: number): boolean => Number.isInteger(n) && n >= 1;
const isNonNegative = (n: number): boolean => Number.isFinite(n) && n >= 0;

export function validate(input: BudgetInput): ValidationResult {
  const errors: ValidationErrors = {};

  if (!Number.isFinite(input.attendees) || input.attendees < 1) {
    errors.attendees = 'At least one attendee is required.';
  } else if (!Number.isInteger(input.attendees)) {
    errors.attendees = 'Attendees must be a whole number.';
  }

  if (!Number.isFinite(input.nights) || input.nights < 1) {
    errors.nights = 'At least one night is required.';
  } else if (!Number.isInteger(input.nights)) {
    errors.nights = 'Nights must be a whole number.';
  }

  if (input.lodgingMode === 'perRoom' && !isWholeNumberAtLeast1(input.occupancy)) {
    errors.occupancy = 'Occupancy per room must be a whole number of at least 1.';
  }

  const amounts: [ValidatedField, number, string][] = [
    ['venueAmount', input.venueAmount, 'Venue amount'],
    ['transportationAmount', input.transportationAmount, 'Transportation amount'],
    ['staff', input.staff, 'Staff fees'],
    ['miscellaneous', input.miscellaneous, 'Miscellaneous'],
  ];
  for (const [field, value, label] of amounts) {
    if (!isNonNegative(value)) errors[field] = `${label} must be zero or more.`;
  }

  if (
    !Number.isFinite(input.contingencyPct) ||
    input.contingencyPct < 0 ||
    input.contingencyPct > 100
  ) {
    errors.contingencyPct = 'Contingency must be between 0 and 100.';
  }

  if (input.activities.some((a) => !isNonNegative(a.price))) {
    errors.activities = 'Activity prices must be zero or more.';
  }

  return Object.keys(errors).length === 0 ? { ok: true } : { ok: false, errors };
}
