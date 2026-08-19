# Deucarian Simultria Viewer Connection Agent Notes

Package ID: `com.deucarian.simultria-viewer-connection`

Follow the canonical Package Registry architecture and dependency rules.

## Ownership

This optional connection package owns the adapter between Simultria API,
Viewer Authentication, Command Routing, and generic viewer initialization. It
also owns credential-free development profiles, their project/local selection,
and an explicit local development-context export.

It must not own HTTP transport, session storage, authentication UI, endpoint
URLs, environment URL templates, Report/Activity marker-media-command behavior,
generic viewer UI, or a viewer application bootstrap.

## Invariants

- Development profiles never serialize tokens, credentials, API base URLs, or
  authentication routes.
- Metadata containing secret-like keys is rejected before command creation or
  export.
- Auto-load dispatches the canonical `initialize_viewer` JSON command through
  the sole initialized scene-owned `CommandRoutePortBehaviour`. It never finds
  or invokes a product bootstrap and never stores an active-viewer selector.
- The project default lives in `ProjectSettings`; a local override lives only in
  gitignored `UserSettings`.
- WebGL development export is explicit, optional, and credential-free.
- Runtime connection leases must use the same session for target registration,
  API authentication, and trusted-origin model loading.
- Model URLs are resolved only for live dispatch and never include bearer
  values; preview/export commands remain URL-free.
- Do not add direct `UnityEngine.Debug` calls.

## Validation

Run the shared Package Registry validator, EditMode tests, and `git diff --check`.
Do not run player builds unless explicitly requested.
