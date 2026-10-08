import type { RateCard } from './calculator/types';

/**
 * PLACEHOLDER PRICES. Replace with real Retreat Builders client prices.
 * Every value is a USD client price with markup already included.
 */
export const defaultRateCard: RateCard = {
  lodgingPerPersonPerNight: 120,
  lodgingPerRoomPerNight: 220,
  mealsPerPersonPerDay: 65,
  activities: [
    { id: 'yoga-session', name: 'Guided yoga session', price: 40, perPerson: true },
    { id: 'team-workshop', name: 'Facilitated team workshop', price: 1200, perPerson: false },
    { id: 'guided-hike', name: 'Guided hike', price: 55, perPerson: true },
    { id: 'cooking-class', name: 'Group cooking class', price: 85, perPerson: true },
    { id: 'campfire-evening', name: 'Campfire evening', price: 450, perPerson: false },
  ],
};
