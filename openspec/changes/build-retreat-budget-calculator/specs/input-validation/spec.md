# Spec Delta

## Purpose

Defines which planner inputs are valid and how invalid input is reported, so the calculator never presents a budget built on nonsensical values.

## ADDED Requirements

### Requirement: Attendees and nights
The system SHALL require attendees and nights to be whole numbers of at least 1.

#### Scenario: Zero attendees
- **WHEN** the attendee count is 0
- **THEN** validation fails with a message on the attendees field stating at least one attendee is required

#### Scenario: Fractional nights
- **WHEN** nights is 2.5
- **THEN** validation fails with a message on the nights field requiring a whole number

### Requirement: Non-negative amounts
The system SHALL reject negative values for venue, transportation, staff, miscellaneous and custom activity amounts.

#### Scenario: Negative amount
- **WHEN** the staff fee is -100
- **THEN** validation fails with a message on the staff field requiring zero or more

### Requirement: Occupancy
The system SHALL require occupancy per room to be a whole number of at least 1 when lodging is priced per room.

#### Scenario: Occupancy ignored in per-person mode
- **WHEN** lodging is per person and occupancy is empty
- **THEN** validation does not report an occupancy error

#### Scenario: Invalid occupancy in per-room mode
- **WHEN** lodging is per room and occupancy is 0
- **THEN** validation fails with a message on the occupancy field

### Requirement: Contingency range
The system SHALL require contingency to be between 0 and 100 inclusive.

#### Scenario: Out-of-range contingency
- **WHEN** contingency is 120
- **THEN** validation fails with a message on the contingency field

### Requirement: Field-level errors without a budget
The system SHALL report each error against its field and SHALL NOT produce budget results while any error exists.

#### Scenario: Multiple errors
- **WHEN** attendees is 0 and staff is negative
- **THEN** both errors are reported and no totals are produced

### Requirement: Optional retreat name
The system SHALL accept an empty retreat name.

#### Scenario: Name omitted
- **WHEN** the retreat name is empty and all other inputs are valid
- **THEN** validation passes
