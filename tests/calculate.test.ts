import { describe, expect, it } from 'vitest';
import { calculateBudget } from '../src/calculator/calculate';
import { CATEGORIES, type BudgetInput, type Category } from '../src/calculator/types';
import { baseInput, testRateCard } from './fixtures';

function amountOf(overrides: Partial<BudgetInput>, category: Category): number {
  const result = calculateBudget(baseInput(overrides), testRateCard);
  return result.lineItems.find((l) => l.category === category)!.amount;
}

describe('line items', () => {
  it('reports all seven categories, zero when unused', () => {
    const result = calculateBudget(baseInput(), testRateCard);
    expect(result.lineItems.map((l) => l.category)).toEqual([...CATEGORIES]);
    expect(amountOf({}, 'staff')).toBe(0);
  });
});

describe('lodging', () => {
  it('prices per person', () => {
    expect(amountOf({ attendees: 10, nights: 3 }, 'lodging')).toBe(3000);
  });

  it('rounds rooms up in per-room mode', () => {
    expect(
      amountOf({ attendees: 7, nights: 2, lodgingMode: 'perRoom', occupancy: 2 }, 'lodging'),
    ).toBe(1600);
  });

  it('charges one room for a single attendee', () => {
    expect(
      amountOf({ attendees: 1, nights: 1, lodgingMode: 'perRoom', occupancy: 2 }, 'lodging'),
    ).toBe(200);
  });

  it('ignores occupancy in per-person mode', () => {
    expect(amountOf({ lodgingMode: 'perPerson', occupancy: NaN }, 'lodging')).toBe(3000);
  });
});

describe('venue', () => {
  it('charges a flat amount regardless of length', () => {
    expect(amountOf({ venueAmount: 2000, venueMode: 'flat', nights: 5 }, 'venue')).toBe(2000);
  });

  it('charges per day as nights + 1', () => {
    expect(amountOf({ venueAmount: 500, venueMode: 'perDay', nights: 3 }, 'venue')).toBe(2000);
  });
});

describe('meals', () => {
  it('includes arrival and departure days', () => {
    expect(amountOf({ attendees: 10, nights: 3 }, 'meals')).toBe(2400);
  });
});

describe('activities', () => {
  it('sums per-person and flat activities', () => {
    const activities = [
      { id: 'a', name: 'A', price: 50, perPerson: true },
      { id: 'b', name: 'B', price: 400, perPerson: false },
    ];
    expect(amountOf({ attendees: 10, activities }, 'activities')).toBe(900);
  });

  it('is zero with no activities', () => {
    expect(amountOf({ activities: [] }, 'activities')).toBe(0);
  });
});

describe('transportation, staff and miscellaneous', () => {
  it('multiplies per-person transportation by attendees', () => {
    expect(
      amountOf(
        { attendees: 10, transportationAmount: 75, transportationMode: 'perPerson' },
        'transportation',
      ),
    ).toBe(750);
  });

  it('keeps flat transportation, staff and miscellaneous as entered', () => {
    expect(amountOf({ transportationAmount: 300 }, 'transportation')).toBe(300);
    expect(amountOf({ staff: 1500 }, 'staff')).toBe(1500);
    expect(amountOf({ miscellaneous: 250 }, 'miscellaneous')).toBe(250);
  });
});

describe('totals', () => {
  // 3000 lodging + 2400 meals + 2000 venue + 2600 staff = 10000
  const tenThousand = { venueAmount: 2000, staff: 2600 };

  it('applies the 10% default contingency', () => {
    const r = calculateBudget(baseInput(tenThousand), testRateCard);
    expect(r.subtotal).toBe(10000);
    expect(r.contingencyAmount).toBe(1000);
    expect(r.total).toBe(11000);
    expect(r.costPerAttendee).toBe(1100);
  });

  it('has total equal to subtotal at 0% contingency', () => {
    const r = calculateBudget(baseInput({ ...tenThousand, contingencyPct: 0 }), testRateCard);
    expect(r.total).toBe(r.subtotal);
  });

  it('shares sum to 100 when the subtotal is positive', () => {
    const r = calculateBudget(baseInput(tenThousand), testRateCard);
    const sum = r.lineItems.reduce((s, l) => s + l.share, 0);
    expect(sum).toBeCloseTo(100, 10);
    expect(r.lineItems.find((l) => l.category === 'lodging')!.share).toBeCloseTo(30, 10);
  });

  it('gives zero shares and no NaN when everything is zero', () => {
    const r = calculateBudget(baseInput(), {
      ...testRateCard,
      mealsPerPersonPerDay: 0,
      lodgingPerPersonPerNight: 0,
    });
    expect(r.subtotal).toBe(0);
    expect(r.lineItems.every((l) => l.share === 0)).toBe(true);
    expect(r.total).toBe(0);
    expect(r.costPerAttendee).toBe(0);
  });
});

describe('precision and rate card', () => {
  it('keeps full precision (no rounding in the calculator)', () => {
    const r = calculateBudget(
      baseInput({ attendees: 3, nights: 1, contingencyPct: 0, venueAmount: 1000 }),
      { ...testRateCard, lodgingPerPersonPerNight: 0, mealsPerPersonPerDay: 0 },
    );
    expect(r.total).toBe(1000);
    expect(r.costPerAttendee).toBe(1000 / 3);
    expect(r.costPerAttendee).not.toBe(333.33);
  });

  it('uses the supplied rate card', () => {
    const custom = { ...testRateCard, lodgingPerPersonPerNight: 10, mealsPerPersonPerDay: 0 };
    const r = calculateBudget(baseInput({ attendees: 2, nights: 1 }), custom);
    expect(r.lineItems.find((l) => l.category === 'lodging')!.amount).toBe(20);
  });

  it('is deterministic and takes no markup field', () => {
    const input = baseInput({ staff: 100 });
    expect(calculateBudget(input, testRateCard)).toEqual(calculateBudget(input, testRateCard));
    expect('markup' in input).toBe(false);
  });
});
