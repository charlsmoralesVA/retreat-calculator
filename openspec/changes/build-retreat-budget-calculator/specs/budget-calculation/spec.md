# Spec Delta

## Purpose

Defines how a retreat budget is computed from planner inputs and rate-card prices: itemized categories, subtotal, contingency, total, per-attendee cost and category shares. All prices are client prices; markup is already included.

## ADDED Requirements

### Requirement: Category line items
The system SHALL compute a subtotal for each category: lodging, venue, meals, activities, transportation, staff and miscellaneous.

#### Scenario: All categories reported
- **WHEN** a valid budget is calculated
- **THEN** the result contains one subtotal for each of the seven categories, with zero for a category that has no cost

### Requirement: Lodging cost
The system SHALL price lodging per person or per room using the rate-card lodging rate. In per-room mode the number of rooms SHALL be the attendee count divided by occupancy per room, rounded up.

#### Scenario: Per-person lodging
- **WHEN** 10 attendees stay 3 nights in per-person mode at a rate of $100 per person per night
- **THEN** the lodging subtotal is $3,000

#### Scenario: Per-room lodging rounds rooms up
- **WHEN** 7 attendees stay 2 nights in per-room mode with occupancy 2 at a rate of $200 per room per night
- **THEN** 4 rooms are charged and the lodging subtotal is $1,600

#### Scenario: Single attendee in per-room mode
- **WHEN** 1 attendee stays 1 night in per-room mode with occupancy 2 at $200 per room per night
- **THEN** 1 room is charged and the lodging subtotal is $200

### Requirement: Venue cost
The system SHALL price the venue as a flat amount or as a per-day amount, where the number of days is nights plus one.

#### Scenario: Flat venue
- **WHEN** the venue amount is $2,000 in flat mode for any length of stay
- **THEN** the venue subtotal is $2,000

#### Scenario: Per-day venue
- **WHEN** the venue amount is $500 in per-day mode for a 3-night stay
- **THEN** the venue subtotal is $2,000 (4 days)

### Requirement: Meals cost
The system SHALL compute meals as the rate-card meals-per-person-per-day price multiplied by attendees and by days, where days equal nights plus one.

#### Scenario: Meals include arrival and departure days
- **WHEN** 10 attendees stay 3 nights at $60 per person per day
- **THEN** the meals subtotal is $2,400 (10 x 4 days x $60)

### Requirement: Activities cost
The system SHALL sum the selected activities, charging a per-person activity once per attendee and a flat activity once.

#### Scenario: Mixed activities
- **WHEN** 10 attendees select a per-person activity at $50 and a flat activity at $400
- **THEN** the activities subtotal is $900

#### Scenario: No activities
- **WHEN** no activities are selected
- **THEN** the activities subtotal is zero

### Requirement: Transportation, staff and miscellaneous cost
The system SHALL treat staff and miscellaneous as flat planner-entered amounts, and transportation as a flat amount or a per-person amount multiplied by attendees.

#### Scenario: Per-person transportation
- **WHEN** 10 attendees and a transportation amount of $75 in per-person mode
- **THEN** the transportation subtotal is $750

### Requirement: Totals and contingency
The system SHALL compute the subtotal as the sum of the category subtotals, the contingency amount as subtotal multiplied by the contingency percentage, and the total as subtotal plus contingency. The default contingency SHALL be 10%.

#### Scenario: Default contingency
- **WHEN** the subtotal is $10,000 and contingency is left at its default
- **THEN** the contingency amount is $1,000 and the total is $11,000

#### Scenario: Zero contingency
- **WHEN** contingency is 0%
- **THEN** the total equals the subtotal

### Requirement: Cost per attendee
The system SHALL compute cost per attendee as the total divided by the attendee count.

#### Scenario: Per-person cost
- **WHEN** the total is $11,000 for 10 attendees
- **THEN** the cost per attendee is $1,100

### Requirement: Category share
The system SHALL report each category's percentage of the subtotal. When the subtotal is zero, every share SHALL be zero.

#### Scenario: Shares sum to 100
- **WHEN** the subtotal is greater than zero
- **THEN** the category shares sum to 100% before display rounding

#### Scenario: Zero subtotal
- **WHEN** every category is zero
- **THEN** all shares are 0% and no division error occurs

### Requirement: Precision and rounding
The system SHALL keep full precision in calculation and round money to two decimal places only for display.

#### Scenario: Rounding only at display
- **WHEN** a total of $1,000 is divided among 3 attendees
- **THEN** the displayed per-attendee cost is $333.33 while the underlying value is not rounded

### Requirement: No separate markup
The system SHALL NOT apply markup at calculation time, and SHALL NOT report a separate client price or margin amount; the total is the client price.

#### Scenario: Prices unaffected by an extra markup step
- **WHEN** a budget is calculated twice from the same inputs and rate card
- **THEN** both results are identical and no markup field is part of the input
