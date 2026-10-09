# local-dev-backend Specification

## Purpose

Defines the local Postgres and authentication backend that developers run on their machine, so the app can be built and tested against a real database without any cloud account.

## Requirements


### Requirement: One-command local backend
The system SHALL start a complete local backend (Postgres database, authentication service, REST API, admin UI and email inbox) with a single documented command, given Docker Desktop is running.

#### Scenario: Fresh start
- **WHEN** a developer runs the documented start command on a machine with Docker Desktop running
- **THEN** the database, auth service, API, admin UI and email inbox become reachable on their documented local ports

#### Scenario: Docker not running
- **WHEN** the developer runs the start command while Docker Desktop is stopped
- **THEN** the command fails with an error indicating Docker is unavailable and no partial state is left behind

### Requirement: Reproducible database schema
The system SHALL define the database schema exclusively through committed migration files, and a documented reset command SHALL rebuild the local database from those migrations alone.

#### Scenario: Reset to known state
- **WHEN** a developer runs the documented reset command
- **THEN** the local database is dropped and recreated with every committed migration applied in order and all user data removed

### Requirement: Shared stack across worktrees
All git worktrees of this repository SHALL use the same local project identity and ports, so that a single running stack serves whichever worktree is active.

#### Scenario: Second worktree reuses the stack
- **WHEN** the stack is already running from one worktree and a developer works in another worktree
- **THEN** the app in the second worktree connects to the same running stack without starting a second one

#### Scenario: Port conflict avoided
- **WHEN** a developer attempts to start a second stack while the shared stack is running
- **THEN** the documentation directs them to reuse the running stack rather than start another

### Requirement: Environment configuration contract
The system SHALL provide a committed example environment file listing every variable the app needs to reach the local backend, and SHALL keep the real local environment file out of version control.

#### Scenario: New contributor setup
- **WHEN** a contributor copies the example environment file and fills in values printed by the status command
- **THEN** the app can connect to the local backend with no other configuration

#### Scenario: Secrets not committed
- **WHEN** a developer creates the local environment file
- **THEN** version control ignores it

### Requirement: Local email confirmation
The system SHALL deliver authentication emails (such as sign-up confirmation) to a local inbox viewable in the browser, and SHALL NOT send mail to external addresses.

#### Scenario: Sign-up email captured
- **WHEN** a user signs up against the local backend
- **THEN** the confirmation email appears in the local inbox and no external email is sent
