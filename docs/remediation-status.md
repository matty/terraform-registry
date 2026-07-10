# Terraform Registry remediation status

This is the generated-by-command acceptance ledger for the remediation program. It
is intentionally separate from the delivery plan: a phase is only marked verified
by `scripts/remediation/remediation.sh complete` after its executable gate, package
evidence, phase merge, and post-merge `develop` workflow are all confirmed.

Use `scripts/remediation/remediation.sh status` to view it, `packages <phase>` to
view the work queue, and `verify <phase>` to run the common and phase-specific
checks. Each phase-specific gate lives in `scripts/remediation/gates/` and must be
implemented alongside its phase acceptance tests; absent gates fail closed.

## Phase status

| Phase | Name | Status | Verification record |
|---:|---|---|---|
| 0 | Migration safety | Pending | — |
| 1 | Release interoperability and immediate authorization | Pending | — |
| 2 | Bounded ingestion and identity policy | Pending | — |
| 2b | Mirror containment | Pending | — |
| 3 | Transactional publication and durable extraction | Pending | — |
| 4 | Operator control and storage operations | Pending | — |
| 5 | Durable side effects and cross-cutting performance | Pending | — |
| 6 | Release certification | Pending | — |

## Bootstrap status

| Package | Status | Verification record |
|---|---|---|
| SUP-001 NuGet advisory baseline | Ready for PR | `NSwag.AspNetCore` 14.7.1 plus direct `Microsoft.OpenApi` 2.7.5; audit restore reports no vulnerable NuGet packages and the Release build passes. Aggregate integration-suite certification remains pending its stable completion record. |
| Documentation/program approval | Pending | — |
| Phase-gate CI and protected environments | Pending | — |

## Current work

- [x] P0-GUARD — fail-closed journal/schema validation and startup ordering; targeted tests pass.
- [x] P0-PG — non-destructive VCS repair/backfill and malformed journal-state rejection; focused Testcontainers tests pass.
- [x] P0-SQLITE — FK-safe 010 rebuild with populated, atomic, and rerun coverage; targeted tests pass.
- [x] P0-MATRIX — executable SQLite/PostgreSQL migration matrix passes. On 2026-07-10, disposable Docker PostgreSQL E2E upload plus `pg_dump`/`pg_restore` preserved all 48 public-table counts and all 18 DbUp journal entries; the recovery runbook is checked in. Production rollout evidence remains required before Phase 0 certification.

All remaining package definitions, branches, requirements, and prerequisites are
machine-readable in [`scripts/remediation/manifest.tsv`](../scripts/remediation/manifest.tsv).
