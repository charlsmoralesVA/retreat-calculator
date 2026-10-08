export type LodgingMode = 'perPerson' | 'perRoom';
export type VenueMode = 'flat' | 'perDay';
export type TransportationMode = 'flat' | 'perPerson';

export type Category =
  'lodging' | 'venue' | 'meals' | 'activities' | 'transportation' | 'staff' | 'miscellaneous';

export const CATEGORIES: readonly Category[] = [
  'lodging',
  'venue',
  'meals',
  'activities',
  'transportation',
  'staff',
  'miscellaneous',
];

export interface Activity {
  id: string;
  name: string;
  /** Client price in USD (markup already included for catalog activities). */
  price: number;
  /** true: charged once per attendee; false: charged once. */
  perPerson: boolean;
}

/** Fixed client prices in USD. Markup is already included in every value. */
export interface RateCard {
  lodgingPerPersonPerNight: number;
  lodgingPerRoomPerNight: number;
  mealsPerPersonPerDay: number;
  activities: readonly Activity[];
}

export interface BudgetInput {
  name: string;
  attendees: number;
  nights: number;
  lodgingMode: LodgingMode;
  /** Used only when lodgingMode is 'perRoom'. */
  occupancy: number;
  venueAmount: number;
  venueMode: VenueMode;
  transportationAmount: number;
  transportationMode: TransportationMode;
  staff: number;
  miscellaneous: number;
  /** Percentage, 0-100. */
  contingencyPct: number;
  /** Selected catalog activities plus any custom ones. */
  activities: readonly Activity[];
}

export interface LineItem {
  category: Category;
  amount: number;
  /** Percentage of the subtotal, 0-100. Zero when the subtotal is zero. */
  share: number;
}

export interface BudgetResult {
  lineItems: LineItem[];
  subtotal: number;
  contingencyAmount: number;
  total: number;
  costPerAttendee: number;
}

export const DEFAULT_CONTINGENCY_PCT = 10;
