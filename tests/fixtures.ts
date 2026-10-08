import type { BudgetInput, RateCard } from '../src/calculator/types';

export const testRateCard: RateCard = {
  lodgingPerPersonPerNight: 100,
  lodgingPerRoomPerNight: 200,
  mealsPerPersonPerDay: 60,
  activities: [],
};

export function baseInput(overrides: Partial<BudgetInput> = {}): BudgetInput {
  return {
    name: 'Test retreat',
    attendees: 10,
    nights: 3,
    lodgingMode: 'perPerson',
    occupancy: 2,
    venueAmount: 0,
    venueMode: 'flat',
    transportationAmount: 0,
    transportationMode: 'flat',
    staff: 0,
    miscellaneous: 0,
    contingencyPct: 10,
    activities: [],
    ...overrides,
  };
}
