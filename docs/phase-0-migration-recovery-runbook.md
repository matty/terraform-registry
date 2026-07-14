# Phase 0 migration backup, restore, and unsafe-state recovery

This operator runbook implements `DB-006`. It is deliberately fail-closed: do
not delete DbUp journal rows, renumber an embedded migration, manually alter a
production schema, or restart a failing instance until the state is classified
and a reviewed recovery path is selected.

## Required evidence

Before a schema rollout, record the environment/database, exact deployed
application SHA, old and proposed image digests, operator, UTC start/end times,
full `SchemaVersions` journal, whether the deployed binary contains PostgreSQL or
SQLite 010, backup ID/location/retention, and disposable restore target. Record
source/restored row counts for every application table, table-specific field and
relationship assertions, backup/restore/migration duration, and observed lock
wait or blocking (or explicitly that none was observed). Attach the exact
candidate-binary readiness result and Phase 0 matrix output.

A successful disposable exercise is not evidence that production was restored.

## Establish deployed and DbUp state

Obtain the deployed SHA/image digest from the deployment platform, then save the
journal output as an artifact. Do not put connection strings in the ticket.

```bash
psql "$PGURL" -X -v ON_ERROR_STOP=1 \
  -c 'SELECT ScriptName, Applied FROM "SchemaVersions" ORDER BY ScriptName;'
sqlite3 -readonly "$SQLITE_DB" \
  'SELECT ScriptName, Applied FROM SchemaVersions ORDER BY ScriptName;'
```

If the journal is absent, non-contiguous, contains an unknown script, or says 010
was applied while its required schema shape is absent, stop. Preserve readiness
logs and use the unsafe-state path below.

## PostgreSQL: backup and disposable restore

Use a unique, empty disposable restore database. These commands do not roll back
production.

```bash
set -euo pipefail
: "${PGURL:?source URL from secret manager}"
: "${RESTORE_PGURL:?empty disposable restore database URL}"
workdir="$(mktemp -d)"
started="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
pg_dump --dbname="$PGURL" --format=custom --file="$workdir/phase-0.dump" --verbose
pg_restore --dbname="$RESTORE_PGURL" --clean --if-exists --no-owner --verbose "$workdir/phase-0.dump"

# Compare all public-table row counts; preserve both files and their diff.
for url in "$PGURL" "$RESTORE_PGURL"; do
  name=$([ "$url" = "$PGURL" ] && echo source || echo restored)
  psql "$url" -X -At -v ON_ERROR_STOP=1 <<'SQL' >"$workdir/$name-row-counts.txt"
SELECT format('SELECT %L || ''|'' || count(*) FROM %I.%I;',
              schemaname || '.' || tablename, schemaname, tablename)
FROM pg_catalog.pg_tables
WHERE schemaname = 'public'
ORDER BY tablename;
\gexec
SQL
done
diff -u "$workdir/source-row-counts.txt" "$workdir/restored-row-counts.txt"
printf 'started=%s\nfinished=%s\n' "$started" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
  >"$workdir/timing.txt"
```

The count query is only a template; the Phase 0 matrix's table-specific field and
relationship assertions are authoritative. Capture timestamped `pg_stat_activity`
and `pg_locks` snapshots from approved monitoring while backup/migration runs,
then record longest wait and blocker PID/query. Start the exact candidate image
against `RESTORE_PGURL`, run the Phase 0 gate, and preserve its output.

## SQLite: backup and disposable restore

For a disposable SQLite database, the checked-in command performs an online
backup, restores it to a separate file, compares all user-table row counts and
the DbUp journal, and runs `foreign_key_check`:

```bash
scripts/verification/phase-0-backup-restore-evidence.sh sqlite \
  /path/to/disposable-registry.db ./artifacts/phase-0-sqlite-restore
```

Attach the generated Markdown record and comparison files. The script never
migrates or writes to its source. For a live SQLite file, first take an
application-consistent copy using the platform's documented quiesce/snapshot
procedure; do not copy a live WAL database with `cp`.

## Unsafe-state recovery

1. Remove the instance from traffic; preserve readiness failure/logs, deployed
   SHA/image digest, journal/schema metadata, and a new backup.
2. Classify: unjournaled 010 may take forward migration; complete journaled 010
   may take forward validation/repair; incomplete or unknown journaled state must
   not receive best-effort SQL.
3. Restore the last known-good backup into a disposable target. Validate row
   counts, fields/relationships, journal, locks/duration, and the exact repair
   binary before approving production recovery.
4. Restore the approved backup through the platform change process and deploy the
   reviewed forward repair. This is a roll-forward recovery, never a journal edit
   or manual schema rollback.
5. Retain the failed snapshot and evidence through incident review and backup
   retention obligations.

### Limitation: original 010 data loss

If an already-journaled defective PostgreSQL 010 deleted `vcs_sources`, the
application cannot reconstruct source identity/ownership, namespace/name/provider,
repository owner/name, encrypted PAT, webhook secret, active flag, or timestamps.
`vcs_connections` cannot recreate them. Recover only from an external backup or
an authoritative external system after security review; rotate recovered or
potentially exposed PATs and webhook secrets. Record this limitation explicitly.

## Approval checklist

- [ ] Exact deployed SHA and immutable image digests recorded.
- [ ] DbUp journal and 010 classification recorded for every target.
- [ ] Backup identifier/retention and disposable restore target recorded.
- [ ] Restore row counts, relationships, journal, and SQLite foreign keys (where
      applicable) compared successfully.
- [ ] Backup/restore/migration durations and lock/blocking observations recorded.
- [ ] Exact release binary passed readiness and the Phase 0 matrix on the restore.
- [ ] Irrecoverable-data limitation acknowledged, or evidence shows it does not
      apply.
