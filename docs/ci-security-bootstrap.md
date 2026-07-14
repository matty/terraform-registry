# CI and security remediation bootstrap

The CI and Security workflows run for pushes and pull requests affecting
`remediation/phase/**`, and for GitHub merge-queue `merge_group` checks. The
following checks are intended to be required for every remediation phase:

- CI / .NET build, test, coverage
- CI / Frontend build and audit
- CI / Docker build and scan
- Security / Dependency review (pull requests)
- Security / CodeQL
- Security / Trivy filesystem scan

`npm audit --audit-level=high` and the Trivy filesystem scan both fail the
workflow for HIGH or CRITICAL findings. Exceptions require a separately
reviewed, time-bounded policy change; they must not be made non-blocking in a
remediation branch.

`DEV-DOCKER-001` is the sole current exception: `Dockerfile.dev` is excluded
from the filesystem scan until `P1-DOCKER` makes that development-only image
non-root. It expires on 2026-09-30 and does not affect the production
`Dockerfile`, which remains scanned and blocking.

Merge-queue and pull-request jobs have read-only repository permissions. Image
publication is in the separate `Publish Docker image` job, which is eligible
only for `push` events or an explicitly requested `workflow_dispatch` with
`push_image=true`. Consequently, a `merge_group` build cannot log into GHCR,
push an image, create a tag, or receive registry/OIDC write permissions.

## Renovate merge watcher

`Renovate merge watcher` runs only from the trusted `develop` revision on an
hourly schedule or by manual dispatch. It examines routine Renovate PRs without
checking out or executing their code, and can merge at most one with a rebase.
It requires the protected CI/security checks, `renovate/stability-days`, a
clean current merge state, an unchanged head SHA, and no comments, reviews, or
unresolved review threads. A pending stability status is never mergeable.
Queued, in-progress, or superseded-cancelled required checks defer the watcher
successfully; a completed non-success check remains blocking and is reported as
a watcher failure.

Renovate applies `automerge-candidate` only to digest, pin, patch, and minor
updates. The watcher requires that label and rejects `security`,
`dependency-dashboard`, and major labels, so vulnerability-alert and
dashboard-approved major updates remain maintainer-reviewed. Human-authored
PRs are never eligible for this automation.

Before the final rebase request, the watcher verifies that the PR's `develop`
base SHA is unchanged and that the CI and Security push runs for that exact
SHA succeeded. This public personal repository cannot use GitHub merge queues;
when a competing `develop` merge is in progress, the watcher defers rather than
merging another dependency update.

## Repository settings required outside this repository

An administrator must create a branch ruleset matching `remediation/phase/**`
that requires pull requests, the checks above as applicable, at least one
approval, resolved conversations, linear history, and no force pushes. Workflow
filters alone do not provide branch protection.

For repositories eligible for GitHub merge queues, an administrator should
enable one for `develop` and configure the current CI and Security check names
as required status checks. GitHub does not offer merge queues to this public
personal repository, so the Renovate watcher provides the documented
single-update/base-recheck fallback.

Protected cloud integration environments are configured separately. Their OIDC
federated credentials must trust only the intended repository, workflow, and
protected remediation/develop branch or merge-group subject; no cloud credential
or federated subject may be available to a fork pull-request workflow.
