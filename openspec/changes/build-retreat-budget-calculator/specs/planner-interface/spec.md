# Spec Delta

## Purpose

Defines the localhost single-page experience in which a planner enters retreat details and reads an itemized budget, styled with Retreat Builders placeholder brand tokens.

## ADDED Requirements

### Requirement: Local single-page app
The system SHALL run as a single page served on localhost by a single command, with no accounts and no data persisted beyond the page session.

#### Scenario: Start locally
- **WHEN** the developer runs the documented dev command
- **THEN** the app is reachable on localhost and shows the planner form

### Requirement: Planner form
The system SHALL let the planner enter retreat name, attendees, nights, lodging mode and occupancy, venue amount and mode, transportation amount and mode, staff, miscellaneous and contingency percentage, and choose activities.

#### Scenario: Contingency default
- **WHEN** the form first loads
- **THEN** contingency shows 10%

#### Scenario: Occupancy only for per-room lodging
- **WHEN** lodging mode is per person
- **THEN** the occupancy field is hidden or disabled

### Requirement: Activity add-ons
The system SHALL show the sample activities as add-ons the planner can select, and SHALL let the planner add a custom activity with a name, a price and a per-person or flat basis.

#### Scenario: Add a sample activity
- **WHEN** the planner selects a sample activity
- **THEN** its cost appears in the activities line and in the totals

#### Scenario: Add a custom activity
- **WHEN** the planner adds a custom activity named "Sound bath" at $300 flat
- **THEN** it is included in the activities subtotal

### Requirement: Live results
The system SHALL recalculate and redisplay the budget on every valid input change, without a submit action.

#### Scenario: Changing attendees
- **WHEN** the planner changes attendees from 10 to 12
- **THEN** the breakdown, total and per-attendee cost update immediately

### Requirement: Itemized results display
The system SHALL display each category's subtotal and share of the subtotal, the subtotal, the contingency amount, the total and the cost per attendee, formatted in USD with a `$` sign and two decimals.

#### Scenario: Currency format
- **WHEN** a total of 11000 is displayed
- **THEN** it reads $11,000.00

### Requirement: Error presentation
The system SHALL show validation messages next to the affected field and SHALL replace the results with a prompt to fix the inputs while errors exist.

#### Scenario: Invalid attendees
- **WHEN** the planner clears the attendees field
- **THEN** an error appears beside it and no stale totals are shown

### Requirement: Placeholder brand tokens
The system SHALL take all colors, fonts, spacing and radii from a single set of named brand tokens with placeholder values, so the brand guide can be applied by changing that set only.

#### Scenario: Swap brand values
- **WHEN** a token value in the brand stylesheet is changed
- **THEN** the corresponding color or font changes across the app without editing component styles

### Requirement: Accessible form
The system SHALL label every input and associate each error message with its field.

#### Scenario: Labelled inputs
- **WHEN** the form is inspected
- **THEN** every input has an associated visible label
