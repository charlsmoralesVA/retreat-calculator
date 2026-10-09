# Spec Delta

## Purpose

Lets a signed-in user save retreat budgets, return to them later, and be certain that no other user can see or change them.

## ADDED Requirements

### Requirement: Save a budget
The system SHALL allow a signed-in user to save the current calculator inputs as a named budget.

#### Scenario: Save new budget
- **WHEN** a signed-in user enters a name and chooses to save
- **THEN** the system stores the name and all current inputs and shows the budget in their list

#### Scenario: Update existing budget
- **WHEN** a signed-in user edits inputs of a budget they opened and chooses to save
- **THEN** the system overwrites that budget's stored inputs and updates its last-modified time

#### Scenario: Not signed in
- **WHEN** a signed-out visitor attempts to save
- **THEN** the system prompts them to log in or sign up and keeps their entered inputs

### Requirement: List and open budgets
The system SHALL show a signed-in user only their own budgets, newest-modified first, and SHALL restore a budget's inputs exactly when it is opened.

#### Scenario: List own budgets
- **WHEN** a signed-in user opens their saved budgets
- **THEN** the system lists their budgets by name and last-modified time, most recent first

#### Scenario: Open a budget
- **WHEN** a user opens a saved budget
- **THEN** the calculator is populated with the stored inputs and shows the same totals as when saved

#### Scenario: Empty state
- **WHEN** a signed-in user has no saved budgets
- **THEN** the system shows a message explaining how to save one

### Requirement: Rename and delete budgets
The system SHALL allow a user to rename or permanently delete their own budgets, and SHALL ask for confirmation before deleting.

#### Scenario: Rename
- **WHEN** a user renames a budget to a non-empty name
- **THEN** the list shows the new name

#### Scenario: Confirmed delete
- **WHEN** a user confirms deletion of a budget
- **THEN** the budget is removed and no longer appears in their list

#### Scenario: Cancelled delete
- **WHEN** a user cancels the delete confirmation
- **THEN** the budget is unchanged

### Requirement: Per-user data isolation
The system SHALL ensure a user can read, create, modify and delete only budgets that belong to them, enforced by the database and not only by the interface.

#### Scenario: Cross-user read blocked
- **WHEN** user A requests a budget owned by user B directly through the API
- **THEN** the database returns no data for it

#### Scenario: Cross-user write blocked
- **WHEN** user A attempts to update or delete user B's budget
- **THEN** the operation affects no rows

#### Scenario: Anonymous access blocked
- **WHEN** an unauthenticated request queries saved budgets
- **THEN** the database returns no rows and rejects any write

### Requirement: Save failures are visible
The system SHALL tell the user when a save, load or delete fails and SHALL NOT discard their unsaved inputs.

#### Scenario: Backend unreachable
- **WHEN** a save fails because the backend is unreachable
- **THEN** the system shows an error and the entered inputs remain in the form
