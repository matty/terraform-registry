# Terraform Registry hardening specification

Status: Proposed

Baseline: `develop` at `133d32b6fbc8e1819ec0b287ad5849379391898a`

Companion plan: [remediation-delivery-plan.md](remediation-delivery-plan.md)

## 1. Purpose

This specification turns the July 2026 correctness, security, protocol, deployment, and performance audit into testable system requirements. The target outcome is a registry that can be upgraded without data loss, serves artifacts accepted by Terraform, preserves artifact/catalog consistency under failure and concurrency, enforces explicit authorization, and bounds all attacker-controlled work.

The requirements are intentionally independent of individual implementation classes. Branch and worktree allocation is defined in the companion delivery plan.

`IAM-010`, `REL-004`, and the reproducibility expansion in `SUP-002` are preventive hardening added around the confirmed findings. They are deliberately isolated from the corrective PRs so they cannot delay or obscure release-blocking fixes.

## 2. Architecture direction

Keep the current single-deployable modular monolith. Do not introduce microservices, event sourcing, a general-purpose command bus, or a repository per table as part of this remediation.

Add only focused internal contracts where an invariant spans implementations:

- an archive descriptor and validator shared by API, VCS, Local, Azure, and S3 paths;
- a publication attempt/commit contract shared by storage backends;
- a durable leased-work contract for extraction and loss-intolerant side effects;
- a mirror resource-governance contract for admission, cache accounting, and leases;
- an explicit identity lifecycle and namespace authorization policy.

Read-heavy paths may use dedicated query methods. This does not require application-wide CQRS.

## 3. System invariants

Every implementation and test must preserve these invariants:

1. **Upgrade safety:** a supported database upgrade never silently deletes or disconnects user data. Unknown or partially migrated states fail closed before the application serves traffic.
2. **Artifact truth:** stored bytes, recorded format, checksum, object name, and advertised URL describe the same artifact.
3. **Installability:** every successfully published module can be installed by a supported Terraform CLI, or publication fails without a catalog row or final artifact.
4. **Atomic replacement:** a failed or losing publication attempt cannot delete or corrupt the previous artifact or another attempt's winner.
5. **Explicit authorization:** zero roles means zero permissions. Authentication never grants a role as a side effect of an ordinary request.
6. **Bounded work:** request bodies, archive expansion, queues, concurrent downloads, caches, retries, and temporary storage have enforced limits.
7. **Durable state transitions:** accepted loss-intolerant work survives process restart and is retryable and idempotent.
8. **Cancellation:** abandoned requests stop cancellable database, network, storage, and parsing work without leaving partial commits.
9. **Release fidelity:** the application, generated frontend, migrations, and container image all come from the same commit.
10. **Observable failure:** rejected work, stale leases, retries, cache pressure, reconciliation failures, and migration state are visible through logs, metrics, and health/readiness status.

## 4. Scope and requirements

### 4.1 Database migration safety

| ID | Requirement | Acceptance criteria |
|---|---|---|
| `DB-001` | Preserve PostgreSQL VCS data across migration 010. | A populated pre-010 fixture containing users, VCS sources, encrypted PATs, webhook secrets, active state, and timestamps upgrades with field-for-field and relationship preservation. |
| `DB-002` | Make SQLite table rebuilds foreign-key safe. | Populated modules, downloads, users, API keys, roles, webhooks, and VCS rows survive upgrade; `PRAGMA foreign_key_check` is empty. |
| `DB-003` | Support both unjournaled and already-journaled migration 010 states. | Tests cover fresh schema, populated pre-010 schema, already-journaled 010 schema, second execution, and an interrupted/unknown state. |
| `DB-004` | Fail closed on unsafe migration state. | Startup reports an actionable readiness failure before serving traffic; it does not attempt a destructive best-effort repair. |
| `DB-005` | Use expand/dual-read/backfill/contract for new schema changes. | The prior application version remains compatible during the declared rolling-upgrade window; destructive contract cleanup occurs in a later release. |
| `DB-006` | Require backup and restore evidence for schema phases. | The phase PR records a successful restore into a disposable environment, row-count comparisons, lock/duration observations, and rollback limitations. |

The deployed SHA and DbUp journal state must be established before implementation. `origin/main` does not contain migration 010 at this baseline, but local branches and `develop` do, and `develop` may have been deployed. Therefore migration 010 must be treated as potentially applied.

Changing an old embedded script protects only databases that have not journaled that script. A new forward migration must validate or repair already-journaled schemas. Data already erased by a destructive migration cannot be reconstructed without an external backup.

At this baseline, the next unused conventional prefixes are PostgreSQL `018` and SQLite `017`. The coordinator must re-check and reserve exact filenames immediately before creating a schema branch. Existing duplicate numeric prefixes must not be renamed because DbUp journals the full resource name.

### 4.2 Module protocol, archives, and publication

| ID | Requirement | Acceptance criteria |
|---|---|---|
| `MOD-001` | Detect and validate archive format at ingress. | ZIP and gzip/tar are identified from bytes, not the supplied filename. Unknown, truncated, mismatched, or corrupt input is rejected before publication. |
| `MOD-002` | Preserve or canonically normalize archive format. | All backends either store a canonical ZIP or persist the real format. Existing artifacts have an explicit compatibility/migration path. |
| `MOD-003` | Return go-getter-compatible download sources. | The URL path suffix or `archive=` hint matches the bytes and is included before cloud signing. Local, Azure, and S3 pass real `terraform init` tests. |
| `MOD-004` | Conform to module registry HTTP semantics. | Exact-version download returns `204` plus `X-Terraform-Get` independent of User-Agent; latest download redirects to the canonical versioned endpoint. Relative URLs are covered. |
| `MOD-005` | Commit only installable artifacts. | Invalid content leaves no active catalog row, final object, extraction job, audit success event, or publish webhook. |
| `MOD-006` | Implement a total SemVer order. | Arbitrary-length numeric core and prerelease identifiers never throw and compare according to SemVer precedence; list/latest endpoints remain available. |
| `MOD-007` | Apply one backend download conformance contract. | The same download, format, existence, URL, and cancellation tests run against Local, Azure, and S3 implementations. Mutation/fault conformance is owned by `STO-001`–`STO-004`. |
| `MOD-008` | Advertise only readable artifacts. | Download metadata/token creation checks owned storage existence and readability. Missing artifacts return a consistent unavailable/not-found result and emit reconciliation drift. |

The initial archive safety defaults are:

| Limit | Default |
|---|---:|
| Compressed module archive | 100 MiB |
| Expanded archive total | 1 GiB |
| Archive entries | 10,000 |
| Single expanded entry | 256 MiB |
| Maximum compression ratio | 100:1 |

All values must be configurable, validated at startup, and enforced while streaming. An implementation may choose stricter defaults after measuring representative modules, but it may not ship without an expanded-size and entry-count limit.

### 4.3 Extraction, storage, and VCS ingestion

| ID | Requirement | Acceptance criteria |
|---|---|---|
| `ING-001` | Spool and extract archives with bounded resources. | Compressed input is streamed to bounded temporary storage without a full `MemoryStream`/`ToArray` duplicate. Bomb, entry-count, per-entry, ratio, path traversal, cancellation, and temporary-disk tests fail safely and clean temporary files. Per-active-ingest managed buffering is at most 4 MiB excluding framework/SDK overhead. |
| `ING-002` | Persist extraction work durably. | Accepted jobs survive restart; pending and stale-processing jobs are reclaimed; more than one batch drains; retries are idempotent. |
| `ING-003` | Define job leases and terminal states. | Claims are owner-checked, heartbeated, expire predictably, and support retry/dead-letter semantics with attempt/error metadata. |
| `STO-001` | Use a unique staging identity for every publication attempt. | Concurrent same-coordinate uploads never share a temporary file or object. |
| `STO-002` | Promote and replace atomically. | Barrier-controlled races leave exactly one readable winner; injected DB/storage failure preserves the prior version; loser cleanup cannot delete the winner. |
| `STO-003` | Keep catalog and artifact metadata aligned. | Replacement updates checksum, source/provenance, commit, format, size, extraction state, and documentation invalidation as one logical commit. |
| `STO-004` | Make purge outcomes truthful. | A partial artifact deletion is reported and retried; a successful response means catalog and owned artifacts reached the defined terminal state. |
| `STO-005` | Establish startup ordering without losing reconciliation. | Database migration and schema readiness finish before storage-backed services are resolved. Constructors perform no reconciliation, storage scan, network I/O, or sync-over-async work. A minimal hosted one-shot adapter preserves existing artifact discovery/drift behavior after migration and gates readiness until its initial pass completes. |
| `STO-006` | Run reconciliation asynchronously after readiness. | A hosted reconciler pages work, retries, cancels, and exposes readiness/metrics. It uses recorded metadata rather than ambiguous filename parsing. |
| `VCS-001` | Bound and authenticate webhook ingress before expensive work. | Request body is limited to 1 MiB by default; signature validation precedes repository/network work; invalid requests are `4xx`. |
| `VCS-002` | Reuse bounded archive ingestion for tag downloads. | VCS and manual sync share archive limits and cancellation; transient upstream/publish failures return retryable `5xx`, success returns `2xx`. |
| `ING-004` | Provide named rate/concurrency policy infrastructure. | Named policies have validated configuration, stable partitioning contracts, common overload responses, and metrics; API-key verifier concurrency remains independently bounded by `IAM-007`. |
| `ING-005` | Bound pending durable work. | Configuration limits pending/claim batch/worker concurrency and retention. Admission applies backpressure or an explicit retryable rejection before unbounded backlog growth, and overload affects readiness/metrics according to a documented policy. |
| `ING-006` | Attach rate/concurrency policy to resource-intensive ingress. | Module/provider upload, public webhook, API-key verification, and mirror-triggering endpoints select the documented policy/partition and return observable `429` responses under deterministic tests. |

### 4.4 Deployment integrity

| ID | Requirement | Acceptance criteria |
|---|---|---|
| `DEP-001` | Support private Azure downloads with either shared-key or managed-identity authentication. | TokenCredential clients issue usable user-delegation SAS URLs for modules and providers; SAS failure is fail-closed; shared-key behavior remains compatible. |
| `DEP-002` | Make default and mounted stores writable by the declared non-root container user. | A container running as the Dockerfile user creates, uploads, and downloads module and provider artifacts without an ownership override. |
| `DEP-003` | Build the generated frontend and application image from the same commit. | CI injects a unique frontend marker and asserts it exists in the image; stale checked-in output cannot be shipped. |

### 4.5 Identity, authorization, credentials, and proxy security

| ID | Requirement | Acceptance criteria |
|---|---|---|
| `IAM-001` | Prevent role resurrection. | Removing a user's final role remains effective across request, restart, JWT, and API-key paths; zero roles produces zero permissions without request-time assignment. |
| `IAM-002` | Make admission policy explicit. | Production selects closed/existing-users-only, allowlist, or constrained auto-provisioning. Tests cover issuer, tenant/organization, domain, verified-email, allowlist, and missing-claim behavior. Unconstrained OIDC auto-provisioning is not silently enabled. |
| `IAM-003` | Restrict mutations by namespace ownership. | Maintainers can mutate owned namespaces; users cannot replace/delete another namespace; admin/system override is explicit and audited. |
| `IAM-004` | Separate high-impact permissions. | Replace, purge, role management, key creation, and shared-key creation require distinct enforced permissions. |
| `IAM-005` | Enforce `api_keys.manage`. | Every key controller operation returns `403` without the permission. API-key principals cannot mint a successor unless policy explicitly permits it. |
| `IAM-006` | Replace password-hardening for random API tokens. | New keys use a versioned keyed digest with fixed-time comparison. Existing Argon2 hashes remain usable through a dual-verifier and upgrade-on-use window. |
| `IAM-007` | Bound credential verification and writes. | Per-prefix/principal concurrency and rate limits are enforced; `last_used_at` writes are coalesced or throttled. |
| `IAM-008` | Make artifact download tokens stateless and bounded. | Module/provider download tokens are purpose-scoped, expire automatically, enforce the intended replay policy, and work across two instances without process dictionaries. |
| `IAM-009` | Trust forwarded headers only from configured proxies. | A trusted proxy produces the correct scheme, client IP, and `Secure` cookies; headers from untrusted peers are ignored. Production auth cookies are always secure. |
| `IAM-010` | Validate local authentication redirects. | Local return paths reject backslashes, controls, scheme-relative values, and external destinations. |
| `IAM-011` | Keep the automatically assigned built-in role least-privileged. | The default role is read-only and never receives upload, replace, delete, restore, purge, role-management, shared-key, or operator permissions automatically. Elevated rights require an explicit assignment. |
| `IAM-012` | Enforce active state and credential revocation. | A disabled/offboarded user is denied through cookie, JWT, API key, and Terraform login paths; logout/revocation updates server-checked state immediately rather than waiting for token expiry. |
| `IAM-013` | Make Terraform authorization codes durable and single-use. | Codes work across two instances, survive the required restart window, expire automatically, bind to client/redirect/PKCE values, and are consumed transactionally once. |

For legacy modules with no owner, migration must assign a controlled system/bootstrap-admin owner or leave mutation admin-only until an explicit claim. It must not grant ownership to the first caller.

### 4.6 Mirror correctness and governance

| ID | Requirement | Acceptance criteria |
|---|---|---|
| `MIR-001` | Make host and authentication settings authoritative. | Each allowed hostname maps to its actual upstream; ambiguous mappings fail startup. Module and provider authentication switches alter endpoint behavior. |
| `MIR-002` | Enforce configured timeouts and admission limits. | Default global concurrency is 4 and per-coordinate concurrency is 1; observed concurrency never exceeds configuration. |
| `MIR-003` | Enforce a cache budget and deterministic eviction. | Default total cache remains at or below 100 GiB, accounting includes all stored artifacts, and eviction never removes an in-use object. |
| `MIR-004` | Implement negative caching. | Misses are cached for the configured default 60 seconds and expire predictably. |
| `MIR-005` | Provide distributed single-flight behavior. | One hundred concurrent misses cause one upstream fetch and equivalent client responses, with no transient false `404`. |
| `MIR-006` | Heartbeat and honor lease loss. | Long downloads renew ownership; a worker that loses a lease does not publish or delete another worker's result. |
| `MIR-007` | Fetch provider platforms lazily. | Version metadata does not download every platform package. Shared checksum/signature material is fetched at most once per version refresh. |
| `MIR-008` | Verify provider signatures before readiness. | Trusted signatures succeed; tampered package/checksum/signature and unknown keys fail closed; persisted state records verified identity. |
| `MIR-009` | Expose an audited operator control plane. | Authorized operators can inspect/update effective config, view cache/lease/job state, and purge safely. Existing mirror permissions are enforced by API and UI. |

SSRF protections already present—scheme restrictions, address validation, pinned connections, disabled automatic redirects, and redirect revalidation—must remain covered by regression tests.

### 4.7 Performance, background work, and delivery

| ID | Requirement | Acceptance criteria |
|---|---|---|
| `PERF-001` | Page module coordinates in SQL. | Search, count, and coordinate pagination execute in at most three database round trips; transferred/materialized rows are bounded by the page coordinates plus versions belonging to those coordinates, not total registry size. SQLite/PostgreSQL parity tests pass. |
| `PERF-002` | Stream provider uploads through bounded storage. | Declared and chunked oversize requests are rejected; hashing/validation uses a bounded temporary file; per-active-upload application-managed buffering is at most 4 MiB excluding framework/SDK overhead. |
| `PERF-003` | Reduce provider package lookup round trips. | Package lookup uses at most two database round trips; independent storage URL creation runs concurrently without changing error or cancellation behavior. |
| `PERF-004` | Propagate request cancellation. | `RequestAborted` reaches repositories, HTTP clients, storage, parsing, and long-running workers; cancellation cannot leave a partial commit. |
| `REL-001` | Replace detached loss-intolerant tasks. | Audit and webhook delivery use bounded durable work/outbox semantics with drain, retry, idempotency, and observable failure. |
| `REL-002` | Treat analytics loss policy explicitly. | Analytics is either durable or intentionally bounded/lossy with drop metrics; no unobserved `Task.Run` remains. |
| `REL-003` | Apply safe HTTP delivery policies. | Brotli/gzip is negotiated; hashed public assets receive immutable caching; authenticated/API responses are not accidentally cached. |
| `REL-004` | Apply browser-facing security headers. | Production responses use HSTS at the appropriate boundary and a tested CSP and baseline security-header policy without breaking OIDC, Swagger policy, or generated frontend assets. |
| `SUP-001` | Remediate production High/Critical dependency findings. | NuGet audit has no High/Critical findings and no blanket warning suppression is introduced. |
| `SUP-002` | Pin build inputs used in release artifacts. | Terraform, `terraform-config-inspect`, container bases, and relevant tool/action versions are reproducible and recorded. |
| `SUP-003` | Resolve or formally accept the currently unreachable frontend advisory. | The affected Nuxt UI components remain absent, or the package is upgraded; any exception names an owner, expiry date, reachability test, and compensating control. |

Broad cancellation propagation is deliberately scheduled after repository and storage interface changes to avoid repeated mechanical conflicts.

## 5. Compatibility and rollout policy

### 5.1 Database changes

- Take and verify a backup before every schema phase.
- Test the exact production binary against a restored copy.
- Never roll back by deleting DbUp journal rows or manually reversing schema.
- Use expand/dual-read/backfill/contract for user status, ownership, API-key hash versions, durable jobs, and outbox tables.
- Retain old read compatibility for at least one deployed application version.
- Record which application/image versions can run against each schema version.

### 5.2 Artifact compatibility

- Existing artifact records must be classified by bytes before their advertised format changes.
- If an artifact cannot be read or classified, mark it unavailable with an actionable operator state; never guess from its current `.zip` name.
- Signed cloud URLs must add archive hints before canonical request signing.
- Replacement and migration must not silently rewrite user artifacts without a recorded checksum transition.

### 5.3 Credentials

- New digest formats include an algorithm/version and signing-key identifier.
- Old API keys are upgraded only after successful legacy verification.
- Signing-key rotation supports an overlap window; verification accepts active and previous keys, while issuance uses only the active key.
- Disabling a user or revoking a key takes effect through server-checked state and does not wait for a JWT to expire.

## 6. Verification matrix

| Area | Required scenarios |
|---|---|
| Migration | Fresh DB; populated pre-010; journaled 010; interrupted state; second run; backup restore; row and relationship comparison; SQLite FK check; PostgreSQL Testcontainer. |
| Module protocol | ZIP and tar.gz; API and VCS publishing; exact/latest endpoints; relative URLs; Local/Azure/S3; the exact Terraform CLI matrix pinned by the bootstrap PR. |
| Storage | Concurrent create/replace/purge; DB failure; upload failure; promotion failure; cleanup failure; process termination; stale attempt; previous winner remains readable. |
| Extraction | Corrupt/truncated input; compression bomb; too many entries; oversized entry; traversal/link; queue pressure; cancellation; restart; stale lease; retry idempotency. |
| Authorization | Anonymous; static system token; cookie; OIDC; API key; no role; removed role; disabled user; wrong tenant/domain; namespace owner/non-owner; admin; revoked/expired credential. |
| Mirror | Same-key contention; distinct-key load; slow upstream; timeout; negative cache; cache eviction; lease loss/heartbeat; lazy platforms; signature failure; auth on/off. |
| Deployment | Non-root container; mounted/default stores; generated frontend marker; immutable image digest; forwarded proxy; Azure shared key and managed identity; S3-compatible service. |
| Performance | At least 100,000 module versions; query plans; at most three DB round trips for a list page; materialized rows bounded by page coordinates plus their versions; no more than 4 MiB application-managed buffering per active archive/provider upload; cache and queue pressure; reproducible before/after latency/allocation evidence. Any hardware-dependent latency threshold must be approved in a specification amendment before implementation. |

Mock-only tests are insufficient for user-delegation SAS canonicalization, S3 conditional writes/races, Terraform/go-getter behavior, or the final non-root container.

## 7. Observability requirements

Add or retain structured metrics for:

- migration version/state and readiness failures;
- rejected archives by reason and rejected/expanded bytes;
- extraction queue depth, claim latency, attempts, stale jobs, and dead letters;
- publication attempts, conflicts, rollbacks, orphan cleanup, and reconciliation drift;
- authentication denial reason, disabled-user attempts, key verification throttling, and admission decisions without credential contents;
- mirror active downloads, per-coordinate waiters, lease loss, upstream requests, negative-cache hits, cache bytes, and evictions;
- webhook/outbox queue depth, retries, age, failures, and drops;
- database query duration and returned-row counts for paginated list paths.

Logs and metrics must never contain PATs, webhook secrets, API keys, authorization codes, SAS tokens, or complete signed download URLs.

## 8. Definition of done

A requirement is complete only when:

1. a confirmed behavioral defect has a targeted regression test that fails against the baseline and passes with the change; procedural, evidence-only, and preventive requirements instead have an executable compliance check or recorded acceptance evidence;
2. unit, integration, backend contract, and relevant real-system tests pass;
3. failure, retry, cancellation, rollout, and rollback behavior is documented in the PR;
4. configuration has validation, secure defaults, and operator documentation;
5. new schema is covered by fresh, populated-upgrade, journaled, and compatibility tests;
6. observability needed to operate the behavior is present;
7. the phase acceptance branch passes the full gate in the delivery plan;
8. the requirement's owning phase PR merges into `develop` and its post-merge workflow is green.

Unrelated cleanup, broad renaming, dependency upgrades, and mechanical cancellation edits must not be mixed into correctness/security PRs unless the work package explicitly owns them.
