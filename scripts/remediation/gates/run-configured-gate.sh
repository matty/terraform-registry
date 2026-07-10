#!/usr/bin/env bash
set -euo pipefail

phase="${1:?phase is required}"
variable="REMEDIATION_PHASE_${phase//b/_B}_GATE"
command="${!variable:-}"

if [[ -z "$command" ]]; then
  cat >&2 <<EOF
No executable acceptance command is configured for phase $phase.

Set $variable to the exact phase acceptance command (normally the phase-gate test
suite added with that phase), then rerun the remediation verifier. This failure is
intentional: a generic build cannot certify migration, cloud, Terraform, fault, or
load invariants from the remediation plan.
EOF
  exit 1
fi

exec bash -o pipefail -c "$command"
