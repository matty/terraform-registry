#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MANIFEST="$ROOT/scripts/remediation/manifest.tsv"
STATUS="$ROOT/docs/remediation-status.md"

usage() {
  cat <<'EOF'
Usage:
  scripts/remediation/remediation.sh status
  scripts/remediation/remediation.sh packages <phase>
  scripts/remediation/remediation.sh verify <phase>
  scripts/remediation/remediation.sh complete <phase> <evidence-file>

verify runs the common Release checks and the phase-specific gate command. It does
not mark a phase complete. complete verifies again, requires explicit acceptance
evidence, and then updates the checked-off ledger.
EOF
}

phase_exists() {
  awk -F '\t' -v phase="$1" 'NR > 1 && $1 == phase { found = 1 } END { exit !found }' "$MANIFEST"
}

packages() {
  awk -F '\t' -v phase="$1" 'NR > 1 && $1 == phase { printf "%-18s %-52s %s\n", $2, $3, $4 }' "$MANIFEST"
}

require_phase() {
  if ! phase_exists "$1"; then
    echo "Unknown phase '$1'. Expected one of: 0, 1, 2, 2b, 3, 4, 5, 6." >&2
    exit 64
  fi
}

run_common_checks() {
  cd "$ROOT"
  git diff --check
  dotnet restore -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=high '-warnaserror:NU1903;NU1904'
  dotnet build terraform-registry.sln --no-restore --configuration Release
  ASPNETCORE_ENVIRONMENT=Test dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --no-build --configuration Release
  npm --prefix TerraformRegistry/web-src ci
  npm --prefix TerraformRegistry/web-src audit --audit-level=high
  npm --prefix TerraformRegistry/web-src run generate
  docker build --tag "terraform-registry:${USER:-local}-audit" .
}

verify() {
  local phase="$1"
  local gate="$ROOT/scripts/remediation/gates/phase-${phase}.sh"
  require_phase "$phase"
  run_common_checks
  if [[ ! -x "$gate" ]]; then
    echo "Missing executable phase gate: ${gate#$ROOT/}" >&2
    exit 1
  fi
  "$gate"
}

complete() {
  local phase="$1"
  local evidence="$2"
  require_phase "$phase"
  [[ -f "$evidence" ]] || { echo "Evidence file not found: $evidence" >&2; exit 66; }
  verify "$phase"

  while IFS=$'\t' read -r package _ _ _; do
    [[ "$package" == "package" ]] && continue
    grep -Fqx -- "- [x] $package" "$evidence" || {
      echo "Evidence must contain: - [x] $package" >&2
      exit 1
    }
  done < <(awk -F '\t' -v phase="$phase" 'NR > 1 && $1 == phase { print $2 "\t" $3 "\t" $4 "\t" $5 }' "$MANIFEST")
  grep -Fqx -- '- [x] Phase gate' "$evidence" || { echo 'Evidence must contain: - [x] Phase gate' >&2; exit 1; }
  grep -Fqx -- '- [x] Phase PR merged to develop' "$evidence" || { echo 'Evidence must contain: - [x] Phase PR merged to develop' >&2; exit 1; }
  grep -Fqx -- '- [x] Post-merge develop workflow green' "$evidence" || { echo 'Evidence must contain: - [x] Post-merge develop workflow green' >&2; exit 1; }

  local stamp
  stamp="$(date -u +%Y-%m-%dT%H:%M:%SZ) @ $(git -C "$ROOT" rev-parse HEAD)"
  sed -i -E "s|^(\| ${phase} \| [^|]+ \|) Pending( \|.*)$|\1 Verified ${stamp}\2|" "$STATUS"
  echo "Phase $phase recorded as verified in ${STATUS#$ROOT/}."
}

case "${1:-}" in
  status) sed -n '/^## Phase status/,$p' "$STATUS" ;;
  packages) [[ $# -eq 2 ]] || { usage >&2; exit 64; }; require_phase "$2"; packages "$2" ;;
  verify) [[ $# -eq 2 ]] || { usage >&2; exit 64; }; verify "$2" ;;
  complete) [[ $# -eq 3 ]] || { usage >&2; exit 64; }; complete "$2" "$3" ;;
  *) usage >&2; exit 64 ;;
esac
