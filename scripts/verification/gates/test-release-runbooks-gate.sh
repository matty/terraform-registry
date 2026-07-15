#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
GATE="$ROOT/scripts/verification/gates/release-runbooks.sh"

test -x "$GATE"

assert_rejects() {
  local file="$1"
  local from="$2"
  local to="$3"
  local copy
  copy="$(mktemp -d)"
  trap 'rm -rf "$copy"' RETURN
  cp -a "$ROOT/." "$copy"
  FROM="$from" TO="$to" perl -0pi -e 's/\Q$ENV{FROM}\E/$ENV{TO}/g' "$copy/$file"
  if RELEASE_RUNBOOK_ROOT="$copy" bash "$GATE"; then
    echo "release runbook gate accepted a mutated $file contract" >&2
    return 1
  fi
  rm -rf "$copy"
  trap - RETURN
}

assert_rejects \
  'TerraformRegistry/Startup/AdminEndpointMappingExtensions.cs' \
  'app.MapPut("/api/admin/mirror/config"' \
  'app.MapPost("/api/admin/mirror/config"'
assert_rejects \
  'TerraformRegistry/Handlers/MirrorAdminHandlers.cs' \
  'Permissions.MirrorConfigure' \
  'Permissions.MirrorManage'
assert_rejects \
  'TerraformRegistry/Handlers/MirrorAdminHandlers.cs' \
  '"mirror.config_updated"' \
  '"mirror.config_changed"'
assert_rejects \
  'TerraformRegistry/Handlers/ModuleDocsHandlers.cs' \
  'new { queued });' \
  'new { accepted = queued });'
assert_rejects \
  'docs/release-operations-runbook.md' \
  '.queued == true' \
  '.queued == false'
