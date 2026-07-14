#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
workflow="$root/.github/workflows/renovate-merge-watcher.yaml"

test -f "$workflow"

grep -Fxq 'name: Renovate merge watcher' "$workflow"
grep -Fxq '  schedule:' "$workflow"
grep -Fxq '  workflow_dispatch:' "$workflow"
! grep -Fq 'pull_request_target:' "$workflow"
grep -Fxq '  contents: read' "$workflow"
grep -Fxq '  pull-requests: write' "$workflow"
grep -Fq 'cancel-in-progress: false' "$workflow"
grep -Fq 'bash scripts/automation/merge-renovate-pr.sh' "$workflow"
! grep -Fq 'ref: ${{ github.event.pull_request.head.sha }}' "$workflow"
! grep -Fq 'ref: ${{ github.event.pull_request.head.ref }}' "$workflow"
