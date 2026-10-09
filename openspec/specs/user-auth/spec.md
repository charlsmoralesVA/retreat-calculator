# user-auth Specification

## Purpose

Lets people create an account and sign in to the retreat budget calculator so their saved data is tied to them and private to them.

## Requirements


### Requirement: Account sign-up
The system SHALL allow a visitor to create an account with an email address and a password, and SHALL require the email to be confirmed before the account can sign in.

#### Scenario: Successful sign-up
- **WHEN** a visitor submits a valid unused email and a password of at least 8 characters
- **THEN** the system creates the account and tells the visitor to check their email for a confirmation link

#### Scenario: Email already registered
- **WHEN** a visitor signs up with an email that already has an account
- **THEN** the system does not create a second account and shows a non-revealing message

#### Scenario: Weak or invalid input
- **WHEN** a visitor submits a malformed email or a password shorter than 8 characters
- **THEN** the system rejects the submission and shows which field is invalid

### Requirement: Login
The system SHALL allow a confirmed user to sign in with their email and password.

#### Scenario: Successful login
- **WHEN** a confirmed user submits correct credentials
- **THEN** the system signs them in and shows their saved budgets

#### Scenario: Wrong credentials
- **WHEN** a user submits an incorrect password or unknown email
- **THEN** the system rejects the login with a generic error and does not sign them in

#### Scenario: Unconfirmed account
- **WHEN** a user whose email is not yet confirmed attempts to sign in
- **THEN** the system rejects the login and explains the email must be confirmed

### Requirement: Session persistence
The system SHALL keep a signed-in user signed in across page reloads until they log out or the session expires.

#### Scenario: Reload while signed in
- **WHEN** a signed-in user reloads the page
- **THEN** they remain signed in without re-entering credentials

### Requirement: Logout
The system SHALL allow a signed-in user to log out, ending their session on that browser.

#### Scenario: Logout clears access
- **WHEN** a signed-in user chooses to log out
- **THEN** the system ends the session and no saved budgets are visible until they sign in again
