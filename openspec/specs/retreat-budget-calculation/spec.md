# retreat-budget-calculation Specification

## Purpose

Computes the cost of a retreat from organizer-entered inputs, giving per-person and total figures that update as the inputs change.

## Requirements

### Requirement: Cost inputs
The system SHALL accept headcount, number of nights, a lodging mode (per person or per room), the lodging rate for that mode, people per room, food cost per person per day, a fixed venue fee, activities cost per person, travel cost per person, a contingency percentage, and an optional markup percentage.

#### Scenario: Defaults
- **WHEN** the calculator loads with no saved budget
- **THEN** all numeric inputs start at zero and totals show zero, and lodging is priced per person

#### Scenario: Invalid input
- **WHEN** a user enters a negative number, or a headcount that is not a whole number
- **THEN** the system flags the field and excludes it from the totals until corrected

#### Scenario: Invalid markup
- **WHEN** a user enters a negative markup or a value that is not a number
- **THEN** the system flags the markup field and treats the markup as zero, so no client price is shown, until it is corrected

#### Scenario: Large markup accepted
- **WHEN** a user enters a markup above 100%
- **THEN** the system accepts it and computes the client price normally

### Requirement: Live totals
The system SHALL compute and display a subtotal, the contingency amount, the grand total and the per-person cost, updating immediately when any input changes.

#### Scenario: Worked example
- **WHEN** headcount is 10, nights is 3, lodging is 100, food is 50, venue fee is 1000, activities is 40, travel is 80, and contingency is 10%
- **THEN** subtotal is 7,200 (3,000 lodging + 2,000 food + 1,000 venue + 400 activities + 800 travel), contingency is 720, grand total is 7,920, and per-person cost is 792

#### Scenario: Formula definition
- **WHEN** totals are computed
- **THEN** lodging equals headcount times nights times lodging rate when priced per person, or rooms needed times nights times lodging rate when priced per room; food equals headcount times (nights plus 1) times food rate; activities and travel equal headcount times their per-person amounts; subtotal is the sum of those plus the venue fee; contingency is subtotal times the percentage; grand total is subtotal plus contingency; and per-person cost is grand total divided by headcount

#### Scenario: Zero headcount
- **WHEN** headcount is zero
- **THEN** per-person cost shows as not available rather than an error or infinity

### Requirement: Calculator available without login
The system SHALL let a signed-out visitor use the calculator in full; login SHALL be required only to save and retrieve budgets.

#### Scenario: Anonymous use
- **WHEN** a signed-out visitor fills in the calculator
- **THEN** all totals compute and display normally

### Requirement: Client price from markup
When a markup percentage above zero is set, the system SHALL compute a client price from the total cost including contingency, and SHALL display the per-person price, the quoted total, the margin amount and the margin percentage.

#### Scenario: Formula definition
- **WHEN** markup is above zero and headcount is at least one
- **THEN** the per-person price equals total cost times (1 plus markup percentage divided by 100), divided by headcount, rounded to the nearest cent with exact halves rounding up; the quoted total equals that rounded per-person price times headcount; the margin amount equals the quoted total minus the total cost; and the margin percentage equals the margin amount divided by the quoted total

#### Scenario: Worked example without rounding loss
- **WHEN** the total cost is 7,920 for 10 attendees and markup is 25%
- **THEN** the per-person price is 990.00, the quoted total is 9,900.00, the margin amount is 1,980.00, and the margin percentage is 20.0%

#### Scenario: Worked example with rounding
- **WHEN** the total cost is 10,000 for 7 attendees and markup is 25%
- **THEN** the per-person price is 1,785.71, the quoted total is 12,499.97, the margin amount is 2,499.97, and the margin percentage is 20.0%

#### Scenario: Quoted total multiplies back exactly
- **WHEN** a client price is displayed
- **THEN** the displayed per-person price multiplied by headcount equals the displayed quoted total exactly

#### Scenario: Contingency is part of the cost base
- **WHEN** contingency is above zero and markup is above zero
- **THEN** the client price is computed from the total cost that already includes contingency

### Requirement: Client price visibility and edge cases
The system SHALL show the client price section only when markup is above zero, SHALL leave the total cost unchanged by markup, and SHALL show the client figures as not available when headcount is zero.

#### Scenario: No markup
- **WHEN** markup is zero or empty
- **THEN** no client price section is shown and the total cost and per-person cost are as they would be without the markup feature

#### Scenario: Markup does not change total cost
- **WHEN** a markup is entered or changed
- **THEN** the subtotal, contingency, grand total and per-person cost do not change

#### Scenario: Zero headcount
- **WHEN** markup is above zero and headcount is zero
- **THEN** the client per-person price, quoted total, margin amount and margin percentage show as not available

#### Scenario: Live update
- **WHEN** any input or the markup changes
- **THEN** the client price figures update immediately

### Requirement: Lodging priced per person or per room
The system SHALL let the user choose whether lodging is priced per person or per room. In per-room mode it SHALL take the number of people per room, compute rooms needed as headcount divided by people per room rounded up, and charge rooms needed times nights times the room rate. Per person is the default.

#### Scenario: Default mode
- **WHEN** the calculator loads with no saved budget
- **THEN** lodging is priced per person, the lodging rate is labelled per person per night, and no people-per-room input is shown

#### Scenario: Switching to per room
- **WHEN** the user switches lodging to per room
- **THEN** the lodging rate is labelled per room per night, a people-per-room input appears, and a Rooms needed result is shown

#### Scenario: Per-room worked example
- **WHEN** lodging is per room at 180 per room per night with 2 people per room, headcount is 10, nights is 3, food is 50, venue fee is 1000, activities is 40, travel is 80, and contingency is 10%
- **THEN** rooms needed is 5, lodging is 2,700, subtotal is 6,900, contingency is 690, grand total is 7,590, and per-person cost is 759

#### Scenario: Partly filled room is charged in full
- **WHEN** lodging is per room at 150 per room per night with 2 people per room, headcount is 7, and nights is 3
- **THEN** rooms needed is 4 and lodging is 1,800

#### Scenario: One person per room matches per-person pricing
- **WHEN** lodging is per room with 1 person per room at a rate of 100, headcount is 10, and nights is 3
- **THEN** rooms needed is 10 and lodging is 3,000, the same as per-person pricing at a rate of 100

#### Scenario: Zero headcount in per-room mode
- **WHEN** lodging is per room with a valid people-per-room value and headcount is zero
- **THEN** rooms needed is 0 and lodging is 0

#### Scenario: Invalid people per room
- **WHEN** lodging is per room and people per room is blank, zero, negative, or not a whole number
- **THEN** the system flags the people-per-room field, shows Rooms needed as not available, and excludes lodging from the totals until it is corrected

#### Scenario: People per room ignored in per-person mode
- **WHEN** lodging is per person
- **THEN** any people-per-room value is ignored, is never flagged, and has no effect on the totals

#### Scenario: Entered values are kept when the mode changes
- **WHEN** the user switches the mode and then switches back
- **THEN** the lodging rate and people-per-room values they entered are still present

#### Scenario: Only lodging depends on the mode
- **WHEN** the mode changes
- **THEN** food, venue, activities and travel are unchanged for the same inputs, and the contingency, markup and client price are computed from the resulting total cost as usual
