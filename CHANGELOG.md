# Changelog

## [0.4.1] - 2026-08-24

### Added

- Added a public credential-free WebGL export overload for a caller-owned,
  already resolved development environment.

### Fixed

- Automatic runtime routing no longer prevents consumers from exporting an
  explicitly selected manual environment for local development builds.

## [0.4.0] - 2026-08-24

### Added

- Added optional portal-driven environment resolution using the public
  Simultria Unity build-version directory.
- Added injectable Unity build metadata and structured, credential-free
  resolution results for viewer integrations and tests.

### Changed

- Development profiles can now choose explicit manual resolution or automatic
  build-version resolution without introducing a second mapping asset.
- Initialization payload creation accepts an explicitly resolved environment so
  authentication and commands can share one authoritative selection.

### Security

- Automatic resolution fails closed for missing configuration, lookup errors,
  response mismatches, unknown backend environment names, and unconfigured
  target environments. It never assumes Production.

## [0.3.1] - 2026-08-20

- Replaced the hard-coded environment dropdown with `SimultriaEnvironmentDescriptors.Standard`
  so Development, Testing, Acceptance, and Production appear in canonical order.
- Kept empty environment IDs falling back to Development.
- Kept unknown environment IDs as `Custom (...)` entries.
- Updated the environment selector to keep behavior stable for successful changes,
  including authentication refresh.

## [0.3.0] - 2026-08-20

- Added generic `ApiConnectionProfile` authentication registration overloads,
  validated through Simultria API's reusable connection adapter.
- Made development profiles prefer a project-owned generic connection while
  retaining their legacy `SimultriaApiProfile` field and APIs for serialized
  compatibility.
- Updated Edit Mode authentication, trusted binding validation, and live model
  resolution to use the same selected generic or legacy composition.
- Added explicit generic-connection support to the optional runtime provider.
- Made legacy default-provider registration conditional so a package default
  with blank hosts leaves project-owned runtime composition available.
- Updated dependencies to Deucarian API 1.4.2 and Simultria API 0.3.0.

## [0.2.0] - 2026-08-19

- Added a package-owned Edit Mode Simultria authentication target that yields
  synchronously to live viewer targets and Play Mode.
- Made the editor target available through the credential-free Simultria API
  defaults when no project/model development profile is selected.
- Added a guarded, token-opaque one-time remembered-owner migration from the
  legacy Report Viewer target to the stable package target.
- Added the Simultria implementation of Viewer Authentication's neutral runtime
  connection provider, including a session-bound API client and exact trusted
  API origin.
- Added live-only authenticated model resolution and generic `model_url` /
  `model_version` enrichment while keeping previews and exports URL-free.
- Made development-context auto-load opt-in for newly imported neutral viewers;
  existing product ProjectSettings remain authoritative.
- Added stable single-viewer authentication identity and explicit
  `SimultriaApiProfile` plus `ApiEnvironmentId` runtime registration overloads.
- Bound every live bearer session to the exact stable target, Simultria
  provider, API profile, environment, and resolved authentication composition;
  mismatches now fail before any API request.
- Made editor registration retryable and owner migration post-success, deferred
  registry-triggered refreshes outside registry mutations, and hardened runtime
  cleanup so observer failures cannot strand a token session or provider lease.
- Kept authentication sessions transient and endpoint/token ownership in
  Simultria API and Viewer Authentication respectively.

## [0.1.0] - 2026-08-19

- Added credential-free Simultria viewer development profiles.
- Added project default and local user override selection.
- Added canonical Command Routing initialization payload and handler, routed
  through the shared scene-owned local ingress.
- Added shared Simultria environment/authentication composition and sanitized
  status.
- Added Play Mode auto-load and optional secret-free local WebGL export.
