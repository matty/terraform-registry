# PostgreSQL Migration Upgrade-Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve PostgreSQL VCS source data during migration 010 and prove all embedded PostgreSQL migrations upgrade a Docker database containing representative data.

**Architecture:** Rewrite 010 as an in-place expand–migrate–contract migration: create a connection for each source using its UUID, backfill the source's connection ID, enforce constraints, then remove only duplicated credentials. Add a Testcontainers integration test that discovers embedded resources, applies exactly one script per step, seeds schema-available data, and verifies all prior data after every step.

**Tech Stack:** .NET 10, DbUp, Npgsql, xUnit, Testcontainers PostgreSQL, Docker.

## Global Constraints

- PostgreSQL only; use the existing `PostgreSqlContainer` fixture, not Docker Compose.
- Do not drop or recreate `vcs_sources` in migration 010.
- Preserve each legacy VCS source's ID, user, module coordinates, repository, active state, timestamps, encrypted PAT, and webhook secret.
- Discover the entire embedded PostgreSQL chain rather than setting a maximum migration number.
- Historical databases that already ran destructive 010 require backup or authoritative external recovery.

---

## File Structure

- Modify: `TerraformRegistry.Migrations/Scripts/PostgreSQL/010_vcs_connections.sql` — data-preserving VCS relationship migration.
- Modify: `TerraformRegistry.Tests/DbUpPostgresqlMigrationTests.cs` — migration-preservation and populated script-by-script Docker tests.
- Modify: `docs/operations/database-migration-recovery.md` — historical data-loss limitation.

### Task 1: Make migration 010 preserve VCS source data

**Files:**
- Modify: `TerraformRegistry.Migrations/Scripts/PostgreSQL/010_vcs_connections.sql`
- Test: `TerraformRegistry.Tests/DbUpPostgresqlMigrationTests.cs`

**Interfaces:**
- Consumes: migration 007's legacy `vcs_sources` table and migration 002's `users` table.
- Produces: `vcs_connections(id UUID)` and `vcs_sources.connection_id UUID NOT NULL` with cascading foreign keys.

- [ ] **Step 1: Write the failing preservation test**

Add `Migration010PreservesPopulatedLegacyVcsSourcesAndCredentials` to `DbUpPostgresqlMigrationTests.cs`. Migrate to 009; insert user `user-1` and source `a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11` with all legacy fields; migrate to 010; query:

```sql
SELECT s.id, s.user_id, s.namespace, s.name, s.provider, s.repo_owner, s.repo_name,
       s.is_active, s.created_at, s.updated_at, c.pat_encrypted, c.webhook_secret,
       c.created_by, c.default_org, c.is_active, c.created_at, c.updated_at
FROM vcs_sources s
JOIN vcs_connections c ON c.id = s.connection_id
WHERE s.id = 'a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11';
```

Assert one row and the original source fields plus `encrypted-pat`, `webhook-secret`, `user-1`, `hashicorp`, and active state.

- [ ] **Step 2: Run the test and observe the destructive behavior**

Run:

```bash
dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter 'FullyQualifiedName~Migration010PreservesPopulatedLegacyVcsSourcesAndCredentials'
```

Expected: FAIL because current migration 010 drops the seeded source.

- [ ] **Step 3: Implement the safe expand–migrate–contract SQL**

Replace the full file with SQL equivalent to:

```sql
CREATE TABLE IF NOT EXISTS vcs_connections (
    id UUID PRIMARY KEY,
    label TEXT NOT NULL,
    provider TEXT NOT NULL DEFAULT 'github',
    pat_encrypted TEXT,
    default_org TEXT,
    webhook_secret TEXT NOT NULL,
    created_by TEXT,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_vcs_connections_active ON vcs_connections(is_active);

ALTER TABLE vcs_sources ADD COLUMN IF NOT EXISTS connection_id UUID;

INSERT INTO vcs_connections (
    id, label, provider, pat_encrypted, default_org, webhook_secret,
    created_by, is_active, created_at, updated_at
)
SELECT id, format('Migrated %s/%s', namespace, name), 'github', pat_encrypted,
       repo_owner, webhook_secret, user_id, is_active, created_at, updated_at
FROM vcs_sources
ON CONFLICT (id) DO NOTHING;

UPDATE vcs_sources SET connection_id = id WHERE connection_id IS NULL;
ALTER TABLE vcs_sources ALTER COLUMN connection_id SET NOT NULL;
ALTER TABLE vcs_sources
    ADD CONSTRAINT vcs_sources_connection_id_fkey
    FOREIGN KEY (connection_id) REFERENCES vcs_connections(id) ON DELETE CASCADE;
ALTER TABLE vcs_sources
    ADD CONSTRAINT vcs_sources_user_id_fkey
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;
ALTER TABLE vcs_sources DROP COLUMN pat_encrypted;
ALTER TABLE vcs_sources DROP COLUMN webhook_secret;
CREATE INDEX IF NOT EXISTS idx_vcs_sources_connection ON vcs_sources(connection_id);
```

- [ ] **Step 4: Run the preservation test**

Run the command from Step 2.

Expected: PASS; source and connection fields are retained.

- [ ] **Step 5: Commit the behavior change**

```bash
git add TerraformRegistry.Migrations/Scripts/PostgreSQL/010_vcs_connections.sql TerraformRegistry.Tests/DbUpPostgresqlMigrationTests.cs
git commit -m "fix: preserve VCS sources during PostgreSQL migration"
```

### Task 2: Test every PostgreSQL migration with populated data in Docker

**Files:**
- Modify: `TerraformRegistry.Tests/DbUpPostgresqlMigrationTests.cs`

**Interfaces:**
- Consumes: `CreateFreshDatabase()`, `GetEmbeddedScriptNames(string)`, and the safe 010 schema.
- Produces: `EveryPostgresMigrationUpgradesPopulatedDatabaseWithoutDataLoss()`.

- [ ] **Step 1: Write the failing end-to-end test**

Add a test that obtains `scripts = GetEmbeddedScriptNames(".Scripts.PostgreSQL.")`, creates one database, opens one connection, then for each resource:

```csharp
await AssertSeedDataAsync(connection);
MigrateResource(script, connectionString);
await SeedDataAvailableAtCurrentSchemaAsync(connection);
await AssertSeedDataAsync(connection);
```

After the loop, assert the scripts equal `GetPostgresJournalScriptNamesAsync(connection)`, run `AssertForeignKeysAreValidAsync(connection)`, call `new DbUpMigrator(NullLogger<DbUpMigrator>.Instance).Migrate("postgres", connectionString)`, and reassert journal and seed data.

Add `MigrateResource` that uses `DeployChanges.To.PostgresqlDatabase(connectionString)`, filters `WithScriptsEmbeddedInAssembly` to an ordinal-equal resource name, calls `WithTransactionPerScript()`, and asserts `PerformUpgrade().Successful` with the resource name and error.

- [ ] **Step 2: Run the test before helpers are defined**

Run:

```bash
dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter 'FullyQualifiedName~EveryPostgresMigrationUpgradesPopulatedDatabaseWithoutDataLoss'
```

Expected: compilation failure for the undefined seed and foreign-key helpers.

- [ ] **Step 3: Implement seed and assertion helpers**

Implement schema checks using parameterized `information_schema.tables` and `information_schema.columns` queries. Make `SeedDataAvailableAtCurrentSchemaAsync` use idempotent, dependency-ordered `INSERT ... ON CONFLICT ... DO NOTHING` statements for:

- `modules` ID 42 and its `module_downloads` row;
- `users` ID `user-1` and `api_keys`;
- `webhooks`;
- legacy `vcs_sources` before 010, then joined source/connection data after 010;
- `roles` and `user_roles`;
- `audit_logs`;
- `module_extractions`;
- `providers`, `provider_gpg_keys`, `provider_versions`, `provider_platforms`, and `provider_downloads`;
- `runtime_settings`;
- `module_llm_contexts`.

Implement `AssertSeedDataAsync` to assert each stable seed key exists whenever its table exists. When `vcs_sources.connection_id` exists, assert the migrated source joins to a connection preserving the legacy PAT and webhook secret; otherwise assert legacy source credential columns. Implement `AssertForeignKeysAreValidAsync` by querying `pg_constraint` for unvalidated foreign keys and asserting zero rows.

- [ ] **Step 4: Run focused and complete migration integration tests**

Run:

```bash
dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter 'FullyQualifiedName~EveryPostgresMigrationUpgradesPopulatedDatabaseWithoutDataLoss'
dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter 'FullyQualifiedName~DbUpPostgresqlMigrationTests'
```

Expected: PASS with Docker available; every resource is journaled, all seeds survive, foreign keys validate, and the final production migrator invocation is a no-op.

- [ ] **Step 5: Commit the test**

```bash
git add TerraformRegistry.Tests/DbUpPostgresqlMigrationTests.cs
git commit -m "test: cover populated PostgreSQL migration upgrades"
```

### Task 3: Document the historical destructive migration limitation

**Files:**
- Modify: `docs/operations/database-migration-recovery.md`

**Interfaces:**
- Consumes: the existing recovery runbook.
- Produces: explicit backup/external recovery guidance for databases that previously applied destructive 010.

- [ ] **Step 1: Add the runbook section**

Add before `## Approval checklist`:

```markdown
### Historical 010 data loss

The original PostgreSQL 010 migration dropped `vcs_sources`. If it was already
applied, the missing source identities, ownership, module coordinates,
repository details, encrypted PATs, webhook secrets, activation state, and
timestamps cannot be reconstructed from `vcs_connections`. Restore a backup or
recover from an authoritative external system; rotate recovered or potentially
exposed credentials. The corrected 010 protects only databases that have not
yet applied the historical destructive version.
```

- [ ] **Step 2: Verify documentation and migration coverage**

Run:

```bash
scripts/verification/gates/test-release-runbooks-gate.sh
dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter 'FullyQualifiedName~DbUpPostgresqlMigrationTests'
```

Expected: both commands pass.

- [ ] **Step 3: Commit the documentation**

```bash
git add docs/operations/database-migration-recovery.md
git commit -m "docs: record historical VCS migration data loss"
```

## Plan Self-Review

- Spec coverage: Task 1 supplies the safe migration, Task 2 supplies Docker-backed incremental upgrades with data and a no-op final run, and Task 3 records the non-recoverable historical case.
- Placeholder scan: all current PostgreSQL tables through 016 have a named seed and preservation assertion category.
- Type consistency: all helpers use the existing `NpgsqlConnection`, `DbUpMigrator`, DbUp, and Testcontainers APIs already present in the test class.

