# S3 Storage Provider Design

**Date:** 2026-04-28

**Status:** Approved in chat, pending final spec review

## Goal

Add support for a new `TerraformRegistry.S3` module storage provider that works with both AWS S3 and S3-compatible endpoints, follows the existing Azure Blob provider pattern, and preserves the current Terraform module registry API surface.

## Context

The registry currently supports two module storage backends:

- `LocalModuleService` in the main web application, which stores archives on disk and serves downloads through `/module/download?token=...`
- `AzureBlobModuleService` in the separate `TerraformRegistry.AzureBlob` project, which stores archives in object storage and returns direct SAS download URLs

The new S3 provider should follow the Azure pattern rather than the local pattern:

- object content lives in S3-compatible storage
- module metadata remains in the database
- downloads are delegated to pre-signed object URLs

## Scope

In scope:

- new `TerraformRegistry.S3` project
- new `S3ModuleService` implementing `IModuleService` through `ModuleService`
- app wiring for `StorageProvider = "s3"`
- configuration for AWS S3 and S3-compatible endpoints
- upload, download, purge, readiness, and startup reconciliation behavior
- TDD-oriented implementation plan and tests
- README and appsettings documentation updates

Out of scope:

- refactoring existing Azure or local providers behind a new shared object-storage abstraction
- Terraform provider registry protocol support
- automatic bucket creation
- multipart upload optimization
- storage migration tooling between providers

## Recommended Approach

Implement S3 support as a new provider-specific assembly parallel to `TerraformRegistry.AzureBlob`.

This keeps the storage-provider selection model unchanged:

- `Program.cs` continues to select a single `IModuleService` implementation based on `StorageProvider`
- the API handlers remain storage-agnostic
- `S3ModuleService` encapsulates S3-specific configuration, object operations, pre-signed URL generation, and reconciliation logic

This is the lowest-risk path because it matches the current repository structure and avoids broad refactoring before the feature is usable.

## Architecture

### Project Layout

Add a new class library project:

- `TerraformRegistry.S3`

Responsibilities:

- depend on `TerraformRegistry.API`
- host `S3ModuleService`
- own AWS SDK dependencies and S3-specific logic

Update existing wiring:

- add project reference from `TerraformRegistry` to `TerraformRegistry.S3`
- add project reference from `TerraformRegistry.Tests` to `TerraformRegistry.S3`
- extend `Program.cs` to construct `S3ModuleService` when `StorageProvider` is `s3`

### Service Responsibilities

`S3ModuleService` should mirror the Azure provider's behavior:

- delegate list/get/version/delete/restore/description operations to `IDatabaseService`
- upload module archives to object storage
- store the object key in `ModuleStorage.FilePath`
- generate pre-signed GET URLs in `GetModuleDownloadPathAsync(...)`
- purge database metadata first, then best-effort delete the object
- reconcile bucket contents into the database during startup
- implement `CheckStorageAsync()` for readiness checks

### Download Behavior

The application already supports storage-specific download targets through `IModuleService.GetModuleDownloadPathAsync(...)`.

S3 should follow the Azure behavior:

- return a direct pre-signed object URL
- let module handlers continue to set `X-Terraform-Get`
- return `204` for Terraform CLI requests
- redirect browsers with `302` to the returned pre-signed URL

No new download endpoint is required for S3.

## Configuration

Use a new `S3` configuration section.

Required:

- `S3:BucketName`
- `S3:Region`

Optional:

- `S3:ServiceUrl`
- `S3:ForcePathStyle` default `false`
- `S3:PresignedUrlExpiryMinutes` default `5`
- `S3:AccessKeyId`
- `S3:SecretAccessKey`
- `S3:SessionToken`

### Credential Resolution

Recommended credential order:

1. If both `S3:AccessKeyId` and `S3:SecretAccessKey` are present, use explicit credentials
2. Include `S3:SessionToken` when provided
3. Otherwise fall back to the AWS SDK default credential chain

This gives strong compatibility across:

- AWS IAM roles and instance/task identities
- local development with the default AWS SDK chain
- S3-compatible deployments that require explicit keys and a custom endpoint

### Endpoint Compatibility

The provider must support both AWS S3 and S3-compatible endpoints.

Expected behavior:

- default AWS usage works with `Region` and no custom endpoint
- `ServiceUrl` enables custom endpoints such as MinIO, R2, or Ceph RGW
- `ForcePathStyle` supports targets that do not support or do not prefer virtual-host addressing

## Storage Model

Object keys should follow the same convention used by the Azure provider:

- `{namespace}/{name}-{provider}-{version}.zip`

Database storage remains unchanged:

- `ModuleStorage.FilePath` stores the object key
- the database remains the source of truth for module listing and version metadata

The object store remains the source of truth for archive bytes.

## Runtime Behavior

### Startup

On construction, `S3ModuleService` should:

- create or obtain the S3 client using resolved configuration
- validate that required configuration is present
- verify bucket access
- run a reconciliation pass that scans objects and inserts missing database metadata

It should not create buckets automatically. Bucket provisioning is a deployment concern and auto-creation is error-prone across AWS regions and S3-compatible implementations.

### Upload

Upload flow:

1. Build the object key from module coordinates
2. Check whether the object already exists
3. If it exists and `replace` is `false`, return `false`
4. If it exists and `replace` is `true`, remove the existing object first
5. Upload the new archive object, optionally attaching object metadata
6. Write the `ModuleStorage` row to the database
7. If the database write fails, best-effort delete the uploaded object and return `false`

Object metadata should include enough fields to support later recovery:

- `namespace`
- `name`
- `provider`
- `version`
- `description`
- `publishedAt`

### Download

Download flow:

1. Query `IDatabaseService.GetModuleStorageAsync(...)`
2. If no record exists, return `null`
3. Confirm the object exists
4. Generate a pre-signed GET URL using `PresignedUrlExpiryMinutes`
5. Return the URL string

If object lookup or pre-signing fails, log the failure and return `null`.

### Delete and Restore

Soft delete and restore remain database operations only:

- `DeleteModuleVersionAsync(...)` calls `SoftDeleteModuleAsync(...)`
- `RestoreModuleVersionAsync(...)` calls `RestoreModuleAsync(...)`

This matches current local and Azure behavior.

### Purge

Purge flow:

1. Load the module storage row including deleted entries
2. Remove the database row permanently
3. Best-effort delete the object from S3
4. Log storage deletion failures but still return success when the database delete succeeded

This matches the current Azure semantics and keeps the metadata source consistent.

### Health Checks

`CheckStorageAsync()` should verify bucket reachability and basic access.

A healthy result means:

- the bucket is reachable with the configured client
- the registry has enough permissions to inspect it

An unhealthy result should include a provider-specific reason so `/ready?detail=true` can report a useful failure cause.

## Reconciliation Behavior

Startup reconciliation should mirror the Azure provider's intent.

Algorithm:

1. Enumerate bucket objects
2. Prefer object metadata when present
3. Fall back to parsing the object key when metadata is missing
4. Validate coordinates and SemVer
5. Skip invalid objects
6. Query the database for an existing module row
7. Insert only when the module is missing from the database
8. Log and continue on per-object failures

This supports:

- recovering metadata after database replacement
- preserving object-store-backed modules across database rebuilds
- operating against buckets that were populated outside the current app instance

## Testing Strategy

Implementation should be developed TDD style.

That means the execution plan should center each behavior around:

1. write a failing test
2. run the targeted test and confirm failure
3. implement the minimum code to pass
4. rerun the targeted test
5. run the broader relevant suite

### Test Areas

Provider-specific unit coverage should mirror the existing Azure test organization:

- constructor/configuration tests
- delegation tests
- upload tests
- download tests
- reconciliation tests
- purge tests
- health-check tests

Specific cases to cover:

- missing `BucketName`
- missing `Region` in normal AWS mode
- explicit credentials path
- default credential-chain path
- custom endpoint plus path-style configuration
- existing object with `replace = false`
- replace flow with prior object removal
- database failure after upload triggers object cleanup
- missing DB row on download returns `null`
- missing object on download returns `null`
- successful pre-signed URL generation
- SDK exception during pre-signing returns `null`
- metadata-based reconciliation
- key-parsing fallback reconciliation
- invalid object names or versions are ignored
- duplicate suppression when DB row already exists
- purge succeeds even if object deletion fails after DB removal
- readiness reports unhealthy storage when bucket access fails

## Documentation Changes

Update user-facing documentation to include S3 support:

- `README.md`
- `TerraformRegistry/appsettings.json`
- `TerraformRegistry/appsettings.Development.json`

Documentation should cover:

- `StorageProvider = s3`
- S3 configuration variables
- AWS and S3-compatible deployment examples
- explanation that downloads are delivered by pre-signed URLs

## Risks and Constraints

- S3-compatible endpoints vary in path-style and signing expectations, so endpoint configuration must stay explicit
- pre-signed URL generation depends on correct client configuration and clock correctness
- startup reconciliation must tolerate malformed objects without failing app startup
- do not introduce a shared abstraction refactor as part of this feature

## Acceptance Criteria

The feature is complete when:

- the app accepts `StorageProvider = s3`
- module upload works against AWS S3 and configurable S3-compatible endpoints
- module download returns valid pre-signed URLs
- list/get/version/delete/restore behavior remains unchanged
- purge removes DB metadata and best-effort removes objects
- readiness reflects S3 storage health
- startup reconciliation restores missing DB metadata from bucket contents
- automated tests cover the new provider behavior
- README and configuration docs describe the S3 provider clearly
