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

The watcher excludes vulnerability-alert updates and dashboard-approved major
updates. It also waits for the previous `develop` CI and Security runs to
succeed before considering another dependency PR. Human-authored PRs are never
eligible for this automation.

## Repository settings required outside this repository

An administrator must create a branch ruleset matching `remediation/phase/**`
that requires pull requests, the checks above as applicable, at least one
approval, resolved conversations, linear history, and no force pushes. Workflow
filters alone do not provide branch protection.

The administrator must also enable a merge queue for `develop` and configure
the current CI and Security check names as required status checks. The queue
must send the `merge_group` event; this repository's workflows already handle
that event.

Protected cloud integration environments are configured separately. Their OIDC
federated credentials must trust only the intended repository, workflow, and
protected remediation/develop branch or merge-group subject; no cloud credential
or federated subject may be available to a fork pull-request workflow.
