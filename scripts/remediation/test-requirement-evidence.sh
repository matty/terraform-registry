#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VALIDATOR="$ROOT/scripts/remediation/validate-requirement-evidence.sh"

"$VALIDATOR" --check

copy="$(mktemp -d)"
trap 'rm -rf "$copy"' EXIT
cp -a "$ROOT/." "$copy"

sed -i '0,/^DB-001\t/s//DB-000\t/' "$copy/scripts/remediation/requirement-evidence.tsv"
if "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted a manifest that omits a specification requirement' >&2
  exit 1
fi

rm -rf "$copy"
copy="$(mktemp -d)"
cp -a "$ROOT/." "$copy"
sed -i '0,/^DB-002\t/s/\t[0-9a-f]\{40\}\t/\tmissing-merge-record\t/' "$copy/scripts/remediation/requirement-evidence.tsv"
if "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted a requirement without a merge record' >&2
  exit 1
fi

rm -rf "$copy"
copy="$(mktemp -d)"
cp -a "$ROOT/." "$copy"
sed -i '0,/^DB-003\t/s#scripts/remediation/gates/phase-0.sh#scripts/remediation/gates/missing.sh#' "$copy/scripts/remediation/requirement-evidence.tsv"
if "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted a requirement without an automation gate' >&2
  exit 1
fi

rm -rf "$copy"
copy="$(mktemp -d)"
cp -a "$ROOT/." "$copy"
sed -i '/| Fault and load result |/d' "$copy/docs/release-candidate-evidence.md"
if "$copy/scripts/remediation/validate-requirement-evidence.sh" --check; then
  echo 'validator accepted missing current-candidate evidence' >&2
  exit 1
fi

echo 'Requirement evidence validator tests passed.'
