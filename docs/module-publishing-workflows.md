# Module Publishing Workflows

This registry supports multiple publish entry points for private Terraform modules. The same backend publish path is used for API uploads, portal uploads, GitHub webhook publishes, and manual GitHub sync operations.

## Permissions

- `modules.upload`: allows manual module version upload through the web UI and direct authenticated `POST /v1/modules/{namespace}/{name}/{provider}/{version}` requests.
- `vcs.manage`: allows linking a module coordinate to a GitHub repository, importing existing tags, manually syncing linked repositories, and disconnecting a linked source.

## Manual Portal Upload

Users with the `modules.upload` permission can publish a module archive through the web UI.

Required fields:

- namespace
- name
- provider
- version
- `.zip` module archive

Optional fields:

- description
- replace existing version

The portal calls the same upload endpoint used by scripted clients:

```text
POST /v1/modules/{namespace}/{name}/{provider}/{version}
```

## GitHub-Linked Modules

Users with the `vcs.manage` permission can link a module coordinate to a GitHub repository and an existing VCS connection.

During linking, operators can enable `Import existing tags now` to backfill missing semantic-version tags immediately. That create flow returns a sync summary so the UI can show whether tags were imported during the initial link step.

After a repository is linked, the module detail page exposes `Sync Now`. This fetches tags from GitHub again, skips already-imported versions unless replacement is explicitly requested, and records sync state on the VCS source.

Module-scoped VCS endpoints:

```text
GET  /api/vcs/sources/module/{namespace}/{name}/{provider}
POST /api/vcs/sources/{id}/sync
```

Tracked sync state includes:

- `lastSyncStatus`
- `lastPublishedVersion`
- `lastSyncAt`
- `lastSyncError`

## Shared Publish Path

All publish entry points route through `ModulePublishCoordinator`:

- Terraform CLI / API upload
- portal manual upload
- GitHub webhook tag publish
- manual GitHub sync / backfill

That shared path ensures every published version gets:

- storage write through `IModuleService`
- provenance metadata
- webhook delivery
- audit logging
- queued documentation/schema extraction

## Verification

Backend:

```bash
dotnet test TerraformRegistry.Tests --filter FullyQualifiedName~ModulePublishCoordinatorTests -v minimal
dotnet test TerraformRegistry.Tests --filter FullyQualifiedName~GitHubVcsServiceSyncTests -v minimal
dotnet test TerraformRegistry.Tests --filter FullyQualifiedName~VcsSourceTests -v minimal
dotnet test TerraformRegistry.Tests --filter FullyQualifiedName~UploadModuleTests -v minimal
```

Frontend:

```bash
pnpm --dir TerraformRegistry/web-src build
```
