import {
  CATEGORIES,
  type BudgetInput,
  type BudgetResult,
  type Category,
  type RateCard,
} from './types';

/**
 * Computes the itemized budget. Assumes `input` has passed `validate`.
 * All prices are client prices (markup is already part of the rate card).
 * Values keep full precision; round only when displaying.
 */
export function calculateBudget(input: BudgetInput, rateCard: RateCard): BudgetResult {
  const { attendees, nights } = input;
  const days = nights + 1;

  const rooms = Math.ceil(attendees / input.occupancy);
  const lodging =
    input.lodgingMode === 'perRoom'
      ? rooms * rateCard.lodgingPerRoomPerNight * nights
      : attendees * rateCard.lodgingPerPersonPerNight * nights;

  const amounts: Record<Category, number> = {
    lodging,
    venue: input.venueMode === 'perDay' ? input.venueAmount * days : input.venueAmount,
    meals: attendees * days * rateCard.mealsPerPersonPerDay,
    activities: input.activities.reduce(
      (sum, a) => sum + (a.perPerson ? a.price * attendees : a.price),
      0,
    ),
    transportation:
      input.transportationMode === 'perPerson'
        ? input.transportationAmount * attendees
        : input.transportationAmount,
    staff: input.staff,
    miscellaneous: input.miscellaneous,
  };

  const subtotal = CATEGORIES.reduce((sum, c) => sum + amounts[c], 0);
  const contingencyAmount = subtotal * (input.contingencyPct / 100);
  const total = subtotal + contingencyAmount;

  return {
    lineItems: CATEGORIES.map((category) => ({
      category,
      amount: amounts[category],
      share: subtotal > 0 ? (amounts[category] / subtotal) * 100 : 0,
    })),
    subtotal,
    contingencyAmount,
    total,
    costPerAttendee: total / attendees,
  };
}
