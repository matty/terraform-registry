# Nuxt 4 Security Migration Design

## Objective

Restore a green dependency-security baseline by upgrading the frontend to a supported Nuxt and Nuxt UI combination, removing the obsolete pnpm lockfile, and updating the test infrastructure dependency that introduces the vulnerable SSH.NET package.

The migration must retain the current application routes, SPA deployment model, styling, and user-visible behavior. It is a security and compatibility migration, not a frontend redesign.

## Current State

The frontend currently uses Nuxt 3.21.8 and Nuxt UI 3.3.7. The npm audit gate reports critical and high-severity vulnerabilities in Nuxt, Nuxt DevTools, and transitive build dependencies. Nuxt 3.21.11 resolves the Nuxt advisories, but the first Nuxt UI release that fixes the reachable form-submission advisory is 4.8.1.

Nuxt UI releases from 4.8.1 through the current 4.11.0 declare Nuxt 4.1.0 or newer as a compatibility requirement. Combining Nuxt 3.21.11 with Nuxt UI 4.11.0 causes Nuxt to disable the UI module, after which static generation fails because `.nuxt/ui.css` is absent. A secure Nuxt UI upgrade therefore requires a Nuxt 4 migration.

The .NET test project independently uses Testcontainers.PostgreSql 4.13.0, which resolves SSH.NET 2025.1.0 and fails the blocking NuGet audit. Testcontainers.PostgreSql 4.14.0 resolves SSH.NET 2026.0.0 and removes that advisory.

## Chosen Approach

Upgrade Nuxt and Nuxt UI together on one frontend security branch, using the current compatible stable releases selected by npm within explicitly declared major-version ranges. Preserve the current client-rendered SPA configuration and make only changes required by Nuxt 4 or Nuxt UI 4 compatibility diagnostics.

Update Testcontainers.PostgreSql to 4.14.0 as a separate commit on the same security branch. This keeps the NuGet fix reviewable independently while allowing one pull request to restore all blocking dependency gates.

Supersede the stale Renovate pull requests #147 and #148 after the replacement pull request is verified. Do not merge their old lockfile rewrites.

## Frontend Package Policy

- Use npm as the sole frontend package manager.
- Keep `TerraformRegistry/web-src/package.json` and `package-lock.json` synchronized.
- Delete `TerraformRegistry/web-src/pnpm-lock.yaml`.
- Delete `TerraformRegistry/web-src/pnpm-workspace.yaml`; npm-only operation must not retain pnpm configuration.
- Run dependency resolution with the CI Node major version, Node 24. The host npm 10.9.8 resolver crashes while replacing the Nuxt peer graph; Node 24/npm 11 resolves it successfully.
- Do not add compatibility overrides that force an unsupported Nuxt UI and Nuxt pairing.
- Do not retain a vulnerable Nuxt UI version or suppress the audit gate.
- Do not add audit exceptions for vulnerabilities fixed by this migration.
- Delete `docs/security-exceptions/SUP-003-nuxt-ui.md` and remove its reachability exception from the supply-chain gate after Nuxt UI is upgraded beyond the affected range.

## Application Compatibility

The existing application remains a statically generated, client-rendered SPA. Existing routes, middleware, API calls, runtime configuration, and visual behavior remain in scope for compatibility validation.

Migration changes may include:

- Nuxt configuration keys whose meaning or location changed in Nuxt 4.
- Nuxt UI component props, slots, or imports changed between UI 3 and UI 4.
- CSS imports and generated stylesheet integration required by UI 4.
- Type fixes exposed by the Nuxt 4 toolchain.

Migration changes must not include:

- A visual redesign or component-system replacement.
- New routes, features, or state-management patterns.
- Server-side rendering enablement.
- Unrelated dependency upgrades unless npm must update them to resolve the secure graph.
- Generated frontend output committed solely because hashes changed during the migration, unless the repository's existing release process explicitly requires it.

## Failure Handling and Rollback

Treat compatibility warnings that disable a module as build failures even when Nuxt exits successfully during preparation. Static generation must demonstrate that Nuxt UI is active and that all current routes can be emitted.

Keep the Nuxt/UI migration and Testcontainers update in separate commits. If frontend compatibility cannot be completed without expanding into a redesign, retain the verified .NET commit and stop the frontend migration for a new design decision. Do not weaken security gates to make the branch mergeable.

The branch is recoverable by reverting its individual commits. The existing stale Renovate PRs remain open until the replacement branch passes its gates.

## Verification Gates

The replacement security pull request is acceptable only when all of the following pass from a clean checkout:

1. `npm ci` under Node 24.
2. `npm audit --audit-level=high` with no high or critical finding.
3. `npm run generate` with Nuxt UI enabled and no module-compatibility warning.
4. Static generation of all routes currently emitted by the Nuxt 3 build.
5. `bash scripts/verification/gates/test-supply-chain-pinning.sh`.
6. `dotnet restore` with NuGet audit enabled and NU1903/NU1904 promoted to errors.
7. `dotnet build --no-restore --configuration Release`.
8. The complete `TerraformRegistry.Tests` suite with Docker/Testcontainers and GnuPG available.
9. Slopwatch analysis for every modified C# or project file, with no new finding.
10. `git diff --check` and a review confirming that only migration-related files changed.

The GitHub pull request must also satisfy the repository's required CodeQL, dependency-review, Trivy, Docker, frontend, and .NET checks before merge.

## Expected Files

- Modify `TerraformRegistry/web-src/package.json` for compatible Nuxt 4 and Nuxt UI 4 ranges.
- Regenerate `TerraformRegistry/web-src/package-lock.json` with Node 24/npm 11.
- Delete `TerraformRegistry/web-src/pnpm-lock.yaml`.
- Delete `TerraformRegistry/web-src/pnpm-workspace.yaml`.
- Delete `docs/security-exceptions/SUP-003-nuxt-ui.md` and update `scripts/verification/gates/supply-chain-pinning.sh` plus its test to enforce the npm-only package inputs without the retired Nuxt UI exception.
- Modify `TerraformRegistry/web-src/nuxt.config.ts` only if Nuxt 4 diagnostics require it.
- Modify Vue, TypeScript, or CSS files only when a verified Nuxt UI 4 incompatibility requires it.
- Modify `TerraformRegistry.Tests/TerraformRegistry.Tests.csproj` through the dotnet CLI to update Testcontainers.PostgreSql to 4.14.0.

## Completion Criteria

The dependency baseline is restored when the secure package graph installs reproducibly, the frontend generates with Nuxt UI active, the NuGet and npm blocking audits are clean, all application tests pass, and the replacement pull request is eligible for rebase merge into `develop`.
