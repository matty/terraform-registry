# Storage Emulator Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run disposable Azurite and MinIO integration tests from a portable user-directory harness and GitHub Actions.

**Architecture:** Repository scripts materialize a versioned Compose definition into a dedicated harness directory and use one command contract locally and in CI. The test script starts the two emulators, configures the registry clients with their local endpoints, executes real download and Terraform installation checks, and tears down on request only.

**Tech Stack:** Docker Compose, Azurite, MinIO, Bash, .NET 10, Terraform CLI, GitHub Actions.

## Global Constraints

- Use Docker Compose for local and CI execution.
- Put mutable local state only below `~/.terraform-registry-storage-test` by default.
- Use test-only credentials and never require Azure or AWS credentials.
- Preserve a real Azure user-delegation-SAS gate for DEP-001.
- Every CI invocation uses the same repository scripts as local execution.

---

### Task 1: Portable Compose lifecycle

**Files:**
- Create: `scripts/remediation/storage-emulators/compose.yaml`
- Create: `scripts/remediation/storage-emulators/storage-emulators.sh`
- Test: `scripts/remediation/storage-emulators/storage-emulators.sh`

**Interfaces:**
- Consumes: `start|status|clean` and optional `--home <absolute-path>`.
- Produces: running `azurite` and `minio` services and a canonical harness directory.

- [ ] **Step 1: Write a failing lifecycle assertion**

Create a shell test that invokes `storage-emulators.sh status --home "$TMPDIR/harness"` before startup and expects a non-zero status with a clear “not initialized” message.

- [ ] **Step 2: Run the assertion and verify it fails because the command does not exist**

Run: `bash scripts/remediation/storage-emulators/test-lifecycle.sh`

Expected: non-zero exit because `storage-emulators.sh` is absent.

- [ ] **Step 3: Add the Compose definition and lifecycle command**

Define Azurite Blob on port 10000 and MinIO on port 9000 inside the Compose network. Implement `start`, `status`, and `clean`; reject any cleanup path other than the canonical harness directory or an explicit `--home` child directory.

- [ ] **Step 4: Verify lifecycle behaviour**

Run: `bash scripts/remediation/storage-emulators/test-lifecycle.sh`

Expected: it starts both health-checked containers, reports both healthy, cleans its own containers and volumes, and leaves unrelated containers untouched.

- [ ] **Step 5: Commit**

Run: `git add scripts/remediation/storage-emulators && git commit -m "test: add portable storage emulator lifecycle"`

### Task 2: Emulator-backed registry and Terraform contract gate

**Files:**
- Create: `scripts/remediation/phase-1-storage-emulator-terraform-smoke.sh`
- Modify: `scripts/remediation/storage-emulators/storage-emulators.sh`
- Test: `scripts/remediation/phase-1-storage-emulator-terraform-smoke.sh`

**Interfaces:**
- Consumes: an active harness from `storage-emulators.sh start`.
- Produces: a zero exit only after registry upload, signed emulator download, and `terraform init` complete for Azure Blob and MinIO S3.

- [ ] **Step 1: Write a failing contract assertion**

Create the smoke script so that it requires `AZURITE_CONNECTION_STRING`, `S3_SERVICE_URL`, and `S3_FORCE_PATH_STYLE`; run it before the lifecycle command exports those values.

- [ ] **Step 2: Run the assertion and verify it fails for missing configuration**

Run: `bash scripts/remediation/phase-1-storage-emulator-terraform-smoke.sh --provider azure`

Expected: non-zero exit identifying the missing emulator configuration.

- [ ] **Step 3: Implement Azure and S3 smoke paths**

Start the harness, create a test bucket in MinIO, launch the registry with either the Azurite connection string or MinIO endpoint and credentials, upload ZIP and tar.gz test modules, and run `terraform init` through the registry's HTTPS endpoint for each storage provider.

- [ ] **Step 4: Verify the full contract gate**

Run: `bash scripts/remediation/phase-1-storage-emulator-terraform-smoke.sh --provider all`

Expected: Terraform reports successful initialization for both archive formats and both emulator providers.

- [ ] **Step 5: Commit**

Run: `git add scripts/remediation && git commit -m "test: exercise module downloads through storage emulators"`

### Task 3: GitHub Actions integration

**Files:**
- Modify: `.github/workflows/ci.yaml`
- Test: `.github/workflows/ci.yaml`

**Interfaces:**
- Consumes: repository lifecycle and smoke commands.
- Produces: a secretless `storage-emulator-contract` CI job.

- [ ] **Step 1: Write a failing workflow structure check**

Add a shell assertion that requires `.github/workflows/ci.yaml` to contain a `storage-emulator-contract` job and an `if: failure()` Compose-log upload step.

- [ ] **Step 2: Run the assertion and verify it fails**

Run: `bash scripts/remediation/storage-emulators/test-ci-workflow.sh`

Expected: non-zero exit because the job is absent.

- [ ] **Step 3: Add the secretless CI job**

Configure the job to execute the lifecycle and smoke scripts with `--home "$RUNNER_TEMP/terraform-registry-storage-test"`, cache no credentials, and upload `docker compose logs` only on failure.

- [ ] **Step 4: Verify the workflow and execute its local equivalent**

Run: `bash scripts/remediation/storage-emulators/test-ci-workflow.sh && bash scripts/remediation/phase-1-storage-emulator-terraform-smoke.sh --provider all`

Expected: workflow structure assertion and full local emulator gate both return zero.

- [ ] **Step 5: Commit**

Run: `git add .github/workflows/ci.yaml scripts/remediation/storage-emulators && git commit -m "ci: run storage emulator contract gate"`
