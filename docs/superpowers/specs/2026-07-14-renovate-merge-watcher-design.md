# Renovate Merge Watcher Design

## Purpose

Automate only routine Renovate dependency PRs after their own policy gates are
complete. The watcher never evaluates, checks out, runs, or merges code from a
human-authored PR.

## Scope

The trusted `develop` workflow runs on a schedule and by manual dispatch. It
uses GitHub's API to inspect open PRs, then delegates the eligibility decision
to a checked-in Bash script. A qualifying PR is rechecked immediately before a
rebase merge. Each run processes at most one PR, so the subsequent `develop`
CI and security workflows finish before another dependency update can merge.

## Eligibility

The script accepts a PR only when all of the following are true:

- the author is `app/renovate`, the head branch starts with `renovate/`, and
  the base branch is `develop`;
- it has no comments, reviews, or unresolved review threads;
- its `mergeStateStatus` is `CLEAN` and the head SHA is unchanged between the
  initial and final checks;
- each of the eight protected `develop` checks is `SUCCESS` for the current
  head commit; and
- normal Renovate updates have `renovate/stability-days` in `success` state.

The watcher skips vulnerability-alert PRs and dashboard-approved major updates;
those remain explicitly reviewed. It also skips every PR when the prior
`develop` post-merge CI or security run is not successful.

## Security and failure handling

The workflow does not use `pull_request_target`, does not check out PR code,
and grants only `contents: read` and `pull-requests: write`. The script runs
from the trusted default branch checkout. A skipped or rejected PR is reported
as an auditable workflow summary and exits successfully. API failures, failed
required checks, an inconclusive merge state, or a merge error fail the watcher
without attempting another PR.

## Verification

Shell contract tests use a fake `gh` executable to prove that pending stability,
comments, unresolved threads, unsuccessful checks, changing heads, and an
unclean merge state never invoke the merge API. A positive fixture proves the
script requests a rebase merge exactly once. The workflow contract test verifies
the trusted trigger, minimal permissions, no PR checkout, serial concurrency,
and script invocation.
