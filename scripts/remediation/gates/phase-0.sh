#!/usr/bin/env bash
set -euo pipefail

# This is deliberately a concrete command rather than a generic build: the matrix
# includes SQLite fresh/populated/journaled/interrupted cases and the PostgreSQL
# Testcontainer suite needed to certify the corresponding production path.
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$ROOT"

ASPNETCORE_ENVIRONMENT=Test dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj \
  --configuration Release \
  --filter '(FullyQualifiedName~MigrationAcceptanceMatrixTests|FullyQualifiedName~DbUpPostgresqlMigrationTests)'

# DB-006 portable evidence check. It uses a disposable SQLite fixture and verifies
# online backup/restore, all user-table row counts, DbUp journal, and foreign keys.
# PostgreSQL and production evidence remain operator-recorded in the runbook.
temporary_database="$(mktemp "${TMPDIR:-/tmp}/terraform-registry-p0-restore.XXXXXX.db")"
temporary_evidence="$(mktemp -d "${TMPDIR:-/tmp}/terraform-registry-p0-evidence.XXXXXX")"
trap 'rm -f "$temporary_database"; rm -rf "$temporary_evidence"' EXIT
sqlite3 "$temporary_database" <<'SQL'
PRAGMA foreign_keys = ON;
CREATE TABLE parent (id INTEGER PRIMARY KEY, value TEXT NOT NULL);
CREATE TABLE child (id INTEGER PRIMARY KEY, parent_id INTEGER NOT NULL REFERENCES parent(id));
CREATE TABLE SchemaVersions (ScriptName TEXT NOT NULL, Applied TEXT NOT NULL);
INSERT INTO parent VALUES (1, 'preserved');
INSERT INTO child VALUES (1, 1);
INSERT INTO SchemaVersions VALUES ('010_schema_fixes.sql', '2026-07-10T00:00:00Z');
SQL
scripts/remediation/phase-0-backup-restore-evidence.sh sqlite "$temporary_database" "$temporary_evidence"
