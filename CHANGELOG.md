# Changelog

## [1.0.2] - 2026-08-31

- Registered the package workflow and a bounded, sanitized local-state card with Deucarian Control Center.
- Removed normal `Tools/Deucarian` menu exposure while preserving the standalone open API.
- Updated the shared Editor dependency to 1.2.0.
- Aligned API 2.0.1, Command Routing 0.2.5, Logging 1.0.4, Simultria API 1.0.2, and Authentication 1.0.1.

## [1.0.1] - 2026-08-27

### Changed

- Reduced Simultria Viewer Development to one readiness summary, the active
  development context, environment selection, auto-load, and an asset shortcut.
- Moved detailed connection diagnostics and the optional credential-free local
  WebGL export behind a collapsed Advanced section.
- Removed duplicate authentication, API connection, command dispatch, and
  command preview controls from the development-context window.

## [1.0.0] - 2026-08-26

### Breaking

- Renamed the package to `com.deucarian.simultria-viewer-integration` and the
  credential-free editor asset to `SimultriaViewerDevelopmentContext`.
- Removed every `SimultriaApiProfile` compatibility path and implicit package
  connection fallback. Consumers now supply `ApiConnectionSettings` explicitly.
- Updated the API and Simultria API dependencies to their new major versions.
- Updated Command Routing and Logging to the coordinated editor UX releases.
- Renamed the editor workflow type to
  `SimultriaViewerDevelopmentWindow` and removed remaining Connection UI copy.
- Derived the footer from installed package metadata and moved build
  configuration creation to the Build capability menu.

## [0.5.1] - 2026-08-25

### Added

- Added one runtime model-initialization resolver that turns canonical
  `project_id`, `model_id`, and optional `model_version_id` values into the
  authenticated Simultria model source used by every viewer product.

### Security

- Always resolve the model source through the assigned Simultria environment;
  host-provided `model_url` values are ignored and credential-bearing resolved
  URLs fail closed.

## [0.5.0] - 2026-08-25

### Added

- Resolve player-build environments from the public Simultria Unity build
  directory using the compiled `Application.version` and canonical product.
- Publish one immutable, sanitized runtime environment decision and gate
  generic viewer startup until its matching connection provider is ready.
- Keep project/user development profiles as Editor-only selection inputs while
  player builds use a separate configuration with no target-environment or
  build-version override.

### Changed

- Reject missing, unknown, mismatched, or unavailable build-directory results
  without falling back to another version or environment.

## [0.4.5] - 2026-08-25

### Fixed

- Route Editor-local viewer initialization through the direct WebGL response
  endpoint so loading and lifecycle events reach the running viewer transport.
- Define zero as active-version selection, preserve positive exact version
  pins, and reject negative version values in development profiles.
- Require Simultria API 0.4.1 so standalone consumers use active model-version
  resolution for development-profile initialization.

## [0.4.4] - 2026-08-25

### Fixed

- Retain the authenticated Simultria session for the lifetime of the current
  Unity Editor process and carry it across Edit Mode and Play Mode without
  writing the access token to project or user settings.
- Bind retained Editor authentication to the exact API profile and environment
  so a token is never reused after switching backends.
- Include the explicitly configured `simultria.model-content` client origin in
  runtime connections so authenticated cross-origin model downloads can load
  without allowing arbitrary hosts.

## [0.4.3] - 2026-08-25

### Fixed

- Keep Play Mode development auto-load active while an interactive viewer sign-in
  is in progress, then continue immediately after authentication succeeds.
- Report missing or invalid automatic Play Mode configuration and initialization
  failures as errors instead of leaving consumers with warning-only blank viewers.

## [0.4.2] - 2026-08-25

### Fixed

- Register the selected manual development profile as the generic viewer's
  runtime connection before scene startup, so Play Mode auto-load uses the
  same authenticated API session for model resolution and download.

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

- Added generic `ApiConnectionSettings` authentication registration overloads,
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
