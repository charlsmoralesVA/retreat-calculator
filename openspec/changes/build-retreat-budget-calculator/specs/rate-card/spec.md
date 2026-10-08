# Spec Delta

## Purpose

Defines the fixed, markup-inclusive client prices that the calculator uses for lodging, meals and sample activities, kept as configuration separate from calculation logic so prices can be replaced without code changes to the calculator.

## ADDED Requirements

### Requirement: Fixed client prices
The system SHALL provide a rate card containing client prices for lodging per person per night, lodging per room per night, meals per person per day, and a catalog of sample activities. These prices SHALL already include Retreat Builders' markup.

#### Scenario: Prices are not planner-editable markup
- **WHEN** a planner uses the calculator
- **THEN** lodging and meals prices come from the rate card and no markup percentage is entered

### Requirement: Placeholder values
The rate card SHALL ship with clearly placeholder prices until real Retreat Builders prices are supplied.

#### Scenario: Placeholder prices present
- **WHEN** the application starts with the default rate card
- **THEN** every rate-card price is a defined, non-negative USD amount

### Requirement: Sample activity catalog
The rate card SHALL include sample activities, each with a name, a USD price, and a per-person or flat pricing basis, which a planner can add as add-ons.

#### Scenario: Catalog entry shape
- **WHEN** the activity catalog is read
- **THEN** each entry has a name, a non-negative price and a per-person or flat flag

### Requirement: Replaceable configuration
The calculator SHALL accept the rate card as an input, so tests and future price updates can supply different prices.

#### Scenario: Custom rate card
- **WHEN** a budget is calculated with a different rate card
- **THEN** the result reflects the supplied prices
