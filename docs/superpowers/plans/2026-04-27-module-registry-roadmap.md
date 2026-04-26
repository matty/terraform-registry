# Terraform Module Registry Roadmap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the highest-value module-registry gaps by fixing the publish contract, implementing real module metadata, adding real manual publishing in the UI, and upgrading search/discovery.

**Architecture:** Treat this as four linked workstreams on top of the existing module API. First normalize the package contract and server validation so later work has a stable artifact model. Then add metadata extraction and persistence, expose it through the existing module endpoints, build UI publishing on top of the corrected upload path, and finally expand search/discovery using the new metadata model.

**Tech Stack:** ASP.NET Core minimal APIs, C#, SQLite, PostgreSQL, Nuxt 3, existing integration/unit test suites.

---

## Master Plan

### Workstream Order

1. **Archive Format and Upload Contract**
   This is the foundation. The current docs say `.tar.gz`, while the local implementation assumes `.zip`, and upload validation is minimal.

2. **Real Metadata Extraction and Persistence**
   This depends on a stable archive contract. Once package format is settled, the server can safely inspect archives and persist structured metadata.

3. **Real UI Publishing**
   The UI should use the corrected upload contract and benefit from better validation and metadata extraction.

4. **Search and Discovery**
   This should be built last because the richer metadata model will define the best filter and ranking surface.

### Cross-Cutting Constraints

- Keep the current module endpoint surface stable where possible:
  - `GET /v1/modules`
  - `GET /v1/modules/{namespace}/{name}/{provider}/{version}`
  - `GET /v1/modules/{namespace}/{name}/{provider}/versions`
  - `POST /v1/modules/{namespace}/{name}/{provider}/{version}`
- Preserve backend parity between SQLite and PostgreSQL.
- Prefer additive schema/API evolution over breaking response changes.
- Keep Terraform CLI compatibility intact for download behavior and login flow.

### Shared Success Criteria

- Manual and automated publishing use one documented, validated package contract.
- Module detail endpoints return real metadata instead of placeholders where metadata exists.
- The web UI can perform a real manual upload.
- Search can filter and rank by more than name/description.

---

## Workstream 1: Archive Format and Upload Contract

### Objective

Define one supported module archive format, document it clearly, validate it at upload time, and make storage/download behavior consistent across local and Azure-backed implementations.

### Current Gaps

- README shows `.tar.gz` upload, while local storage scans only `*.zip`.
- Upload handler checks presence of `moduleFile` but not archive type or structure.
- Download endpoint serves `application/zip` and local storage filenames assume `.zip`.
- VCS auto-publish currently streams GitHub tarballs directly into module storage without a normalized package contract.

### Scope

- Pick the canonical package format:
  - Option A: standardize on `.zip`
  - Option B: standardize on `.tar.gz`
  - Option C: accept both, normalize internally
- Add server-side validation for:
  - allowed extension/content type
  - readable archive
  - safe entry paths
  - minimum expected Terraform module content
- Align docs, upload flow, local scanning/recovery logic, and download content types.
- Decide how GitHub VCS ingestion is normalized before persistence.

### File Areas

- Backend API and handlers:
  - `TerraformRegistry/Handlers/ModuleHandlers.cs`
  - `TerraformRegistry.API/ModuleService.cs`
- Storage implementations:
  - `TerraformRegistry/Services/LocalModuleService.cs`
  - `TerraformRegistry.AzureBlob/AzureBlobModuleService.cs`
- VCS automation:
  - `TerraformRegistry/Services/GitHubVcsService.cs`
- Docs:
  - `README.md`
- Tests:
  - `TerraformRegistry.Tests/IntegrationTests/UploadModuleTests.cs`
  - `TerraformRegistry.Tests/UnitTests/LocalModuleServiceTests.cs`
  - `TerraformRegistry.Tests/UnitTests/AzureBlob/*`

### Execution Plan

- [ ] Decide the canonical archive contract and document the decision at the top of this workstream before coding.
- [ ] Add failing tests that prove the current mismatch:
  - upload accepted with wrong archive type or unreadable content
  - local scan ignores the documented format
  - VCS path bypasses format normalization
- [ ] Introduce a shared archive-validation component instead of duplicating checks in handlers and storage services.
- [ ] Update manual upload to reject invalid archives with explicit error messages.
- [ ] Update local recovery/loading logic to use the chosen package contract.
- [ ] Update VCS ingestion to either repack or explicitly validate the fetched artifact before storing it.
- [ ] Update README and any UI helper text so publish instructions match implementation exactly.
- [ ] Run upload, download, and VCS-related tests in both unit and integration suites.

### Exit Criteria

- A user following the README can publish a module successfully on the first try.
- Invalid archives fail fast with clear 400 responses.
- Local storage reload and VCS publish paths use the same archive assumptions.

---

## Workstream 2: Real Metadata Extraction and Persistence

### Objective

Replace synthetic module detail fields with real extracted metadata and persist that metadata consistently across PostgreSQL and SQLite.

### Current Gaps

- `root`, `submodules`, and `providers` are placeholders.
- `dependencies` is stored but upload paths fill it with empty arrays.
- PostgreSQL has a `metadata` JSONB column, but runtime code ignores it.
- SQLite has no equivalent metadata column.

### Scope

- Define a canonical metadata model for module archives:
  - README summary or raw README
  - required providers and constraints
  - required Terraform version
  - root module path
  - detected submodules
  - variables/outputs if feasible in phase 1
- Extend both database backends to persist and read the same structured metadata.
- Update module detail/list responses to use real metadata when present.
- Keep phase 1 focused on metadata that can be extracted reliably from packaged module contents.

### File Areas

- Models:
  - `TerraformRegistry.Models/Module.cs`
  - `TerraformRegistry.Models/ModuleListItem.cs`
  - `TerraformRegistry.Models/ModuleMetadata.cs`
  - likely new metadata DTOs under `TerraformRegistry.Models/`
- Database services and migrations:
  - `TerraformRegistry/Services/SqliteDatabaseService.cs`
  - `TerraformRegistry.PostgreSQL/PostgreSQLDatabaseService.cs`
  - `TerraformRegistry.Migrations/Scripts/SQLite/*`
  - `TerraformRegistry.Migrations/Scripts/PostgreSQL/*`
- Upload/storage services:
  - `TerraformRegistry/Services/LocalModuleService.cs`
  - `TerraformRegistry.AzureBlob/AzureBlobModuleService.cs`
- Tests:
  - database service tests
  - upload/service tests
  - module endpoint integration tests

### Execution Plan

- [ ] Write a metadata contract document inside this workstream:
  - which fields are authoritative
  - which fields are optional
  - which fields are deferred out of phase 1
- [ ] Add failing tests that assert module detail returns real metadata for a fixture module.
- [ ] Introduce a dedicated metadata extraction service that operates on the validated archive contract from Workstream 1.
- [ ] Add SQLite schema support for structured metadata.
- [ ] Wire PostgreSQL reads/writes to the existing `metadata` column.
- [ ] Persist extracted metadata on upload for both manual and VCS-driven publishes.
- [ ] Update `GET /v1/modules/{namespace}/{name}/{provider}/{version}` to map stored metadata instead of placeholders.
- [ ] Optionally expose a subset of metadata in list responses if it helps discovery without bloating responses.
- [ ] Add migration tests and regression tests for both storage backends.

### Exit Criteria

- The detail endpoint no longer hardcodes `root`, `submodules`, and `providers` for newly published modules.
- Metadata is persisted and reloaded consistently on both SQLite and PostgreSQL.
- Existing modules without metadata still render safely with sensible fallback behavior.

---

## Workstream 3: Real UI Publishing

### Objective

Turn the current “Create Module” modal into a real manual publishing flow instead of a GitHub-link-only flow.

### Current Gaps

- Non-GitHub “Create Module” currently closes the modal and refreshes without creating anything.
- There is no frontend upload path for `moduleFile`.
- The UI does not expose server-side validation or archive-contract guidance.

### Scope

- Add a true manual upload flow in the Nuxt UI.
- Preserve GitHub linking as an optional alternative path, not the only meaningful action.
- Surface validation errors clearly.
- Consider whether module creation should support:
  - upload only
  - upload + description
  - upload + optional VCS link in one flow

### File Areas

- UI:
  - `TerraformRegistry/web-src/pages/index.vue`
  - `TerraformRegistry/web-src/composables/useModules.ts`
  - possibly new components under `TerraformRegistry/web-src/components/`
- Backend:
  - existing upload endpoint in `TerraformRegistry/Handlers/ModuleHandlers.cs`
  - maybe minor response shape improvements if needed
- Tests:
  - frontend composable/component tests if present
  - integration tests for upload UX assumptions

### Execution Plan

- [ ] Decide the target UX:
  - separate “Upload Module” and “Link GitHub” actions, or
  - one modal with explicit mode switch
- [ ] Add a frontend upload API helper to `useModules.ts` using `multipart/form-data`.
- [ ] Replace the current no-op manual path with a real file picker, version input, description input, and submit action.
- [ ] Show archive-format requirements based on the contract from Workstream 1.
- [ ] Surface server validation failures inline in the modal.
- [ ] Decide whether successful upload should redirect directly to the module page or just refresh the list.
- [ ] Keep GitHub-link creation working and make the action labels unambiguous.
- [ ] Add UI-level regression coverage around both upload and GitHub-link modes.

### Exit Criteria

- A user can upload a module archive from the web UI without using curl or GitHub webhooks.
- The action labels and behavior match the actual outcome.
- Validation errors are clear enough that users can self-correct without logs.

---

## Workstream 4: Search and Discovery

### Objective

Expand module discovery from basic text search into a structured search surface that uses newly available metadata.

### Current Gaps

- Search request only supports `q`, `namespace`, `provider`, `offset`, `limit`.
- Search only matches name and description.
- Response metadata does not include total count or paging state beyond current offset and limit.

### Scope

- Define additional search/filter fields:
  - version presence
  - provider requirement
  - Terraform version compatibility
  - tags/categories
  - VCS-linked/manual status if useful
- Improve result metadata:
  - total count
  - next offset or `has_more`
  - optional sort field echo
- Upgrade UI list/search controls to use the expanded backend.

### File Areas

- Models:
  - `TerraformRegistry.Models/ModuleSearchRequest.cs`
  - `TerraformRegistry.Models/ModuleList.cs`
- Database services:
  - `TerraformRegistry/Services/SqliteDatabaseService.cs`
  - `TerraformRegistry.PostgreSQL/PostgreSQLDatabaseService.cs`
- API handlers:
  - `TerraformRegistry/Handlers/ModuleHandlers.cs`
- UI:
  - `TerraformRegistry/web-src/pages/index.vue`
  - `TerraformRegistry/web-src/composables/useModules.ts`
- Tests:
  - database search tests
  - integration tests for list/search

### Execution Plan

- [ ] Define the first-phase filter set, keeping it small and directly supported by metadata from Workstream 2.
- [ ] Add failing database-level tests for new filter and paging behavior.
- [ ] Extend `ModuleSearchRequest` and handler parsing.
- [ ] Update both database implementations with matching query semantics.
- [ ] Add total count / `has_more` response metadata and adapt the frontend paging behavior.
- [ ] Update the module list UI to expose the selected filters without overcomplicating the page.
- [ ] Add integration coverage for combined filters and pagination edge cases.

### Exit Criteria

- Search can answer questions more specific than free-text name/description matching.
- Frontend pagination reflects real server state.
- SQLite and PostgreSQL return consistent result sets for the same filters.

---

## Recommended Delivery Slices

### Slice A

- Workstream 1 only
- Output: stable archive contract, validated uploads, corrected docs

### Slice B

- Workstream 2 on top of Slice A
- Output: real module metadata extraction and persistence

### Slice C

- Workstream 3 on top of Slices A-B
- Output: usable manual publishing UI

### Slice D

- Workstream 4 on top of Slices A-B
- Output: richer discovery powered by metadata

### Recommended First Implementation Target

Start with **Workstream 1: Archive Format and Upload Contract**. It has the highest leverage because it removes current user confusion and defines the artifact boundary that the other three workstreams depend on.

