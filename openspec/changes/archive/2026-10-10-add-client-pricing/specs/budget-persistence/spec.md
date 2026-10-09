# Spec Delta

## ADDED Requirements

### Requirement: Markup is saved with the budget
The system SHALL save the markup percentage with the other calculator inputs and SHALL restore it, with the resulting client price, when the budget is opened.

#### Scenario: Save and reopen with markup
- **WHEN** a signed-in user saves a budget with a markup of 25% and later opens it
- **THEN** the markup field shows 25% and the client price matches what was shown when it was saved

### Requirement: Budgets saved before markup existed still open
The system SHALL open budgets that were saved without a markup value, treating the markup as zero, and SHALL NOT alter or lose their other inputs.

#### Scenario: Older budget without markup
- **WHEN** a user opens a budget whose stored inputs contain no markup value
- **THEN** it opens with all its saved inputs, the markup is zero, no client price section is shown, and saving it again stores a markup of zero
