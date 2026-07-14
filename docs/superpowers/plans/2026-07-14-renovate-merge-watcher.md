# Renovate Merge Watcher Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge at most one policy-complete routine Renovate PR without bypassing its stability, CI, or review safeguards.

**Architecture:** A trusted scheduled/manual Actions workflow invokes a checked-in Bash decision script against GitHub API data. The script is tested with a fake GitHub CLI and never checks out or executes PR code.

**Tech Stack:** GitHub Actions, GitHub CLI, Bash, jq.

## Global Constraints

- Branch and commit names must not contain `phase`.
- Never auto-merge human, security-alert, or dashboard-approved major PRs.
- Require Renovate's `automerge-candidate` label, configured only for digest,
  pin, patch, and minor updates.
- Require all eight `develop` branch-protection checks and successful Renovate stability before merging.
- Re-query PR state, current head SHA, comments, reviews, threads, and merge state immediately before a rebase merge.
- Process one PR per run and do not merge when prior `develop` CI/security is not successful.
- Workflow must not use `pull_request_target`, check out PR code, or receive write permissions beyond `pull-requests: write`.

---

### Task 1: Eligibility decision script and tests

**Files:**
- Create: `scripts/automation/merge-renovate-pr.sh`
- Create: `scripts/automation/test-merge-renovate-pr.sh`

- [ ] **Step 1: Write failing fixtures** for pending `renovate/stability-days`, comments, unresolved threads, failed required checks, changed head SHA, unclean merge state, and an eligible routine Renovate PR.
- [ ] **Step 2: Run** `bash scripts/automation/test-merge-renovate-pr.sh` and confirm it fails because the decision script is absent.
- [ ] **Step 3: Implement** a Bash script that reads only GitHub API responses through `gh`, emits an eligibility summary, and calls the rebase merge endpoint once only for the eligible fixture.
- [ ] **Step 4: Re-run** `bash scripts/automation/test-merge-renovate-pr.sh` and confirm all fixtures pass.
- [ ] **Step 5: Commit** the script and tests with a descriptive non-phase message.

### Task 2: Trusted watcher workflow and contract test

**Files:**
- Create: `.github/workflows/renovate-merge-watcher.yaml`
- Create: `scripts/automation/test-renovate-merge-watcher-workflow.sh`

- [ ] **Step 1: Write a failing workflow contract test** that requires scheduled/manual trusted triggers, serial concurrency, `contents: read` plus `pull-requests: write`, no `pull_request_target`, and the decision-script invocation.
- [ ] **Step 2: Run** `bash scripts/automation/test-renovate-merge-watcher-workflow.sh` and confirm it fails because the workflow is absent.
- [ ] **Step 3: Implement** the workflow from `develop` only; it checks out the trusted repository revision and runs the decision script without accessing PR code.
- [ ] **Step 4: Re-run** the workflow contract test and script test, then validate YAML and whitespace.
- [ ] **Step 5: Commit** the workflow and contract test with a descriptive non-phase message.

### Task 3: End-to-end verification and PR

**Files:**
- Modify: `docs/ci-security-bootstrap.md`

- [ ] **Step 1: Document** watcher scope, exclusions, and the rule that pending stability is never mergeable.
- [ ] **Step 2: Run** the two script test suites, `npx renovate-config-validator .github/renovate.json`, `git diff --check`, and the repository remediation workflow checks that cover CI policy.
- [ ] **Step 3: Commit** documentation with a descriptive non-phase message.
- [ ] **Step 4: Push and open a PR**, inspect every check, review, comment, and unresolved thread, and merge only when GitHub reports `CLEAN`.
