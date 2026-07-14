#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT="$ROOT/scripts/automation/merge-renovate-pr.sh"

test -x "$SCRIPT"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/bin"

cat >"$tmp/bin/gh" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

args=" $* "
fixture="${WATCHER_FIXTURE:?WATCHER_FIXTURE must be set}"
state_dir="${WATCHER_STATE_DIR:?WATCHER_STATE_DIR must be set}"

if [[ "$args" == *"/actions/workflows/ci.yaml/runs?"* || "$args" == *"/actions/workflows/security.yaml/runs?"* ]]; then
  printf '%s\n' '{"workflow_runs":[{"conclusion":"success"}]}'
  exit 0
fi

if [[ "$args" == *"/pulls?state=open&base=develop"* ]]; then
  printf '%s\n' '[{"number":42}]'
  exit 0
fi

if [[ "$args" == *"/pulls/42 "* ]]; then
  count_file="$state_dir/pr_reads"
  count=0
  [[ -f "$count_file" ]] && count="$(<"$count_file")"
  count=$((count + 1))
  printf '%s' "$count" >"$count_file"
  head_sha="head-a"
  if [[ "$fixture" == "changed-head" && "$count" -gt 1 ]]; then
    head_sha="head-b"
  fi
  printf '{"number":42,"user":{"login":"app/renovate"},"head":{"ref":"renovate/pin-postgres","sha":"%s"},"base":{"ref":"develop"},"title":"chore(deps): pin postgres","body":"Routine update","labels":[]}' "$head_sha"
  exit 0
fi

if [[ "$args" == *"/issues/42/comments?"* ]]; then
  if [[ "$fixture" == "comments" ]]; then
    printf '%s\n' '[{"id":1}]'
  else
    printf '%s\n' '[]'
  fi
  exit 0
fi

if [[ "$args" == *"/pulls/42/reviews?"* ]]; then
  printf '%s\n' '[]'
  exit 0
fi

if [[ "$args" == *" graphql "* ]]; then
  if [[ "$fixture" == "unresolved-threads" ]]; then
    printf '%s\n' '{"data":{"repository":{"pullRequest":{"mergeStateStatus":"CLEAN","reviewThreads":{"nodes":[{"isResolved":false}],"pageInfo":{"hasNextPage":false}}}}}}'
  elif [[ "$fixture" == "unclean-merge-state" ]]; then
    printf '%s\n' '{"data":{"repository":{"pullRequest":{"mergeStateStatus":"DIRTY","reviewThreads":{"nodes":[],"pageInfo":{"hasNextPage":false}}}}}}'
  else
    printf '%s\n' '{"data":{"repository":{"pullRequest":{"mergeStateStatus":"CLEAN","reviewThreads":{"nodes":[],"pageInfo":{"hasNextPage":false}}}}}}'
  fi
  exit 0
fi

if [[ "$args" == *"/commits/"*"/check-runs?"* ]]; then
  stability='{"name":"renovate/stability-days","status":"completed","conclusion":"success"}'
  if [[ "$fixture" == "pending-stability" ]]; then
    stability='{"name":"renovate/stability-days","status":"in_progress","conclusion":null}'
  fi
  required='[{"name":".NET build, test, coverage","status":"completed","conclusion":"success"},{"name":"Frontend build and audit","status":"completed","conclusion":"success"},{"name":"Docker build and scan","status":"completed","conclusion":"success"},{"name":"Dependency review","status":"completed","conclusion":"success"},{"name":"CodeQL (csharp)","status":"completed","conclusion":"success"},{"name":"CodeQL (javascript-typescript)","status":"completed","conclusion":"success"},{"name":"CodeQL (actions)","status":"completed","conclusion":"success"},{"name":"Trivy filesystem scan","status":"completed","conclusion":"success"}]'
  if [[ "$fixture" == "failed-required-check" ]]; then
    required='[{"name":".NET build, test, coverage","status":"completed","conclusion":"failure"},{"name":"Frontend build and audit","status":"completed","conclusion":"success"},{"name":"Docker build and scan","status":"completed","conclusion":"success"},{"name":"Dependency review","status":"completed","conclusion":"success"},{"name":"CodeQL (csharp)","status":"completed","conclusion":"success"},{"name":"CodeQL (javascript-typescript)","status":"completed","conclusion":"success"},{"name":"CodeQL (actions)","status":"completed","conclusion":"success"},{"name":"Trivy filesystem scan","status":"completed","conclusion":"success"}]'
  fi
  printf '{"check_runs":%s}' "$(printf '%s' "$required" | jq --argjson stability "$stability" '. + [$stability]')"
  exit 0
fi

if [[ "$args" == *" -X PUT "* && "$args" == *"/pulls/42/merge "* ]]; then
  [[ "$args" == *" merge_method=rebase "* && "$args" == *" sha=head-a "* ]] || {
    echo "merge was not bound to the rechecked head with rebase: $*" >&2
    exit 98
  }
  printf '%s\n' merge >>"$state_dir/calls"
  printf '%s\n' '{"merged":true}'
  exit 0
fi

printf 'unexpected gh invocation: %s\n' "$*" >&2
exit 99
EOF
chmod +x "$tmp/bin/gh"

run_fixture() {
  local fixture="$1"
  local expected_exit="$2"
  local expected_decision="$3"
  local state_dir="$tmp/$fixture"
  mkdir -p "$state_dir"

  set +e
  PATH="$tmp/bin:$PATH" GH_REPO=example/registry WATCHER_FIXTURE="$fixture" WATCHER_STATE_DIR="$state_dir" "$SCRIPT" >"$state_dir/output" 2>&1
  local actual_exit=$?
  set -e

  if [[ "$actual_exit" -ne "$expected_exit" ]]; then
    cat "$state_dir/output" >&2
    echo "$fixture exited $actual_exit; expected $expected_exit" >&2
    exit 1
  fi
  if [[ -e "$state_dir/calls" ]]; then
    echo "$fixture invoked merge: $fixture" >&2
    exit 1
  fi
  grep -Fq "decision=$expected_decision pr=42" "$state_dir/output"
}

run_fixture pending-stability 0 skipped-pending-stability
run_fixture comments 0 skipped-comments-or-reviews
run_fixture unresolved-threads 0 skipped-unresolved-review-thread
run_fixture failed-required-check 1 failed-required-check
run_fixture changed-head 1 changed-head
run_fixture unclean-merge-state 1 unclean-merge-state

eligible_state="$tmp/eligible"
mkdir -p "$eligible_state"
PATH="$tmp/bin:$PATH" GH_REPO=example/registry WATCHER_FIXTURE=eligible WATCHER_STATE_DIR="$eligible_state" "$SCRIPT" >"$eligible_state/output" 2>&1
test "$(wc -l <"$eligible_state/calls")" -eq 1
grep -Fqx 'decision=merged pr=42' "$eligible_state/output"

echo 'merge-renovate-pr tests: 7 fixtures passed'
