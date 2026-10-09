# Spec Delta

## ADDED Requirements

### Requirement: Lodging mode is saved with the budget
The system SHALL save the lodging mode and the people-per-room value with the other calculator inputs and SHALL restore them, with the resulting totals, when the budget is opened.

#### Scenario: Save and reopen in per-room mode
- **WHEN** a signed-in user saves a budget priced per room with 2 people per room and later opens it
- **THEN** the mode is per room, people per room shows 2, and rooms needed and the totals match what was shown when it was saved

### Requirement: Budgets saved before lodging mode existed still open
The system SHALL open budgets that were saved without a lodging mode as priced per person, SHALL treat an unrecognised stored mode the same way, and SHALL NOT alter or lose their other inputs.

#### Scenario: Older budget without a mode
- **WHEN** a user opens a budget whose stored inputs contain no lodging mode
- **THEN** it opens priced per person with all its saved inputs and the same totals as before, and saving it again stores the per-person mode

#### Scenario: Unrecognised stored mode
- **WHEN** a user opens a budget whose stored lodging mode is not per person or per room
- **THEN** it opens priced per person
