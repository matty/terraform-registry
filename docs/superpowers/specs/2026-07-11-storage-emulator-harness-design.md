# Storage Emulator Harness Design

## Goal

Provide a portable, disposable local test environment for the registry's Azure Blob and S3-compatible download contracts, and run that exact environment in GitHub Actions.

## Scope

The harness lives outside the repository at `~/.terraform-registry-storage-test` when bootstrapped locally. Repository-owned Compose and shell files are copied there by the bootstrap command, so the user directory contains all mutable state: generated environment values, Docker volumes, and logs.

The Compose topology has two services:

- Azurite Blob Storage, configured with a fixed development account/key and a health probe.
- MinIO, configured with test-only credentials, a bucket-creation helper, and a health probe.

The harness exposes four commands:

- `start` creates the directory if necessary and starts the Compose project.
- `status` reports the two service health states.
- `test` starts the services if needed, runs the registry's emulator contract test command, and preserves logs on failure.
- `clean` runs `docker compose down --volumes --remove-orphans` for this project and removes only the harness directory.

## CI behaviour

GitHub Actions checks out the repository, invokes the same bootstrap and test commands with a CI-specific directory under the runner temporary path, and uploads Compose logs if the test command fails. CI uses no cloud credentials and does not expose emulator credentials outside the job.

## Verification boundary

Azurite verifies the Azure Blob connection-string/shared-key path. MinIO verifies S3 path-style endpoint configuration, object upload/download, and presigned URL consumption. The tests cover ZIP and tar.gz module archives plus actual Terraform CLI installation for each emulator-backed provider.

This cannot verify Azure Entra managed identity or user-delegation SAS. DEP-001 remains a protected real-Azure gate, explicitly separate from this harness.

## Safety

The project name is derived solely from the harness directory. No default Docker volumes, host paths, cloud resources, or repository files are removed by `clean`. The cleanup script rejects an empty or non-canonical harness path before it removes anything.
