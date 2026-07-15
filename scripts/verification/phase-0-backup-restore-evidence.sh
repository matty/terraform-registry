#!/usr/bin/env bash
set -euo pipefail

# Produces reproducible DB-006 evidence for a *disposable* SQLite copy. It never
# migrates or writes to the supplied source database: SQLite's online .backup is
# used, then the backup is restored to a new file and compared with the source.

usage() {
  cat <<'EOF'
Usage: scripts/verification/phase-0-backup-restore-evidence.sh sqlite <source.db> [evidence-dir]

The source must be a disposable SQLite database. The command creates an online
backup and a separate restored database, compares every user-table row count and
DbUp journal row, runs foreign_key_check, and writes a Markdown evidence record.
EOF
}

[[ ${1:-} == sqlite && $# -ge 2 && $# -le 3 ]] || { usage >&2; exit 64; }
command -v sqlite3 >/dev/null || { echo 'sqlite3 is required.' >&2; exit 69; }

source_db=$2
[[ -f "$source_db" ]] || { echo "Source database not found: $source_db" >&2; exit 66; }
evidence_dir=${3:-"$(mktemp -d "${TMPDIR:-/tmp}/terraform-registry-p0-restore.XXXXXX")"}
mkdir -p "$evidence_dir"
backup_db="$evidence_dir/backup.sqlite"
restored_db="$evidence_dir/restored.sqlite"
source_counts="$evidence_dir/source-row-counts.txt"
restored_counts="$evidence_dir/restored-row-counts.txt"
source_journal="$evidence_dir/source-journal.txt"
restored_journal="$evidence_dir/restored-journal.txt"
foreign_key_check="$evidence_dir/restored-foreign-key-check.txt"
record="$evidence_dir/phase-0-backup-restore-evidence.md"

table_counts() {
  local database=$1 output=$2 table count
  sqlite3 -readonly "$database" \
    "SELECT name FROM sqlite_schema WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;" |
  while IFS= read -r table; do
    [[ -n "$table" ]] || continue
    count=$(sqlite3 -readonly "$database" "SELECT COUNT(*) FROM \"${table//\"/\"\"}\";")
    printf '%s|%s\n' "$table" "$count"
  done >"$output"
}

journal() {
  local database=$1 output=$2
  if sqlite3 -readonly "$database" "SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'SchemaVersions';" | grep -qx 1; then
    sqlite3 -readonly "$database" 'SELECT ScriptName FROM SchemaVersions ORDER BY ScriptName;' >"$output"
  else
    printf '%s\n' '<SchemaVersions table absent>' >"$output"
  fi
}

started_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)
started_ns=$(date +%s%N)
sqlite3 "$source_db" ".timeout 5000" ".backup '$backup_db'"
sqlite3 "$restored_db" ".restore '$backup_db'"
finished_ns=$(date +%s%N)
finished_at=$(date -u +%Y-%m-%dT%H:%M:%SZ)
duration_ms=$(( (finished_ns - started_ns) / 1000000 ))

table_counts "$source_db" "$source_counts"
table_counts "$restored_db" "$restored_counts"
diff -u "$source_counts" "$restored_counts" >"$evidence_dir/row-count-diff.txt" || {
  cat "$evidence_dir/row-count-diff.txt" >&2
  echo 'Restore row-count comparison failed.' >&2
  exit 1
}
journal "$source_db" "$source_journal"
journal "$restored_db" "$restored_journal"
diff -u "$source_journal" "$restored_journal" >"$evidence_dir/journal-diff.txt" || {
  cat "$evidence_dir/journal-diff.txt" >&2
  echo 'Restore DbUp journal comparison failed.' >&2
  exit 1
}
sqlite3 -readonly "$restored_db" 'PRAGMA foreign_key_check;' >"$foreign_key_check"
[[ ! -s "$foreign_key_check" ]] || { cat "$foreign_key_check" >&2; echo 'Restored database has foreign-key violations.' >&2; exit 1; }

cat >"$record" <<EOF
# Phase 0 SQLite backup/restore evidence

This record was generated against a disposable SQLite database. It is not
evidence of a production restore.

| Field | Value |
|---|---|
| Started (UTC) | $started_at |
| Finished (UTC) | $finished_at |
| Backup + restore duration | ${duration_ms} ms |
| Source database | $source_db |
| Backup database | $backup_db |
| Restored database | $restored_db |
| SQLite journal mode (source) | $(sqlite3 -readonly "$source_db" 'PRAGMA journal_mode;') |
| SQLite locking mode (source) | $(sqlite3 -readonly "$source_db" 'PRAGMA locking_mode;') |
| Busy timeout used for backup | 5000 ms |
| Deploy/image SHA | RECORD BEFORE APPROVAL |
| DbUp journal state | $source_journal |
| Row-count comparison | passed; see source-row-counts.txt and restored-row-counts.txt |
| DbUp journal comparison | passed; see source-journal.txt and restored-journal.txt |
| Restored foreign_key_check | passed (empty result) |

Required reviewer additions: exact deployed SHA/image digest, backup retention
location, observed lock wait/blocking, PostgreSQL evidence where applicable, and
the exact application binary exercised against the restored copy.
EOF

echo "SQLite backup/restore evidence written to: $record"
