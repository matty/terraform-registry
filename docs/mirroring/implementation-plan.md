# Terraform Registry Mirroring Implementation Plan

**Goal:** Add safe provider and module read-through mirroring, with admin controls, UI management, and Terraform CLI smoke coverage.

**Architecture:** Mirror support is additive: local registry content wins, and upstream content is fetched only when policy allows it. Provider mirroring uses the Terraform provider network mirror protocol under `/mirror`; module mirroring uses registry protocol fallback under `/v1/modules`; admin APIs and UI expose cache/config state without changing Terraform-facing routes.

**Tech Stack:** ASP.NET Core minimal APIs, C# 13/.NET 10, SQLite, PostgreSQL, Nuxt 3, Terraform CLI protocol behavior, existing unit/integration test suites.

---

## Protocol and Security Requirements

- Provider network mirror endpoints are plain HTTPS paths under `/mirror/providers/...`; they do not use Terraform service discovery.
- Provider metadata endpoints may require Terraform credentials, but provider archive URLs must be signed because Terraform does not forward credentials to package URLs.
- Provider hashes must be Terraform-format `zh:` and/or `h1:` strings; do not emit bare SHA-256 hashes.
- Module read-through must preserve Terraform module registry behavior:
  - local module details, versions, and downloads win over upstream data
  - missing local module versions may fall back to upstream registry APIs
  - `X-Terraform-Get` values may be relative URLs
  - go-getter suffixes such as `//*?archive=tar.gz` must be preserved
  - archive-only query hints such as `?archive=tar.gz` must be preserved after caching
  - recursive module registry addresses, including `registry.terraform.io/ns/name/provider` and `https://registry.terraform.io/ns/name/provider`, must be rejected
- SSRF defenses apply before every network fetch:
  - DNS must resolve to public addresses only
  - private, loopback, link-local, multicast, documentation ranges, and other reserved addresses are blocked
  - redirect targets must be revalidated
  - module `/download` discovery must not auto-follow redirects
- Mirror cache writes must not overwrite user-owned local content:
  - a local non-mirror module wins unchanged even if stale mirror cache rows exist
  - a concurrent local/API upload must not be replaced by a mirror refresh
  - replacing existing mirror content must be guarded so the current row is still mirror-owned
- S3, Azure Blob, local, SQLite, and PostgreSQL paths must preserve `ModuleArtifactMetadata.Source`.
- `/mirror` must be treated as API fallback, not SPA fallback.
- Internal agent planning files outside `docs/mirroring` are local artifacts only and must not be committed unless explicitly requested.

---

## Completed Delivery State

- [x] Task 1: mirror runtime configuration and permissions
- [x] Task 2: mirror cache schema, repositories, and leases
- [x] Task 3: mirror policy, hardened HTTP fetches, DNS pinning, and lease service
- [x] Task 4: provider network mirror endpoint
- [x] Task 5: module read-through mirror cache
- [x] Task 6: admin mirror API
- [x] Task 7: admin web UI
- [x] Task 8: documentation, Terraform CLI smoke tests, and final verification

Task 5 follow-up fixes, admin APIs, admin UI, documentation, smoke scripts, and final verification are implemented in the current working tree.

---

## Task 5 Follow-Up: Module Mirror Quality Fixes

**Files:**
- Modify: `TerraformRegistry/Services/Mirror/ModuleMirrorService.cs`
- Modify: `TerraformRegistry/Services/Sqlite/SqliteModuleRepository.cs`
- Modify: `TerraformRegistry.PostgreSQL/Repositories/PostgreSqlModuleRepository.cs`
- Modify: `TerraformRegistry.S3/S3ModuleUploadWorkflow.cs` if needed by tests
- Test: `TerraformRegistry.Tests/UnitTests/ModuleMirrorServiceTests.cs`
- Test: `TerraformRegistry.Tests/UnitTests/S3/S3ModuleServiceUploadTests.cs`
- Test: database repository tests covering exact replacement metadata, if present

- [x] **Step 1: Add a race regression test for mirror replacement**

Add a test to `ModuleMirrorServiceTests` for this sequence:

1. initial local download lookup misses
2. upstream `/download` returns a valid `X-Terraform-Get`
3. service observes an existing mirror-owned module and prepares to refresh
4. before publish, the current local module becomes `Source.Kind = "api-upload"`
5. mirror publish must not replace it
6. the returned local download path must be the non-mirror local path without archive/go-getter hints

Expected assertions:

```csharp
publish.Verify(x => x.PublishAsync(It.IsAny<ModulePublishRequest>(), It.IsAny<CancellationToken>()), Times.Never);
Assert.Equal("/module/download?token=local", result);
```

- [x] **Step 2: Add compare-and-replace protection before mirror publish**

Update `ModuleMirrorService` so mirror refresh replacement is not decided only before archive fetch. Immediately before `PublishAsync`, re-read current local module metadata. If the current module is missing, publish with `Replace = false`. If it is mirror-owned by the same origin, publish with `Replace = true`. If it is any other source, abort publish and return its local download path unchanged.

The replacement decision must happen as close to `PublishAsync` as possible. If storage/repository support cannot make this fully atomic yet, leave a code comment and test that documents the remaining compare-and-swap gap, then plan a repository-level CAS follow-up before Task 5 approval.

- [x] **Step 3: Preserve metadata in exact module replacement**

Update both repository implementations so exact replacement writes the new module metadata:

- `TerraformRegistry/Services/Sqlite/SqliteModuleRepository.cs`
- `TerraformRegistry.PostgreSQL/Repositories/PostgreSqlModuleRepository.cs`

The SQL update must include the serialized `newModule.Metadata`. Prefer including existing module identity and prior metadata in the `WHERE` clause when the repository method already has enough data to enforce exact replacement.

- [x] **Step 4: Add S3 replacement metadata coverage**

Add or update `S3ModuleServiceUploadTests` so replacing a module with mirror metadata verifies that the database replacement receives a `ModuleStorage` whose `Metadata.Source` contains:

```csharp
Kind = "mirror"
Origin = "registry.example.com"
SourceUrl = "/archives/vpc-1.2.3.zip"
ResolvedPackageUrl = "https://registry.example.com/archives/vpc-1.2.3.zip"
ArchiveFormat = "zip"
```

- [x] **Step 5: Run focused verification**

Run:

```bash
dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter "FullyQualifiedName~ModuleMirrorServiceTests|FullyQualifiedName~S3ModuleServiceUploadTests" --no-restore
dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter "MirrorRepository|ModuleRepository|DbUp" --no-restore
git diff --check
git diff --cached --check
```

Expected:

- all targeted tests pass
- no whitespace errors
- known existing `NU1903` warning for `SQLitePCLRaw.lib.e_sqlite3` may remain
- `.slopwatch/baseline.json` may be missing; report that instead of suppressing it

- [ ] **Step 6: Amend Task 5 commit and re-review**

Amend the existing Task 5 commit only. Do not stage unrelated docs or local planning artifacts.

Run two independent reviews:

- spec compliance review against Task 5 protocol/cache requirements
- code-quality review focused on SSRF, concurrency, metadata preservation, storage parity, and test quality

Task 5 is complete only when both reviews return `APPROVED`.

---

## Task 6: Admin Mirror API

**Files:**
- Create: `TerraformRegistry/Handlers/MirrorAdminHandlers.cs`
- Modify: `TerraformRegistry.Models/MirrorAdminModels.cs`
- Modify: `TerraformRegistry/Startup/MirrorEndpointMappingExtensions.cs`
- Modify: `TerraformRegistry/Startup/ServiceRegistrationExtensions.cs` if a summary service is introduced
- Test: `TerraformRegistry.Tests/IntegrationTests/MirrorAdminEndpointTests.cs`
- Test: repository tests if retry/delete needs new repository methods

Required endpoints:

- `GET /api/admin/mirror/summary` with `mirror.read`
- `GET /api/admin/mirror/config` with `mirror.configure`
- `PUT /api/admin/mirror/config` with `mirror.configure`
- `GET /api/admin/mirror/entries` with `mirror.read`
- `POST /api/admin/mirror/retry` with `mirror.manage`
- `DELETE /api/admin/mirror/providers/{hostname}/{namespace}/{type}/{version}` with `mirror.manage`
- `DELETE /api/admin/mirror/modules/{hostname}/{namespace}/{name}/{provider}/{version}` with `mirror.manage`

Implementation requirements:

- Use existing permission patterns from admin/module handlers.
- Return summaries for provider cache rows, module cache rows, failed rows, last sync timestamps, and lease state where available.
- Config read/update must go through `IMirrorConfigService`; do not bind directly to static `IOptions<MirrorOptions>` for runtime state.
- Retry must clear failed state or enqueue a retry through existing repository/service boundaries; do not fetch upstream synchronously inside the admin handler unless the existing mirror service already exposes that behavior safely.
- Delete must remove mirror cache metadata and, where safe, cached artifacts. It must not delete user-owned local modules or provider artifacts outside mirror ownership.
- Admin API routes must not interfere with Terraform-facing `/mirror` provider network mirror routes.

Review requirements:

- spec review must check route names, permissions, config runtime behavior, local content safety, and SQLite/PostgreSQL parity
- code-quality review must check authorization, deletion safety, pagination/filtering, race behavior, and handler test coverage

---

## Task 7: Admin Web UI

**Files:**
- Create: `TerraformRegistry/web-src/composables/useMirrorAdmin.ts`
- Create: `TerraformRegistry/web-src/pages/admin/mirror.vue`
- Modify: `TerraformRegistry/web-src/layouts/default.vue`
- Modify: `TerraformRegistry/web-src/composables/usePermissions.ts`
- Test/build: `TerraformRegistry/web-src`

UI requirements:

- Dense admin interface, not a landing page.
- Tabs or segmented controls for summary, cache entries, and configuration.
- Controls must reflect permission state:
  - read-only users can inspect summary/entries
  - configure controls require `mirror.configure`
  - retry/delete controls require `mirror.manage`
- Config editor must expose provider/module enablement, upstream base URL, allow/deny patterns, TTLs, max sizes, redirect limits, authentication requirements, and allowed artifact/archive hosts.
- Cache entries table must support filtering by kind, hostname, namespace/name/type, version, state, platform, and last sync.
- Retry/delete actions must show pending/loading/error states and refresh data after success.
- Avoid nested cards, hero sections, gradient backgrounds, and marketing copy.

Verification:

```bash
cd TerraformRegistry/web-src
pnpm run build
```

Also run backend admin endpoint tests before review.

---

## Task 8: Documentation, Smoke Tests, and Final Verification

**Files:**
- Modify: `README.md`
- Create or modify smoke scripts under an existing test/dev utilities location
- Do not commit local agent planning artifacts unless explicitly requested

Documentation requirements:

- Explain provider network mirror CLI configuration using HTTPS and a trailing slash.
- Note that provider mirror endpoints do not use Terraform service discovery.
- Explain credentials behavior: metadata endpoints can require credentials; archive URLs are signed.
- Explain module read-through behavior and local-first precedence.
- Document cache policy, allow/deny patterns, reserved IP blocking, TTLs, max package sizes, and redirect limits.
- Document `mirror.read`, `mirror.configure`, and `mirror.manage` permissions.
- Include operational notes for SQLite/PostgreSQL migrations and storage backends.

Smoke test requirements:

- Provider smoke test must use real Terraform required provider config.
- Provider mirror smoke must use HTTPS; if a non-443 port is used, include the port in the Terraform credentials host.
- Module smoke test must derive host from the registry URL and assert `/.well-known/terraform.json`.
- Module smoke test must perform a second `terraform init` in a fresh directory to verify cached read-through behavior.
- Smoke tests must verify no recursive registry module source is accepted.

Final verification:

```bash
dotnet test terraform-registry.sln --no-restore
cd TerraformRegistry/web-src
pnpm run build
git status --short --branch
git log --oneline --decorate -8
```

Expected:

- all tests pass, except only documented pre-existing warnings
- frontend build succeeds
- branch contains implementation commits only
- only project-facing docs under `docs/mirroring` are included
