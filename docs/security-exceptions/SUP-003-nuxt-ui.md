# SUP-003 — Nuxt UI UAuthForm/UForm advisory exception

- Advisory: [GHSA-gj2h-2fpw-fhv9](https://github.com/advisories/GHSA-gj2h-2fpw-fhv9)
- Affected dependency: `@nuxt/ui` 3.3.7, which is within the advisory range
- Owner: `matty` (repository maintainer)
- Expiry: 2026-10-14

## Reachability assessment

The advisory applies to `UAuthForm` and `UForm` server-rendered markup when a
user submits the component before client-side hydration. The registry frontend
does not render either component. The automated supply-chain gate searches the
frontend source (excluding generated dependency and lockfile content) for both
component names and fails if either is introduced.

## Compensating control

The workflow runs `npm audit --audit-level=high`, so a High/Critical escalation
remains release-blocking. This Moderate finding is isolated to components that
are absent from the shipped frontend; the reachability gate prevents an affected
component from being added without first upgrading `@nuxt/ui` or replacing this
exception through normal review. The owner must upgrade or re-assess before the
expiry date; the automated gate fails on that date until the exception is
replaced or renewed through review.
