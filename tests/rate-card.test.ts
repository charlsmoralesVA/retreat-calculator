import { describe, expect, it } from 'vitest';
import { defaultRateCard } from '../src/rate-card';

describe('defaultRateCard', () => {
  it('has non-negative prices', () => {
    expect(defaultRateCard.lodgingPerPersonPerNight).toBeGreaterThanOrEqual(0);
    expect(defaultRateCard.lodgingPerRoomPerNight).toBeGreaterThanOrEqual(0);
    expect(defaultRateCard.mealsPerPersonPerDay).toBeGreaterThanOrEqual(0);
  });

  it('has a catalog of activities with a name, price and pricing basis', () => {
    expect(defaultRateCard.activities.length).toBeGreaterThan(0);
    for (const a of defaultRateCard.activities) {
      expect(a.name.length).toBeGreaterThan(0);
      expect(a.price).toBeGreaterThanOrEqual(0);
      expect(typeof a.perPerson).toBe('boolean');
    }
  });

  it('has unique activity ids', () => {
    const ids = defaultRateCard.activities.map((a) => a.id);
    expect(new Set(ids).size).toBe(ids.length);
  });
});
