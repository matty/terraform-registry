# Mirror Control Plane Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide authorized operators an audited API and UI to inspect and safely manage the Terraform mirror runtime.

**Architecture:** Build an admin-only handler layer over the existing mirror configuration, cache repositories, and lease repository. Add small repository query/delete operations for the state already persisted by the mirror, then expose them through the established minimal-API and Nuxt admin conventions. Mutations must use request cancellation and emit audit events.

**Tech Stack:** ASP.NET Core minimal APIs, C#/.NET 10, SQLite/PostgreSQL repositories, xUnit/Moq, Nuxt/Vue/TypeScript.

## Global Constraints

- Reuse `mirror.read`, `mirror.manage`, and `mirror.configure`; do not grant mirror access to default users.
- Read operations must not mutate cache state; purge must be coordinate-specific and never delete a live lease.
- Every configuration or purge mutation emits an audit event containing non-secret coordinates and outcome.
- API and UI permission checks must both be covered by automated tests.
- Use cancellation tokens on new repository/storage operations.

---

### Task 1: Mirror admin contracts and read paths

**Files:**
- Modify: `TerraformRegistry.API/Interfaces/IProviderMirrorRepository.cs`
- Modify: `TerraformRegistry.API/Interfaces/IModuleMirrorRepository.cs`
- Modify: `TerraformRegistry.API/Interfaces/IMirrorLeaseRepository.cs`
- Modify: `TerraformRegistry.Models/MirrorAdminModels.cs`
- Modify: `TerraformRegistry/Services/Sqlite/SqliteProviderMirrorRepository.cs`
- Modify: `TerraformRegistry/Services/Sqlite/SqliteModuleMirrorRepository.cs`
- Modify: `TerraformRegistry/Services/Sqlite/SqliteMirrorLeaseRepository.cs`
- Modify: PostgreSQL mirror repository counterparts
- Test: `TerraformRegistry.Tests/UnitTests/Database/*Mirror*Admin*Tests.cs`

- [ ] Write failing SQLite and PostgreSQL parity tests for cache summary, bounded package list, and lease list.
- [ ] Run each focused test and confirm it fails because the admin query contract is absent.
- [ ] Add `MirrorCacheSummary`, `MirrorCachePage<T>`, and bounded `ListLeasesAsync`/summary repository contracts, then implement equivalent SQLite/PostgreSQL SQL queries.
- [ ] Run focused tests and confirm they pass.
- [ ] Commit with `feat: add mirror administration queries`.

### Task 2: Safe cache purge contract

**Files:**
- Modify: mirror repository interfaces and SQLite/PostgreSQL implementations
- Modify: `TerraformRegistry.Models/MirrorAdminModels.cs`
- Test: mirror repository tests and handler tests

- [ ] Write failing tests proving purge deletes only the selected cached record and refuses an active matching lease.
- [ ] Run focused tests and confirm the missing purge operation fails.
- [ ] Add coordinate-specific purge methods that return an explicit not-found, purged, or in-use result; remove only database metadata here, leaving artifact deletion to the storage-aware handler/service.
- [ ] Run focused tests and confirm successful purge, no-op missing coordinate, and in-use protection.
- [ ] Commit with `feat: protect mirror cache purges`.

### Task 3: Permissioned, audited mirror admin API

**Files:**
- Create: `TerraformRegistry/Handlers/MirrorAdminHandlers.cs`
- Modify: `TerraformRegistry/Startup/AdminEndpointMappingExtensions.cs`
- Modify: service registration only if a focused mirror-admin coordinator is required
- Test: `TerraformRegistry.Tests/UnitTests/MirrorAdminHandlersTests.cs`
- Test: `TerraformRegistry.Tests/IntegrationTests/MirrorAdminEndpointTests.cs`

- [ ] Write failing handler/integration tests for 403 without each required permission, 200 for read/configuration, audit records for mutation, and 409 for an in-use purge.
- [ ] Run focused tests and confirm route/handler absence causes the expected failures.
- [ ] Implement routes under `/api/admin/mirror`: config GET/PUT, summary, provider/module cache lists, leases list, and coordinate-specific purge. Use `RequestAborted`, clamp pagination, and use `FireAuditLog` for config update/purge.
- [ ] Run focused tests and confirm all authorization, audit, cancellation, and purge assertions pass.
- [ ] Commit with `feat: add audited mirror administration API`.

### Task 4: Mirror admin UI

**Files:**
- Create: `TerraformRegistry/web-src/composables/useMirrorAdmin.ts`
- Create: `TerraformRegistry/web-src/pages/admin/mirror.vue`
- Modify: `TerraformRegistry/web-src/layouts/default.vue`
- Modify: `TerraformRegistry/web-src/pages/admin/roles.vue`
- Test: frontend build/audit commands and endpoint integration tests

- [ ] Add a failing type/build usage for the mirror admin composable and page navigation permission.
- [ ] Run `npm`/frontend build command and confirm the new imports fail before implementation.
- [ ] Implement typed API calls and a permission-gated page showing effective config, cache/lease status, paginated cache entries, and confirmation-gated coordinate purge; never render signing keys or secrets.
- [ ] Run frontend build/audit and focused API tests; confirm both pass.
- [ ] Commit with `feat: add mirror operator console`.

### Task 5: Review and verification

**Files:** all changed files

- [ ] Run `dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --no-restore --logger "console;verbosity=minimal" --blame-hang --blame-hang-timeout 5m`.
- [ ] Run the repository slopwatch check, `git diff --check`, targeted formatting, frontend build/audit, and Docker emulator Terraform contract.
- [ ] Inspect the diff for secret exposure, route authorization gaps, and unintended schema changes.
- [ ] Commit any final correction with a descriptive scope-specific message.
- [ ] Create a PR, watch every GitHub check/review/thread, resolve verified findings, and squash-merge only after `mergeStateStatus` is `CLEAN`.

## Self-review

MIR-009 is covered by Tasks 1–4: effective configuration, cache and lease visibility, safe purge, audit records, API authorization, and UI authorization. The plan does not add a new job table because the existing mirror implementation uses leases rather than a durable mirror job queue; the control plane exposes that live work state. No signing key or other secret is returned to the browser.
