#!/usr/bin/env bash
set -euo pipefail

# This script runs only from a trusted develop checkout. It reads PR metadata
# through GitHub's API and never checks out or executes code from a PR.

readonly REQUIRED_CHECKS=(
  '.NET build, test, coverage'
  'Frontend build and audit'
  'Docker build and scan'
  'Dependency review'
  'CodeQL (csharp)'
  'CodeQL (javascript-typescript)'
  'CodeQL (actions)'
  'Trivy filesystem scan'
)

repo="${GH_REPO:-$(gh repo view --json nameWithOwner --jq .nameWithOwner)}"
owner="${repo%%/*}"
name="${repo#*/}"

report() {
  local decision="$1"
  local number="${2:-none}"
  printf 'decision=%s pr=%s\n' "$decision" "$number"
}

fail() {
  report "$1" "$2"
  exit 1
}

is_routine_renovate_pr() {
  local pr="$1"
  [[ "$(jq -r '.user.login' <<<"$pr")" == 'app/renovate' ]] || return 1
  [[ "$(jq -r '.head.ref' <<<"$pr")" == renovate/* ]] || return 1
  [[ "$(jq -r '.base.ref' <<<"$pr")" == 'develop' ]] || return 1

  local text labels
  text="$(jq -r '[.title, .body // ""] | join("\n") | ascii_downcase' <<<"$pr")"
  labels="$(jq -r '[.labels[]?.name | ascii_downcase] | join("\n")' <<<"$pr")"

  # Renovate represents security-alert and dashboard-approved major updates in
  # its PR body/title or labels. These always remain maintainer-reviewed.
  if grep -Eqi 'security update|vulnerabilit|cve-[0-9]|(^|[^a-z])major([^a-z]|$)' <<<"$text" ||
     grep -Eqi 'security|vulnerabilit|major|dependency-dashboard' <<<"$labels"; then
    return 1
  fi
}

read_pr() {
  gh api "repos/$repo/pulls/$1"
}

has_comments_or_reviews() {
  local number="$1"
  local comments reviews
  comments="$(gh api "repos/$repo/issues/$number/comments?per_page=1")"
  reviews="$(gh api "repos/$repo/pulls/$number/reviews?per_page=1")"
  [[ "$(jq 'length' <<<"$comments")" -gt 0 || "$(jq 'length' <<<"$reviews")" -gt 0 ]]
}

review_thread_state() {
  local number="$1"
  gh api graphql \
    -f query='query($owner:String!,$name:String!,$number:Int!){repository(owner:$owner,name:$name){pullRequest(number:$number){mergeStateStatus reviewThreads(first:100){nodes{isResolved} pageInfo{hasNextPage}}}}}' \
    -f owner="$owner" \
    -f name="$name" \
    -F number="$number"
}

check_state() {
  local sha="$1"
  local checks statuses
  checks="$(gh api "repos/$repo/commits/$sha/check-runs?per_page=100")"

  local required
  for required in "${REQUIRED_CHECKS[@]}"; do
    if ! jq -e --arg required "$required" '
      [.check_runs[] | select(.name == $required)] as $matching |
      ($matching | length) > 0 and
      (($matching | sort_by(.started_at // .completed_at // "") | last) |
        .status == "completed" and .conclusion == "success")
    ' <<<"$checks" >/dev/null; then
      printf 'failed-required-check\n'
      return 0
    fi
  done

  statuses="$(gh api "repos/$repo/commits/$sha/status")"
  if ! jq -e '
    [.statuses[] | select(.context == "renovate/stability-days")] as $matching |
    ($matching | length) > 0 and
    all($matching[]; .state == "success")
  ' <<<"$statuses" >/dev/null; then
    printf 'pending-stability\n'
    return 0
  fi
  printf 'successful\n'
}

inspect() {
  local number="$1"
  local pr="$2"
  local sha threads

  sha="$(jq -r '.head.sha' <<<"$pr")"
  if has_comments_or_reviews "$number"; then
    printf 'comments-or-reviews\n'
    return 0
  fi

  threads="$(review_thread_state "$number")"
  if [[ "$(jq -r '.data.repository.pullRequest.mergeStateStatus' <<<"$threads")" != 'CLEAN' ]]; then
    printf 'unclean-merge-state\n'
    return 0
  fi
  if [[ "$(jq -r '.data.repository.pullRequest.reviewThreads.pageInfo.hasNextPage' <<<"$threads")" != 'false' ]]; then
    printf 'inconclusive-review-threads\n'
    return 0
  fi
  if jq -e '[.data.repository.pullRequest.reviewThreads.nodes[] | select(.isResolved == false)] | length > 0' <<<"$threads" >/dev/null; then
    printf 'unresolved-review-thread\n'
    return 0
  fi
  case "$(check_state "$sha")" in
    successful) ;;
    pending-stability) printf 'pending-stability\n'; return 0 ;;
    failed-required-check) printf 'failed-required-check\n'; return 0 ;;
    *) printf 'inconclusive-check-state\n'; return 0 ;;
  esac

  printf 'eligible:%s\n' "$sha"
}

prior_develop_workflows_are_successful() {
  local workflow runs
  for workflow in ci.yaml security.yaml; do
    runs="$(gh api "repos/$repo/actions/workflows/$workflow/runs?branch=develop&event=push&per_page=1")"
    jq -e '.workflow_runs | length == 1 and .[0].conclusion == "success"' <<<"$runs" >/dev/null || return 1
  done
}

if ! prior_develop_workflows_are_successful; then
  fail 'prior-develop-workflow-not-successful' none
fi

numbers="$(gh api "repos/$repo/pulls?state=open&base=develop&per_page=100" | jq -r '.[].number')"
if [[ -z "$numbers" ]]; then
  report 'no-open-renovate-pr' none
  exit 0
fi

number=''
initial_pr=''
while IFS= read -r candidate; do
  pr="$(read_pr "$candidate")"
  if is_routine_renovate_pr "$pr"; then
    number="$candidate"
    initial_pr="$pr"
    break
  fi
done <<<"$numbers"

if [[ -z "$number" ]]; then
  report 'no-routine-renovate-pr' none
  exit 0
fi

initial_result="$(inspect "$number" "$initial_pr")"
case "$initial_result" in
  eligible:*) initial_sha="${initial_result#eligible:}" ;;
  comments-or-reviews) report 'skipped-comments-or-reviews' "$number"; exit 0 ;;
  unresolved-review-thread) report 'skipped-unresolved-review-thread' "$number"; exit 0 ;;
  pending-stability) report 'skipped-pending-stability' "$number"; exit 0 ;;
  unclean-merge-state|inconclusive-review-threads) fail "$initial_result" "$number" ;;
  failed-required-check) fail 'failed-required-check' "$number" ;;
  *) fail 'unexpected-inspection-result' "$number" ;;
esac

final_pr="$(read_pr "$number")"
if ! is_routine_renovate_pr "$final_pr"; then
  fail 'pr-no-longer-routine-renovate' "$number"
fi
final_sha="$(jq -r '.head.sha' <<<"$final_pr")"
if [[ "$final_sha" != "$initial_sha" ]]; then
  fail 'changed-head' "$number"
fi

final_result="$(inspect "$number" "$final_pr")"
case "$final_result" in
  "eligible:$initial_sha") ;;
  comments-or-reviews) report 'skipped-comments-or-reviews' "$number"; exit 0 ;;
  unresolved-review-thread) report 'skipped-unresolved-review-thread' "$number"; exit 0 ;;
  pending-stability) report 'skipped-pending-stability' "$number"; exit 0 ;;
  unclean-merge-state|inconclusive-review-threads) fail "$final_result" "$number" ;;
  failed-required-check) fail 'failed-required-check' "$number" ;;
  *) fail 'changed-head-or-checks' "$number" ;;
esac

if ! prior_develop_workflows_are_successful; then
  fail 'prior-develop-workflow-not-successful' "$number"
fi

merge_response="$(gh api -X PUT "repos/$repo/pulls/$number/merge" \
  -f merge_method=rebase \
  -f sha="$initial_sha")"
if ! jq -e '.merged == true' <<<"$merge_response" >/dev/null; then
  fail 'merge-not-completed' "$number"
fi
report 'merged' "$number"
