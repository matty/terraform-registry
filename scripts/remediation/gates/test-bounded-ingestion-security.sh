#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
GATE="$ROOT/scripts/remediation/gates/bounded-ingestion-security.sh"

test -x "$GATE"
grep -Fq 'ArchiveWorkspaceFactoryTests' "$GATE"
grep -Fq 'ProviderRegistryServiceTests' "$GATE"
grep -Fq 'VcsWebhookHandlersTests' "$GATE"
grep -Fq 'ApiKeyServiceSecurityTests' "$GATE"
grep -Fq 'ArtifactDownloadTokenServiceTests' "$GATE"
grep -Fq 'RateLimitOptionsTests' "$GATE"
grep -Fq 'ProviderMirrorEndpointTests' "$GATE"
grep -Eq '^  bounded-ingestion-security-gate:' "$ROOT/.github/workflows/ci.yaml"
grep -Fq 'bounded-ingestion-security.sh' "$ROOT/.github/workflows/ci.yaml"
