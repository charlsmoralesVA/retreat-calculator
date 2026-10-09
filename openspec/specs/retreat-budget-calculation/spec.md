# retreat-budget-calculation Specification

## Purpose

Computes the cost of a retreat from organizer-entered inputs, giving per-person and total figures that update as the inputs change.

## Requirements


### Requirement: Cost inputs
The system SHALL accept headcount, number of nights, lodging cost per person per night, food cost per person per day, a fixed venue fee, activities cost per person, travel cost per person, and a contingency percentage.

#### Scenario: Defaults
- **WHEN** the calculator loads with no saved budget
- **THEN** all numeric inputs start at zero and totals show zero

#### Scenario: Invalid input
- **WHEN** a user enters a negative number, or a headcount that is not a whole number
- **THEN** the system flags the field and excludes it from the totals until corrected

### Requirement: Live totals
The system SHALL compute and display a subtotal, the contingency amount, the grand total and the per-person cost, updating immediately when any input changes.

#### Scenario: Worked example
- **WHEN** headcount is 10, nights is 3, lodging is 100, food is 50, venue fee is 1000, activities is 40, travel is 80, and contingency is 10%
- **THEN** subtotal is 7,200 (3,000 lodging + 2,000 food + 1,000 venue + 400 activities + 800 travel), contingency is 720, grand total is 7,920, and per-person cost is 792

#### Scenario: Formula definition
- **WHEN** totals are computed
- **THEN** lodging equals headcount times nights times lodging rate, food equals headcount times (nights plus 1) times food rate, activities and travel equal headcount times their per-person amounts, subtotal is the sum of those plus the venue fee, contingency is subtotal times the percentage, grand total is subtotal plus contingency, and per-person cost is grand total divided by headcount

#### Scenario: Zero headcount
- **WHEN** headcount is zero
- **THEN** per-person cost shows as not available rather than an error or infinity

### Requirement: Calculator available without login
The system SHALL let a signed-out visitor use the calculator in full; login SHALL be required only to save and retrieve budgets.

#### Scenario: Anonymous use
- **WHEN** a signed-out visitor fills in the calculator
- **THEN** all totals compute and display normally
