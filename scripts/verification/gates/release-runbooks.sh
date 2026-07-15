#!/usr/bin/env bash
set -euo pipefail

ROOT="${RELEASE_RUNBOOK_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)}"
RUNBOOK="$ROOT/docs/release-operations-runbook.md"
ADMIN_ENDPOINTS="$ROOT/TerraformRegistry/Startup/AdminEndpointMappingExtensions.cs"
MIRROR_HANDLERS="$ROOT/TerraformRegistry/Handlers/MirrorAdminHandlers.cs"
MODULE_DOCS_HANDLERS="$ROOT/TerraformRegistry/Handlers/ModuleDocsHandlers.cs"
AUDIT_HANDLERS="$ROOT/TerraformRegistry/Handlers/AuditHandlers.cs"

test -f "$RUNBOOK"

for heading in \
  'Backup, restore, and migration state' \
  'Digest rollout and rollback' \
  'Signing and secret rotation' \
  'Extraction jobs and mirror cache' \
  'Alerts and incident response' \
  'Compatibility windows'; do
  grep -Fq "## $heading" "$RUNBOOK"
done

grep -Fq 'phase-0-migration-recovery-runbook.md' "$RUNBOOK"
grep -Fq 'supply-chain-pinning.sh' "$RUNBOOK"
grep -Fq 'MapGet("/health"' "$ROOT/TerraformRegistry/Startup/EndpointMappingExtensions.cs"
grep -Fq 'MapGet("/ready"' "$ROOT/TerraformRegistry/Startup/EndpointMappingExtensions.cs"

# The runbook's operational commands are contracts with the mapped handlers,
# not merely endpoint names. Keep their route, permission, audit, and response
# semantics aligned with the implementation.
grep -Fq '/api/admin/module-docs/summary' "$RUNBOOK"
grep -Fq '/api/admin/module-docs/modules/<namespace>/<name>/<provider>/<version>/requeue' "$RUNBOOK"
grep -Fq '.queued == true' "$RUNBOOK"
grep -Fq '.queued == false' "$RUNBOOK"
grep -Fq 'module_docs.manage' "$RUNBOOK"
grep -Fq 'Retry only after queue capacity is' "$RUNBOOK"
grep -Fq 'MapGet("/api/admin/module-docs/summary"' "$ADMIN_ENDPOINTS"
grep -Fq 'MapPost("/api/admin/module-docs/modules/{namespace}/{name}/{provider}/{version}/requeue"' "$ADMIN_ENDPOINTS"
grep -Fq 'Permissions.ModuleDocsManage' "$MODULE_DOCS_HANDLERS"
grep -Fq 'extractionService.QueueAsync(' "$MODULE_DOCS_HANDLERS"
grep -Fq 'return Results.Accepted(' "$MODULE_DOCS_HANDLERS"
grep -Fq 'new { queued });' "$MODULE_DOCS_HANDLERS"

grep -Fq 'PUT /api/admin/mirror/config' "$RUNBOOK"
grep -Fq 'mirror.configure' "$RUNBOOK"
grep -Fq 'mirror.config_updated' "$RUNBOOK"
grep -Fq '/api/admin/audit?action=mirror.config_updated' "$RUNBOOK"
grep -Fq 'admin.audit' "$RUNBOOK"
grep -Fq 'MapGet("/api/admin/mirror/config"' "$ADMIN_ENDPOINTS"
grep -Fq 'MapPut("/api/admin/mirror/config"' "$ADMIN_ENDPOINTS"
grep -Fq 'Permissions.MirrorConfigure' "$MIRROR_HANDLERS"
grep -Fq 'UpdateConfigAsync(request, actor, context.RequestAborted)' "$MIRROR_HANDLERS"
grep -Fq '"mirror.config_updated"' "$MIRROR_HANDLERS"
grep -Fq 'return Results.Ok(response);' "$MIRROR_HANDLERS"
grep -Fq 'MapGet("/api/admin/audit"' "$ADMIN_ENDPOINTS"
grep -Fq 'Permissions.AdminAudit' "$AUDIT_HANDLERS"

echo 'Release operations runbook contracts verified.'
