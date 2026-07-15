# PostgreSQL migration upgrade-path test

## Goal

Add one Docker-backed integration test that proves the PostgreSQL migration
chain can advance from an empty database to the current schema while it
contains representative application data at every migration boundary.

## Scope

- PostgreSQL only.
- Reuse the existing Testcontainers PostgreSQL fixture in
  `DbUpPostgresqlMigrationTests`.
- Do not change production migrations or Docker Compose configuration.

## Design

The new asynchronous test discovers the embedded PostgreSQL migration resource
names in ordinal order. It applies exactly one not-yet-journaled resource at a
time through DbUp, retaining the same disposable database and DbUp journal for
the whole test. This makes the test cover the complete current chain and any
future embedded migration resources without maintaining a maximum version.

After each successful migration, a focused seed routine inserts a representative
row into every table newly available at that point, or updates data to exercise
new columns where appropriate. Seeds respect foreign-key dependencies. The
initial migration seeds a module; later steps add a user and API key, webhook,
legacy VCS source, role and assignment, audit entry, and later feature records
as their tables become available.

Migration 010 is a required preservation checkpoint. The fixture creates a
legacy VCS source before the migration, then asserts afterwards that its source
identity and metadata remain and that the encrypted PAT and webhook secret were
moved to a linked VCS connection.

Before and after each migration, verification queries assert every previously
seeded record and its required relationships remain present. The final
assertions verify that the journal contains every embedded PostgreSQL script,
foreign-key validation succeeds, and invoking the production `DbUpMigrator`
again performs no work and retains the seeded data.

## Error handling and diagnostics

Each upgrade step is labeled with its embedded resource name. A failed DbUp
result is surfaced with that name and DbUp's original exception, so a test
failure identifies the precise migration boundary. Assertions use stable seed
identifiers and report the missing table or relationship.

## Validation

Run the focused Testcontainers integration test with Docker available, then run
the existing PostgreSQL migration test class. The test is successful only when
all migrations execute, each intermediate seed succeeds, all preservation
checks pass, and the final no-op migration retains the full journal and data.
