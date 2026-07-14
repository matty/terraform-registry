# Remediation Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace completed-remediation automation with durable verification tooling and remove campaign-only CI, scripts, and documentation.

**Architecture:** Durable safeguards move unchanged from `scripts/remediation/` to `scripts/verification/`; workflows and user-facing runbooks refer only to the new location. CI jobs attached exclusively to historical audit branches, together with their scripts and bootstrap documentation, are removed.

**Tech Stack:** Bash, GitHub Actions YAML, Markdown, Ruby YAML parser.

## Global Constraints

- Preserve supply-chain, release-runbook, storage-emulator, Terraform backend, and final-candidate behavior.
- Remove active `scripts/remediation/` callers without changing application code.
- Use static verification because .NET SDK 10.0.301 is unavailable in this environment.

---

### Task 1: Relocate durable verification tooling

**Files:**

- Move retained gate scripts to `scripts/verification/gates/`.
- Move storage emulators, Terraform smoke/matrix tooling, and the backup/restore evidence helper to `scripts/verification/`.
- Modify relocated scripts to refer only to `scripts/verification/` paths.

**Interfaces:** Produces the same executable command-line entry points under `scripts/verification/`.

- [ ] **Step 1: Move the retained files with `git mv`.**

Run `git mv scripts/remediation/storage-emulators scripts/verification/storage-emulators`; move supply-chain, release-runbook, and final-candidate scripts from `scripts/remediation/gates/` to `scripts/verification/gates/`; then move `terraform-backend-matrix.sh`, `test-terraform-backend-matrix.sh`, `terraform-provider-smoke.Dockerfile`, `phase-1-storage-emulator-terraform-smoke.sh`, `phase-1-local-terraform-smoke.sh`, and `phase-0-backup-restore-evidence.sh` into `scripts/verification/`.

- [ ] **Step 2: Replace internal `scripts/remediation/` references in the moved scripts with `scripts/verification/`.**

- [ ] **Step 3: Run `find scripts/verification -type f -name '*.sh' -print0 | xargs -0 -n1 bash -n`; expect exit status 0.**

- [ ] **Step 4: Commit with `git commit -m "chore: relocate durable verification scripts"`.**

### Task 2: Retire campaign-only CI and scripts

**Files:**

- Delete the remaining `scripts/remediation/` directory.
- Delete `.github/workflows/phase-1-real-azure-gate.yaml`.
- Modify `.github/workflows/ci.yaml` and `.github/workflows/security.yaml`.

**Interfaces:** CI retains ordinary verification and release-candidate certification without historical audit branch selectors.

- [ ] **Step 1: Delete audit-only jobs from `.github/workflows/ci.yaml`: `migration-safety-gate`, `phase-1-deployment-gate`, `bounded-ingestion-security-gate`, `mirror-containment-gate`, `publication-fault-gate`, `fault-load-certification`, and `operability-certification`.**

- [ ] **Step 2: Remove `remediation/phase/**` trigger branches from CI and security workflows, then replace retained workflow paths with `scripts/verification/` equivalents.**

- [ ] **Step 3: Delete the real-Azure phase-1 workflow and the no-longer-referenced scripts. Update final-candidate certification to run only retained verification contracts and checks; remove audit-only operability and fault/load calls.**

- [ ] **Step 4: Run `ruby -e 'require "yaml"; ARGV.each { |path| YAML.load_file(path) }' .github/workflows/*.yaml`; expect exit status 0.**

- [ ] **Step 5: Commit with `git commit -m "ci: retire completed remediation gates"`.**

### Task 3: Update durable documentation

**Files:**

- Delete `docs/ci-security-bootstrap.md`.
- Modify `docs/build-inputs.md`, `docs/release-operations-runbook.md`, and `docs/phase-0-migration-recovery-runbook.md`.

**Interfaces:** The runbooks document only retained `scripts/verification/` commands.

- [ ] **Step 1: Delete `docs/ci-security-bootstrap.md` with `git rm`.**

- [ ] **Step 2: Change retained runbook and build-input script paths to `scripts/verification/`; remove wording that describes the concluded remediation campaign.**

- [ ] **Step 3: Run `rg -n 'scripts/remediation/|remediation/phase' README.md docs .github scripts -g '!docs/superpowers/**'`; expect no results.**

- [ ] **Step 4: Commit with `git commit -m "docs: remove completed remediation guidance"`.**

### Task 4: Run focused verification

**Files:** Verify `.github/workflows/*.yaml` and `scripts/verification/**/*.sh`.

- [ ] **Step 1: Run `find scripts/verification -type f -name '*.sh' -print0 | xargs -0 -n1 bash -n`; expect exit status 0.**

- [ ] **Step 2: Run the retained static contracts: `test-supply-chain-pinning.sh`, `test-release-runbooks-gate.sh`, `test-terraform-backend-matrix.sh`, and `test-final-candidate-certification.sh`; expect exit status 0.**

- [ ] **Step 3: Re-run the YAML parser and stale-path search from Tasks 2 and 3; expect exit status 0 and no stale paths.**

- [ ] **Step 4: Run `dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --configuration Release --no-restore`; report the expected SDK 10.0.301 limitation without changing `global.json`.**
