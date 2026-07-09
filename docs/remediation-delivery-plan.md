# Terraform Registry remediation delivery plan

Status: Proposed

Baseline: `develop` at `133d32b6fbc8e1819ec0b287ad5849379391898a`

Specification: [remediation-specification.md](remediation-specification.md)

## 1. Delivery model

Use one temporary phase branch, one leaf branch, and one worktree per work package:

```text
origin/develop
  └── remediation/phase/N-name
        ├── fix|security|perf|feat|ci|test/audit-pN-package-a
        ├── fix|security|perf|feat|ci|test/audit-pN-package-b
        └── test/audit-pN-phase-gate
```

Leaf PRs target the phase branch. After the leaf PRs and phase gate are green, one phase PR targets `develop`. Only the phase branch is merged into `develop`.

This model provides:

- parallel implementation and leaf-level review;
- one combined deployable branch for phase verification;
- no partially implemented invariant on `develop`;
- a single release unit per phase and, for application-only phases, a single revert-based rollback commit;
- an explicit point at which the next phase takes its baseline.

Keep the phase branch short-lived. If it cannot be reviewed and merged as one coherent deployable unit, split it into smaller phase branches rather than bypassing the gate.

## 2. Roles and operating rules

One coordinator/integrator owns the primary checkout, remote synchronization, phase branches, schema number reservations, merge order, and cleanup. Workers own only their assigned worktree and leaf branch. A separate final approver reviews phase PRs; the integrator cannot satisfy approval rules for a branch they last pushed.

With four concurrent implementation slots, use one coordinator/integrator and up to three implementation worktrees.

Rules:

1. Never share a worktree or branch between workers.
2. Never implement directly in the phase worktree; it is for integration, conflict review, and gates.
3. Never switch the primary checkout away from `develop` during active parallel work.
4. Workers do not prune/remove worktrees, delete branches, or fetch into another worker's checkout.
5. Do not cherry-pick between remediation branches. Merge a shared prerequisite first or use a recorded two-level stack.
6. Reserve a single application-schema lane. Phase 0 may use separate pre-reserved PostgreSQL and SQLite repair filenames in parallel, but one owner controls shared `DbUpMigrator` behavior. Later cross-backend schema packages are serialized.
7. Keep each leaf PR to one invariant and its tests. Do not mix broad cleanup or dependency churn into a correctness PR.
8. Every branch must remain buildable. Incomplete behavior is guarded by a default-off feature flag or stays inside an atomic phase branch.
9. `develop` remains the normal integration target and `main` remains release-only.
10. A green phase PR is not complete until the post-merge `develop` workflow is also green.

## 3. Bootstrap PRs

Merge these focused bootstrap PRs directly to `develop`:

| Merge order | Required before | Branch | Scope | Required outcome |
|---:|---|---|---|---|
| 1 | Phase 0 | `chore/audit-bootstrap-nuget-baseline` | `SUP-001`: remediate the current production High NuGet advisory through the owning package set. | The existing warnings-as-errors restore is green without suppressing the advisory. |
| 2 | Phase 0 | `docs/audit-remediation-program` | This specification and delivery plan. | Reviewers approve scope, requirement IDs, phase ownership, version-selection policy, and rollout policy before implementation begins. |
| 3 | Phase 0 | `ci/audit-bootstrap-phase-gates` | CI/security workflow support, phase-branch protection, merge queue, and pinned Terraform CLI test setup. | Leaf PRs targeting `remediation/phase/**` run required checks. This PR amends the merged specification with the exact pinned newest-stable and oldest-supported Terraform versions before a CLI gate is required. |
| 4 | Phase 1 | `ci/audit-bootstrap-cloud-test-environments` | Protected Azure and S3-compatible integration environments. | Trusted CI uses secretless OIDC to an isolated Azure account with blob data and user-delegation-key permissions, plus an isolated S3-compatible conditional-write environment. Fork PRs cannot access credentials; cleanup and spend limits are enforced. |

The four branches can be implemented in parallel, but merge in the listed order so CI is green before the gate workflow becomes required. Add the quoted base pattern `"remediation/phase/**"` to both `.github/workflows/ci.yaml` and `.github/workflows/security.yaml`. Add a push or manual combined-phase gate if required by branch protection.

Create or enable a branch ruleset for `remediation/phase/**` that requires PRs, the current required CI/security checks, at least one valid approval, resolved conversations, linear history, and no force pushes. Workflow filters alone do not protect a phase branch. Make npm/dependency/filesystem scan failures blocking where policy requires them; otherwise define an explicit, time-bounded exception process in the bootstrap PR. Do not include application fixes in the documentation or CI bootstrap PRs.

When adding `merge_group`, restrict registry login and image publication to `push`, or to `workflow_dispatch` when `inputs.push_image` is true. A speculative merge-queue build must never authenticate to GHCR, push an image, or receive release-only write permissions. Prefer separate scan and publication jobs so PR/merge-group jobs retain read-only permissions.

The frontend Moderate advisory may remain as a documented, time-bound exception because affected components are unused. The production High NuGet advisory must be resolved or explicitly isolated before using a warnings-as-errors audit as a required baseline.

## 4. Worktree mechanics

The repository already contains owned worktrees under `.worktrees/` and one stale/prunable external registration. Do not reuse, remove, or broadly prune them. Reserve `.worktrees/remediation/` for this program.

Coordinator preflight:

```bash
set -euo pipefail

ROOT=/home/rocky/terraform-registry
WORKTREES="$ROOT/.worktrees/remediation"
: "${EXPECTED_ORIGIN:?Set EXPECTED_ORIGIN to the canonical repository URL}"

test "$(git -C "$ROOT" rev-parse --show-toplevel)" = "$ROOT"
test "$(git -C "$ROOT" remote get-url origin)" = "$EXPECTED_ORIGIN"
test -z "$(git -C "$ROOT" status --porcelain)"
git -C "$ROOT" switch develop
test "$(git -C "$ROOT" branch --show-current)" = develop
git -C "$ROOT" fetch --prune origin
git -C "$ROOT" pull --ff-only origin develop
test -z "$(git -C "$ROOT" status --porcelain)"

git -C "$ROOT" worktree list --porcelain
git -C "$ROOT" worktree prune --dry-run
mkdir -p "$WORKTREES"
```

`worktree prune --dry-run` is diagnostic only. Do not run a real broad prune during this program.

Create and publish a phase branch:

```bash
PHASE_BRANCH=remediation/phase/0-migration-safety
PHASE_PATH="$WORKTREES/phase-0-migration-safety"

git -C "$ROOT" worktree add \
  -b "$PHASE_BRANCH" \
  "$PHASE_PATH" \
  origin/develop

git -C "$PHASE_PATH" push -u origin "$PHASE_BRANCH"
```

Create a leaf branch from the exact phase tip:

```bash
git -C "$PHASE_PATH" fetch origin
git -C "$PHASE_PATH" pull --ff-only origin "$PHASE_BRANCH"

LEAF_BRANCH=fix/audit-p0-postgres-migration
LEAF_PATH="$WORKTREES/p0-postgres-migration"

git -C "$ROOT" worktree add \
  -b "$LEAF_BRANCH" \
  "$LEAF_PATH" \
  "origin/$PHASE_BRANCH"

git -C "$LEAF_PATH" status --short --branch
git -C "$LEAF_PATH" rev-parse HEAD
```

After the first real implementation commit, push and open the leaf PR against the phase branch. Do not create empty commits just to open a PR:

```bash
git -C "$LEAF_PATH" push -u origin "$LEAF_BRANCH"

gh pr create \
  --draft \
  --base "$PHASE_BRANCH" \
  --head "$LEAF_BRANCH"
```

Before every dependent wave, serialized package, phase-gate branch, and final phase review, the coordinator asserts that the phase worktree is clean and fast-forwards it from the remote phase branch. New leaves always start from `origin/$PHASE_BRANCH`, never a possibly stale local ref.

Use unique Docker Compose project names, image tags, ports, temporary directories, and database names based on the work-package slug so concurrent tests cannot collide.

## 5. Dependency graph

```text
Phase 0 migration/startup safety
  ├── identity schema changes
  ├── durable job/outbox schema
  └── all later schema work

archive format contract
  └── download/backend conformance
        └── Azure delegation SAS

archive resource limits
  ├── VCS ingress hardening
  └── publication commit contract
        └── durable extraction enqueue/workers
              ├── Local atomic publication
              ├── Azure atomic publication
              └── S3 atomic/replacement consistency

zero-role/least-privilege policy
  └── admission/disable/revocation
        └── namespace maintainer authorization

mirror config and runtime limits
  └── single-flight/heartbeats
        └── lazy provider fetch
              ├── signature verification
              └── operator control plane

repository/query changes
  └── broad cancellation propagation

publication/storage consistency
  └── asynchronous reconciliation

Phase 3 leased-work primitives
  └── Phase 5 transactional outbox and webhook/audit delivery
```

## 6. Phases and work packages

### Phase 0 — Migration safety

Integration branch: `remediation/phase/0-migration-safety`

Reserve distinct PostgreSQL and SQLite repair filenames first. Then merge the shared startup/migration guard package:

| Package | Leaf branch | Requirements | Exit evidence |
|---|---|---|---|
| `P0-GUARD` | `fix/audit-p0-startup-migration-guard` | `DB-003`–`DB-005`, `STO-005` | Migration/schema readiness completes before storage resolution; current reconciliation moves from constructors to a minimal post-migration hosted one-shot adapter; common journal-state detection/fixtures exist; unsafe/unknown state fails readiness. This branch owns shared `DbUpMigrator` and startup code. |

After `P0-GUARD`, run the backend repairs in parallel using their separately reserved filename lanes:

| Package | Leaf branch | Requirements | Exit evidence |
|---|---|---|---|
| `P0-PG` | `fix/audit-p0-postgres-migration` | `DB-001` | Populated pre-010 and journaled-state PostgreSQL upgrades preserve all recoverable fields/relationships and fail closed on unknown state. |
| `P0-SQLITE` | `fix/audit-p0-sqlite-migration` | `DB-002` | Populated upgrade preserves all children, passes `foreign_key_check`, is atomic, and reruns safely. |

Phase gate package/branch: `P0-MATRIX` — `test/audit-p0-migration-matrix`

The gate branch owns `DB-006` and the combined acceptance evidence.

Gate:

- fresh and populated SQLite/PostgreSQL chains;
- already-journaled and interrupted-state fixtures;
- field, row-count, and relationship assertions;
- second-run/idempotency behavior;
- verified backup restoration and operator recovery runbook;
- explicit report of data that cannot be reconstructed if 010 already deleted it.

No later schema package may merge before this phase is on `develop`.

### Phase 1 — Release interoperability and immediate authorization

Integration branch: `remediation/phase/1-release-interoperability`

First parallel wave:

| Package | Leaf branch | Requirements | Notes |
|---|---|---|---|
| `P1-PROTOCOL` | `fix/audit-p1-module-protocol-status` | `MOD-004` | Exact/latest HTTP behavior only. |
| `P1-ARCHIVE` | `fix/audit-p1-archive-format-contract` | `MOD-001`, `MOD-002` | Byte-based format descriptor, compatibility classification, and any required schema expansion. |
| `P1-AUTH` | `security/audit-p1-zero-role-least-privilege` | `IAM-001`, `IAM-011` | Remove request-time role resurrection; zero roles stays zero; automatically assigned role becomes read-only. No disabled-user schema is introduced here. |
| `P1-SEMVER` | `fix/audit-p1-semver-total-order` | `MOD-006` | Arbitrary-length numeric ordering and property tests. |
| `P1-DOCKER` | `fix/audit-p1-container-storage-permissions` | `DEP-002` | Writable default and mounted module/provider storage as the declared non-root user. |

Second wave:

| Package | Leaf branch | Dependency | Requirements |
|---|---|---|---|
| `P1-DOWNLOAD` | `fix/audit-p1-download-conformance` | `P1-PROTOCOL`, `P1-ARCHIVE` | `MOD-003`, `MOD-007`, `MOD-008`; Local/Azure/S3 download conformance and real Terraform module smoke. |
| `P1-FRONTEND` | `fix/audit-p1-frontend-image-provenance` | `P1-DOCKER` because both edit `Dockerfile` | `DEP-003` |
| `P1-KEY-PERM` | `security/audit-p1-api-key-permission` | none | `IAM-005` |
| `P1-PROXY` | `security/audit-p1-trusted-proxy-cookies` | none | `IAM-009` |

Final stacked package:

| Package | Leaf branch | Dependency | Requirements |
|---|---|---|---|
| `P1-AZURE-SAS` | `fix/audit-p1-azure-delegation-sas` | `P1-DOWNLOAD` | `DEP-001`: user-delegation SAS, fail closed, shared-key compatibility. |

Phase gate branch: `test/audit-p1-terraform-deployment-gate`

Gate:

- real `terraform init` for ZIP and tar.gz via Local, Azure, and S3-compatible storage;
- exact/latest protocol conformance independent of User-Agent;
- shared-key and managed-identity Azure download paths;
- non-root image module/provider write and download;
- unique frontend marker from the phase commit present in the image;
- removed-role requests remain permissionless through cookie, JWT, and API key, and the automatic built-in role cannot mutate registry content;
- oversized SemVer list/latest requests never throw.

### Phase 2 — Bounded ingestion and identity policy

Integration branch: `remediation/phase/2-bounded-ingestion-identity`

Foundation package:

| Package | Leaf branch | Requirements |
|---|---|---|
| `P2-RATE` | `security/audit-p2-endpoint-rate-limit-foundation` | `ING-004`; named limiter infrastructure, common configuration/metrics, partition contracts, and overload responses. |

After `P2-RATE`, implementation can proceed in parallel, but schema-bearing branches merge in the schema order below:

| Package | Leaf branch | Requirements |
|---|---|---|
| `P2-ARCHIVE` | `security/audit-p2-archive-validation-limits` | `MOD-005`, `ING-001` |
| `P2-ADMISSION` | `security/audit-p2-user-admission-disable` | `IAM-002`, `IAM-012` |
| `P2-APIKEY` | `security/audit-p2-api-key-digest-rate-limit` | `IAM-006`, `IAM-007` |
| `P2-PROVIDER` | `security/audit-p2-provider-upload-streaming` | `PERF-002` |
| `P2-DOWNLOAD-TOKENS` | `security/audit-p2-stateless-download-tokens` | `IAM-008` |
| `P2-AUTH-CODES` | `feat/audit-p2-durable-authorization-codes` | `IAM-013` |

Second wave:

| Package | Leaf branch | Dependency | Requirements |
|---|---|---|---|
| `P2-VCS` | `security/audit-p2-vcs-webhook-ingress` | `P2-ARCHIVE` | `VCS-001`, `VCS-002` |
| `P2-ACL` | `feat/audit-p2-namespace-maintainer-acl` | `P2-ADMISSION`, schema lane | `IAM-003`, `IAM-004` |
| `P2-RATE-ATTACH` | `security/audit-p2-endpoint-rate-limit-attachments` | `P2-ARCHIVE`, `P2-APIKEY`, `P2-PROVIDER`, `P2-VCS` | `ING-006`; endpoint-specific selectors/attachments only |

Schema merge order at the baseline is:

```text
P1-ARCHIVE (only if format persistence is required)
  -> P2-ADMISSION
    -> P2-APIKEY
      -> P2-AUTH-CODES
        -> P2-ACL
          -> P3 durable extraction schema
            -> P5 outbox schema
```

Each schema leaf rebases onto the merged predecessor. New exact migration filenames are reserved only when that leaf begins; implementation work may be parallel, but schema PR finalization and merge are serialized.

Phase gate branch: `test/audit-p2-security-abuse-gate`

Gate:

- corrupt, truncated, bomb, expanded-size, entry-count, entry-size, ratio, traversal, and temporary-disk cases;
- oversized/chunked provider and webhook bodies;
- request cancellation during ingest;
- offboarding, admission, tenant/domain policy, disabled users, and cross-namespace mutation;
- legacy/new API-key verification, bounded parallel validation, immediate revoke, and enforced key permissions;
- token tampering, expiration, replay, replica, and restart behavior.
- named module/provider upload, webhook, and API-key policies saturate at configured limits and return `429` with metrics.

### Phase 2B — Mirror containment

Integration branch: `remediation/phase/2b-mirror-containment`

Until this phase is merged, release guidance must keep mirroring disabled in production. The mirror implementation lane is serialized because the packages share `ModuleMirrorService`, `ProviderMirrorService`, options, and tests:

| Order | Package | Leaf branch | Requirements |
|---:|---|---|---|
| 1 | `P2B-MIR-CONFIG` | `feat/audit-p2b-mirror-runtime-limits` | `MIR-001`–`MIR-004` and the mirror endpoint portion of `ING-006` |
| 2 | `P2B-MIR-LEASE` | `fix/audit-p2b-mirror-singleflight-heartbeats` | `MIR-005`, `MIR-006` |
| 3 | `P2B-MIR-LAZY` | `perf/audit-p2b-provider-lazy-platforms` | `MIR-007` |
| 4 | `P2B-MIR-TRUST` | `security/audit-p2b-provider-signatures` | `MIR-008` |

An independent externally reachable scale fix runs in parallel:

| Package | Leaf branch | Requirements |
|---|---|---|
| `P2B-LIST` | `perf/audit-p2b-module-list-sql` | `PERF-001` |

Phase gate branch: `test/audit-p2b-mirror-containment-gate`

Gate:

- 100 concurrent same-key misses issue one upstream fetch and return no transient `404`;
- global/per-coordinate limits, timeout, negative TTL, cache accounting, and eviction are observed under load;
- lease heartbeat, expiry, cancellation, and lost-owner cases;
- provider metadata does not prefetch all packages; shared metadata is fetched once;
- trusted/tampered/unknown signature cases;
- mirror-triggering endpoint rate/concurrency saturation returns `429` and emits the expected metrics;
- SQL pagination with at least 100,000 versions records plan, round trips, rows transferred, latency, and allocation evidence.

### Phase 3 — Transactional publication and durable extraction

Integration branch: `remediation/phase/3-transactional-publication`

Foundation package:

| Package | Leaf branch | Requirements |
|---|---|---|
| `P3-CONTRACT` | `refactor/audit-p3-publication-commit-contract` | Enabling contract only: unique attempts, catalog/artifact compare-and-swap, rollback ownership, and atomic creation of durable extraction work. Completion of `STO-*` remains assigned to backend packages. |

After `P3-CONTRACT`, land the durable job implementation and enqueue transaction:

| Package | Leaf branch | Requirements |
|---|---|---|
| `P3-JOBS` | `feat/audit-p3-durable-extraction-jobs` | `ING-002`, `ING-003`, `ING-005`, schema lane |

After `P3-JOBS`, implement all backend mutation contracts in parallel:

| Package | Leaf branch | Requirements |
|---|---|---|
| `P3-LOCAL` | `fix/audit-p3-local-atomic-publication` | `STO-001`–`STO-004` Local implementation |
| `P3-AZURE` | `fix/audit-p3-azure-atomic-publication` | `STO-001`–`STO-004` Azure implementation |
| `P3-S3` | `fix/audit-p3-s3-replacement-consistency` | `STO-001`–`STO-004` S3 conformance, including replacement metadata |

Phase gate branch: `test/audit-p3-publication-fault-gate`

Gate:

- barrier-controlled same-coordinate create/replace/purge races;
- injected failure at staging, DB commit, promotion, superseded-delete, and cleanup;
- winner and previous artifact readability assertions;
- process termination, stale lease, multiple batches, retry, and dead-letter behavior;
- artifact checksum/format/provenance/extraction metadata agreement;
- no orphan or winner deletion by losing attempts.

### Phase 4 — Operator control and storage operations

Integration branch: `remediation/phase/4-operator-storage`

Independent parallel lanes:

| Package | Leaf branch | Requirements |
|---|---|---|
| `P4-MIR-ADMIN` | `feat/audit-p4-mirror-control-plane` | `MIR-009`; depends on Phase 2B semantics |
| `P4-PROVIDER-QUERY` | `perf/audit-p4-provider-query-path` | `PERF-003` |
| `P4-RECONCILE` | `perf/audit-p4-async-storage-reconciliation` | `STO-006`; depends on Phase 3 |

Phase gate branch: `test/audit-p4-operator-storage-gate`

Gate:

- authorized/unauthorized mirror configuration, cache inspection, lease/job visibility, purge, and audit behavior;
- provider package path uses the asserted database/storage round-trip budget and preserves errors/cancellation;
- startup remains responsive while a large artifact store reconciles asynchronously.

### Phase 5 — Durable side effects and cross-cutting performance

Integration branch: `remediation/phase/5-operability`

Parallel wave:

| Package | Leaf branch | Requirements |
|---|---|---|
| `P5-SIDE-EFFECTS` | `feat/audit-p5-durable-side-effects` | `REL-001`, `REL-002`; reuse the Phase 3 lease/worker primitives but own a distinct transactional outbox schema and delivery policy |
| `P5-HTTP` | `perf/audit-p5-http-delivery` | `REL-003` |
| `P5-HEADERS` | `security/audit-p5-browser-security-headers` | Preventive hardening `REL-004` |
| `P5-REDIRECT` | `security/audit-p5-local-return-paths` | Preventive hardening `IAM-010` |
| `P5-SUPPLY` | `chore/audit-p5-supply-chain-pinning` | `SUP-002`, `SUP-003` |

Final cross-cutting package:

| Package | Leaf branch | Dependency | Requirements |
|---|---|---|---|
| `P5-CANCEL` | `refactor/audit-p5-request-cancellation` | All repository/storage signature changes merged | `PERF-004` |

Phase gate branch: `test/audit-p5-operability-gate`

Gate:

- no detached loss-intolerant tasks;
- graceful drain, crash/restart, retry, idempotency, and queue saturation;
- request abort reaches DB, HTTP, storage, and parsing without partial commits;
- compression/cache policy tests;
- dependency/secret/container scans and reproducible tool versions;
- required operational metrics and redaction tests.

### Phase 6 — Release certification

Integration branch: `remediation/phase/6-release-certification`

Parallel certification branches:

| Package | Leaf branch | Scope |
|---|---|---|
| `P6-E2E` | `test/audit-p6-terraform-backend-matrix` | Pinned supported Terraform versions; module/provider Local, Azure, S3-compatible, and non-root image matrix. |
| `P6-FAULT-LOAD` | `test/audit-p6-fault-load-certification` | Migration, storage, extraction, mirror, authorization, cancellation, and load scenarios from the specification. |
| `P6-RUNBOOKS` | `docs/audit-p6-upgrade-rollback-runbooks` | Backup/restore, migration state, image digest rollout/rollback, key rotation, cache/job operations, alerts, and known compatibility windows. |

There should be no new product feature in Phase 6. Any failing certification test reopens the owning phase rather than accepting a waiver in the certification branch.

The final phase PR must record the release-candidate image digest and complete verification evidence.

## 7. Parallelism and collision lanes

| Collision domain | Required order |
|---|---|
| Migration scripts/fixtures | Phase 0 uses separately reserved PostgreSQL/SQLite repair lanes with one shared guard owner; all later application schema changes use the serialized order in Phase 2. |
| `ModuleHandlers` and module protocol tests | Protocol status → archive format → download conformance → archive limits/later API changes. |
| `LocalModuleService` | Archive/download contract → stateless download tokens → atomic publication → reconciliation. |
| `AzureBlobModuleService` | Archive/download contract → delegation SAS → atomic publication → reconciliation. |
| Publish/extraction pipeline | Archive validation enables VCS ingress and publication foundation independently; publication contract → durable enqueue/workers → backend commits. |
| `ProviderMirrorService`, `ModuleMirrorService`, mirror options | Serialize Phase 2B mirror packages in listed order; management follows in Phase 4. |
| Authentication middleware/models | Zero-role/least privilege → admission/disable/revocation → namespace ACL. |
| Dockerfile and CI frontend flow | Container storage permissions → frontend image provenance. |
| Repository interfaces/implementations | SQL query changes and durable jobs before broad cancellation propagation. |
| `Program.cs`/DI | Resolve conflicts in the owning leaf. If ownership spans merged packages, create a dedicated conflict-resolution leaf PR; never patch the protected phase branch directly. |

Workers may begin an independent later package while an earlier phase is under review, but it may not merge and must rebase onto the final prerequisite phase. Limit stacked PRs to two levels.

For a stacked branch, record the parent tip:

```bash
PARENT_BRANCH=fix/audit-p1-download-conformance
PARENT_TIP="$(git -C "$ROOT" rev-parse "$PARENT_BRANCH")"

git -C "$ROOT" worktree add \
  -b fix/audit-p1-azure-delegation-sas \
  "$WORKTREES/p1-azure-delegation-sas" \
  "$PARENT_TIP"
```

Initially target the parent PR. After the parent merges into the phase branch, synchronize the phase worktree and rebase the child with the recorded parent tip:

```bash
git -C "$PHASE_PATH" fetch origin
git -C "$PHASE_PATH" pull --ff-only origin "$PHASE_BRANCH"

CHILD_BRANCH=fix/audit-p1-azure-delegation-sas
CHILD_PATH="$WORKTREES/p1-azure-delegation-sas"

git -C "$CHILD_PATH" fetch origin
git -C "$CHILD_PATH" rebase --onto \
  "origin/$PHASE_BRANCH" \
  "$PARENT_TIP" \
  "$CHILD_BRANCH"
git -C "$CHILD_PATH" push --force-with-lease origin "$CHILD_BRANCH"
```

Force updates are forbidden on phase branches. `--force-with-lease` is permitted only on an unmerged leaf after a documented rebase; retarget it to the phase branch and rerun all checks.

## 8. PR contract

Every leaf PR description includes:

- work-package and specification requirement IDs;
- base commit and target phase branch;
- prerequisite PRs and exact parent tip if stacked;
- explicit scope and non-goals;
- data, wire, configuration, and deployment compatibility impact;
- failure, retry, cancellation, and rollback behavior;
- migration filenames and compatibility window, if applicable;
- tests added and commands/results;
- measured evidence for performance claims;
- configuration defaults and operator documentation;
- observability and secret-redaction impact.

Review requirements:

1. Regression test demonstrates the baseline failure or invariant violation.
2. Targeted suite is green in the leaf worktree.
3. Full required CI is green against the latest phase tip.
4. `git diff --check` is clean and the worktree has no unintended files.
5. No unresolved TODO, disabled test, warning suppression, broad exception swallow, or security-check bypass is introduced.
6. Any rebase after approval invalidates prior CI/approval and requires rerun.

Squash leaf PRs into the protected phase branch. Squash the final phase PR into `develop`, which currently requires linear history; the phase PR and linked leaf PRs retain provenance while the phase becomes one release commit. Application-only phase commits can be reverted; schema phases remain roll-forward-only.

The bootstrap enables a merge queue for `develop` and ensures required workflows handle the `merge_group` event. The queue must test the phase against the current `develop` tip before squash merge. If a merge queue cannot be enabled, pause other `develop` merges while the final phase candidate is updated and reverified; do not bypass strict up-to-date protection. The coordinator/integrator performs updates, and a different person supplies the final valid approval.

## 9. CI and verification gates

Every leaf and phase PR runs the existing Release build/test, frontend build, Docker build/Trivy scan, NuGet/npm audit, dependency review, CodeQL, and secret/filesystem scan. Required checks are blocking. A currently reporting-only scan becomes blocking in bootstrap or uses the documented owner/expiry/compensating-control exception process.

Baseline local commands:

```bash
dotnet restore \
  -p:NuGetAudit=true \
  -p:NuGetAuditMode=all \
  -p:NuGetAuditLevel=high \
  "-warnaserror:NU1903;NU1904"

dotnet build terraform-registry.sln \
  --no-restore \
  --configuration Release

dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj \
  --no-build \
  --configuration Release

npm --prefix TerraformRegistry/web-src ci
npm --prefix TerraformRegistry/web-src audit --audit-level=high
npm --prefix TerraformRegistry/web-src run generate

docker build --tag "terraform-registry:${USER}-audit" .
```

Additional required gates by change type:

| Change | Required verification |
|---|---|
| Migration | Fresh/populated/journaled/interrupted/second-run matrix, backup restore, SQLite FK check, PostgreSQL Testcontainer, lock/duration observation. |
| Archive/protocol | ZIP/tar.gz, API/VCS, Local/Azure/S3, relative/signed URLs, actual Terraform CLI. |
| Storage | Concurrent same-coordinate create/replace/purge and injected DB/storage/promotion/cleanup failure. |
| Authentication | Anonymous, cookie/JWT, OIDC, API key, disabled/removed-role, tenant policy, owner/non-owner/admin, expired/revoked credential. |
| Extraction/webhook | All archive budgets, body limits, cancellation, job crash/restart/stale lease, HTTP retry semantics. |
| Mirror | Contention, limits, cache/negative cache, leases, lazy fetch, signatures, auth and slow upstream. |
| Frontend/container | Generated marker, non-root default/mounted stores, readiness, image/security scan. |
| Performance | Reproducible dataset, before/after plan, rows, latency percentiles, allocations, memory, and a recorded non-regression threshold. |

Mock-only verification is not sufficient for Azure delegation SAS, S3 conditional writes/races, Terraform/go-getter behavior, or the final container.

Untrusted fork PRs run secretless unit/emulator/contract checks only. Real Azure/S3 checks run as protected required checks on the trusted phase branch and its `merge_group`, after maintainer-reviewed code is present there; no cloud credential or federated subject is available to a fork workflow.

## 10. Migration emergency path

Before Phase 0 implementation, identify:

- exact deployed application SHA(s);
- whether either 010 script is present in each deployed binary;
- DbUp journal rows on every environment;
- whether pre-upgrade backups exist;
- whether destructive effects have already occurred.

Phase 0 must provide three paths:

1. safe behavior for a database that has not journaled the defective script;
2. forward validation/repair for a database that has journaled it;
3. backup restoration instructions for irrecoverable deleted data.

If a released `main` build contains the defective migration, cut the production repair from that exact release line:

```text
hotfix/migration-safety-<calver> -> main
chore/forward-merge-migration-hotfix -> develop
```

Forward-merge the identical hotfix. Do not recreate it with differently numbered scripts or divergent SQL.

Never recover by deleting journal rows, renumbering/renaming journaled scripts, manually editing production schema without a reviewed runbook, or resetting Git history.

## 11. Rollout and rollback

- Record old and new immutable image digests for every phase; do not rely on the mutable `develop` tag.
- Application-only phases roll back by reverting the phase squash commit and deploying the previous digest.
- Database phases roll forward. Contract cleanup occurs only after the compatibility window and backup evidence.
- Feature flags remain default-off until their complete dependent stack passes its phase gate.
- Stop the merge queue immediately if the phase or post-merge `develop` workflow fails.
- Readiness must fail during unsafe schema state, unrecoverable worker state, or required storage initialization failure.
- Record production rollout evidence before deleting the phase branch.

## 12. Safe cleanup

Remove only worktrees owned by this program and only after confirming they are clean and merged or intentionally abandoned:

```bash
set -euo pipefail

LEAF_PATH="$WORKTREES/p1-download-conformance"
LEAF_BRANCH=fix/audit-p1-download-conformance

test -z "$(git -C "$LEAF_PATH" status --porcelain)"
test "$(gh pr view "$LEAF_BRANCH" --json mergedAt --jq '.mergedAt != null')" = true
git -C "$ROOT" worktree remove "$LEAF_PATH"
git -C "$ROOT" push origin --delete "$LEAF_BRANCH"
```

Because squash-merged leaf tips are not ancestors of the phase branch, retain their local refs during normal cleanup. Local ref deletion is a separate, explicitly reviewed maintenance action after merged-PR and expected-tip verification. Do not use forced worktree removal, `git branch -D`, destructive reset/checkout, or a broad worktree prune during routine cleanup.

## 13. Audit traceability

| Confirmed finding | Specification IDs | Owning package/phase |
|---|---|---|
| PostgreSQL/SQLite migration data loss and startup failure | `DB-001`–`DB-006`, `STO-005` | `P0-GUARD`, `P0-PG`, `P0-SQLITE`, `P0-MATRIX` |
| Archive bytes/names/URLs disagree; Terraform install failure | `MOD-001`–`MOD-003`, `MOD-007` | `P1-ARCHIVE`, `P1-DOWNLOAD`, `P1-AZURE-SAS` |
| Download metadata can advertise a missing local artifact | `MOD-008` | `P1-DOWNLOAD` |
| Exact/latest HTTP contract mismatch | `MOD-004` | `P1-PROTOCOL` |
| SemVer overflow and incorrect numeric precedence | `MOD-006` | `P1-SEMVER` |
| Docker provider directory and stale frontend | `DEP-002`, `DEP-003` | `P1-DOCKER`, `P1-FRONTEND` |
| Role resurrection and destructive automatic role | `IAM-001`, `IAM-011` | `P1-AUTH` |
| Azure managed-identity SAS failure | `DEP-001` | `P1-AZURE-SAS` |
| Corrupt publication and archive memory/disk exhaustion | `MOD-005`, `ING-001` | `P2-ARCHIVE` |
| OIDC admission, disabled-user lifecycle, and namespace-wide mutation | `IAM-002`–`IAM-004`, `IAM-012` | `P2-ADMISSION`, `P2-ACL` |
| API-key permission bypass and Argon2 amplification | `IAM-005`–`IAM-007` | `P1-KEY-PERM`, `P2-APIKEY` |
| Provider upload buffering | `PERF-002` | `P2-PROVIDER` |
| No endpoint rate/concurrency limiter | `ING-004`, `ING-006` | `P2-RATE`, `P2-RATE-ATTACH`, `P2B-MIR-CONFIG` |
| VCS webhook body/archive/cancellation/status gaps | `VCS-001`, `VCS-002` | `P2-VCS` |
| Process-local leaking download tokens/auth codes | `IAM-008`, `IAM-013` | `P2-DOWNLOAD-TOKENS`, `P2-AUTH-CODES` |
| Insecure proxy cookie/scheme | `IAM-009` | `P1-PROXY` |
| Local return-path validation (preventive addition) | `IAM-010` | `P5-REDIRECT` |
| Local/Azure concurrent publication and non-atomic replace/purge | `STO-001`–`STO-004` | `P3-LOCAL`, `P3-AZURE`, phase gate |
| S3 stale replacement metadata | `STO-003`, `STO-004` | `P3-S3` |
| Stranded/unbounded in-memory extraction work | `ING-002`, `ING-003`, `ING-005` | `P3-JOBS` |
| Inert mirror limits/cache/timeouts/auth and eager platform fetch | `MIR-001`–`MIR-004`, `MIR-007` | `P2B-MIR-CONFIG`, `P2B-MIR-LAZY` |
| Mirror false 404 and missing lease heartbeat | `MIR-005`, `MIR-006` | `P2B-MIR-LEASE` |
| Unverified provider signatures | `MIR-008` | `P2B-MIR-TRUST` |
| Missing mirror operator API/UI | `MIR-009` | `P4-MIR-ADMIN` |
| Full-table in-memory module listing | `PERF-001` | `P2B-LIST` |
| Sequential provider package lookup | `PERF-003` | `P4-PROVIDER-QUERY` |
| Constructor-time reconciliation before migrations and blocking scans | `STO-005`, `STO-006` | `P0-GUARD`, `P4-RECONCILE` |
| Detached audit/webhook/analytics work | `REL-001`, `REL-002` | `P5-SIDE-EFFECTS` |
| Missing request cancellation | `PERF-004` | `P5-CANCEL` |
| Missing response compression and asset policy | `REL-003` | `P5-HTTP` |
| Browser security headers (preventive addition) | `REL-004` | `P5-HEADERS` |
| Dependency advisories and mutable build inputs | `SUP-001`–`SUP-003` | Bootstrap, `P5-SUPPLY` |
| Missing real backend/Terraform/fault/load coverage | Verification matrix and definition of done | Phase gates, `P6-E2E`, `P6-FAULT-LOAD` |

Anything discovered during implementation that is not represented by a requirement ID is either added through a reviewed specification amendment or filed as follow-up work. It must not be silently folded into an unrelated leaf PR.
