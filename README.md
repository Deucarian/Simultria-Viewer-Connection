# Deucarian Simultria Viewer Integration

`com.deucarian.simultria-viewer-integration` is the optional adapter between
a generic Deucarian viewer and the Simultria backend ecosystem. Install it for
Simultria-backed viewers; omit it from a vendor-neutral viewer.

The package deliberately does not turn authentication into a Simultria feature:

- Deucarian API owns HTTP transport and generic endpoint/environment mechanics.
- Simultria API owns Simultria environment resolution, route catalogs, DTOs, and
  the backend-specific authentication provider.
- Viewer Authentication owns sessions, token lifecycle, secure local storage,
  and the shared authentication menu.
- Command Routing owns the canonical command envelope and local scene ingress.
- This package owns only the connection, credential-free development context,
  and development-time handoff into a running viewer.

In the Editor, the package installs one early session bridge used by both the
scene connection gate and the fallback auto-loader. Signing in before Play Mode
therefore hands the same transient session to target registration, API
authentication, and model loading; no viewer project implements this handoff.

## Install

Install the stable package branch in a Simultria-backed viewer:

```json
"com.deucarian.simultria-viewer-integration": "https://github.com/Deucarian/Simultria-Viewer-Connection.git#main"
```

Required package versions are declared in `package.json`, including API 2.0.2,
Simultria API 1.0.4, Command Routing 0.2.5, Authentication 1.0.2, and Logging 1.0.4.

## Player build configuration

Player builds use `SimultriaViewerBuildConfiguration`, not a development
profile. This asset contains only the project-owned API connection, the API
environment that hosts the public build directory, and the canonical backend
product. It deliberately has no target-environment or build-version override.

At startup the resolver sends the compiled `Application.version` and product
to `GET /api/v2/unity/builds/versions/{version}/{product}`. It verifies that
the returned version and product are exact, maps the returned environment, and
publishes one immutable session decision. Unknown versions, backend fallback
records, deprecated/unknown environments, transport failures, and unconfigured
target environments stop startup before a session or authenticated API client
is created.

`SimultriaViewerBuildConnectionGate` can hold a generic viewer bootstrap
disabled until that decision and its runtime connection provider are ready.
Development contexts and their manual/version override fields compile only in
the Unity Editor and should live in an `Editor` folder or Editor-only settings.

## Development context

Create a context from:

`Assets > Create > Deucarian > Viewer > Simultria Development Context`

The context stores only:

- an explicit Manual or Automatic-from-Unity-build-version mode;
- a manual `ApiEnvironmentId` and a project-owned generic
  `ApiConnectionSettings` reference;
- for automatic mode only, the environment whose configured host exposes the
  public build directory, the portal product ID, and an optional local/editor
  build-version override;
- project, model, and optional model-version IDs;
- placement position, rotation, and scale;
- the development-only force-show option; and
- optional non-sensitive JSON metadata.

It has no base URL, access token, credentials, login/validation route, Report
marker/media/command data, or historical `is_default_context` flag. Secret-like
metadata keys are rejected recursively.

### Environment resolution

Editor Manual mode preserves the existing behavior: an empty environment ID means
Development, while an explicitly authored custom ID remains intact and is
accepted when the connection settings configure it. Local is a first-class
built-in selection with the stable `simultria.local` ID; it is displayed as
**Local**, never as a custom environment, and may remain intentionally
unconfigured until a developer supplies a host in project-owned settings.

Editor Automatic mode uses `Application.version` unless the profile supplies an
explicit local override. Player builds always use `Application.version` from
the build configuration. Both call the public Simultria route
`GET /api/v2/unity/builds/versions/{id}/{product}` through the profile's
explicitly selected build-directory environment. The backend response is the
only build-to-environment mapping. This package does not create a duplicate
rules asset or store a build-to-environment table.

The lookup fails closed when the build version, product, directory environment,
response identity, backend environment name, or resolved target environment is
missing or invalid. There is deliberately no implicit Production host or
Production fallback. Deployment hosts remain solely in the project-owned API
connection settings.

Create the referenced connection from:

`Assets > Create > Deucarian > Connections > Simultria Connection Settings`

Then enter each environment host in that asset's inspector. The Simultria
factory supplies five blank Local, Development, Testing, Acceptance, and
Production slots plus the API v2 catalog; it supplies no
deployment URL and never copies one into the development context.

Open **Deucarian Control Center > Connections > Simultria Viewer Development** to select one project
default. That selection is stored in:

`ProjectSettings/DeucarianSimultriaViewerConnection.asset`

A developer may enable a local override. It is stored in the project's
gitignored `UserSettings` and does not change the shared default.

The compact default view shows one readiness summary, the active development
context, its environment, the Auto-load on Play option, and a shortcut to open
the context asset. Project defaults and gitignored local overrides share the
same single context field.

Detailed connection diagnostics and the explicit credential-free local WebGL
export remain available in the collapsed Advanced section. Authentication, API
connection editing, and manual command testing stay in their existing
package-owned tools instead of being duplicated here.

It never displays or serializes an access token.

## Edit Mode authentication

The package registers one editor-lifetime Viewer Authentication target backed
by a private transient session and a resolved Simultria API provider. A
selected development context overrides its API composition and environment;
automatic contexts register only after their portal result has resolved. When
none is selected, no authentication target is registered. Project and model IDs are never required
merely to sign in or refresh authentication. This makes the shared
**Deucarian Control Center > Connections > Authentication** workflow usable outside Play Mode
without any product-local login or validation endpoint assets.

The target uses the stable ID
`SimultriaViewerConnectionAuthentication.DefaultTargetId`. Viewer
Authentication remains the sole owner of optional remembered-token storage in
ignored `UserSettings`; this package never reads a token into the development
context. A remembered token owned by the historical `report-viewer` target is
rebound once to the stable package target through Viewer Authentication's
token-opaque Editor facade. Other target owners are never claimed. The editor target is
synchronously removed before Play Mode and as soon as any real viewer target
is registered. It is restored only after the runtime target is gone and the
editor has returned to Edit Mode, so the menu never receives an intentional
editor/runtime target pair and the package never reads or copies the token.

The transient handoff is bound to the selected settings asset, environment,
and a SHA-256 fingerprint of its resolved client hosts and endpoint catalog.
Changing a host or route on the same asset therefore creates a different
binding and cannot restore the previous backend's bearer. The binding contains
no raw host, route, header, or token value.
Authentication persistence and runtime-provider creation use that same digest
and recheck it around registration callbacks, so remembered or transient
bearers cannot cross a host, route, secondary-client, or policy change.

## Canonical initialization command

The package emits `initialize_viewer` with a typed payload:

```json
{
  "protocol_version": 1,
  "command_id": "simultria-development-42",
  "command": "initialize_viewer",
  "payload": {
    "revision": 42,
    "environment_id": "simultria.development",
    "project_id": 832,
    "model_id": 41,
    "model_version_id": 7,
    "placement": {
      "position": { "x": 0, "y": 0, "z": 0 },
      "rotation_euler": { "x": 0, "y": 0, "z": 0 },
      "scale": { "x": 1, "y": 1, "z": 1 }
    },
    "force_show_loaded_model_objects": true
  },
  "metadata": {
    "source": "simultria-development-profile",
    "transport": "editor-local",
    "remote_endpoint": "direct"
  }
}
```

The stored preview/export remains URL-free. Immediately before a live local
dispatch, `SimultriaViewerModelInitializationResolver` resolves the selected
model version with the sole live session's authenticated API client and adds
the generic `model_url` and `model_version` fields. Player products call that
same resolver from their typed initialization handler, so Editor and compiled
builds share one project/model/version policy. Resolution failure fails closed.
Host-provided model URLs are ignored; bearer values are never copied into the
command or URL query, and bearer-like URL query fields are rejected.

Automatic mode resolves the effective environment before command creation.
That exact environment ID is written to `initialize_viewer`, used to validate
the authentication binding, and used for model resolution. The synchronous
payload and registration overloads intentionally reject an unresolved
automatic profile; consumers pass the explicit result from
`SimultriaViewerEnvironmentResolver.ResolveAsync` instead.

Before that request, the package verifies that the sole target is the stable
`simultria-viewer` target and is backed by the same Simultria provider, API
profile, environment, and resolved authentication composition selected by the
development context. An arbitrary viewer target or environment mismatch fails
closed instead of forwarding its bearer.

Play Mode auto-load waits for exactly one live Viewer Authentication target with
an access token and exactly one initialized `CommandRoutePortBehaviour`. It then
routes the JSON through `ICommandRoutePort.RouteMessageAsync` using transport
`editor-local` and the direct-page response endpoint `direct`. It never invokes a
Report, Activity, or template bootstrap directly and stores no active-viewer
dropdown or target ID.

For a selected manual development context, the editor also registers that
context's generic runtime connection before the viewer scene starts. Generic
viewer templates therefore lease the same stable authentication target, API
client, environment, and trusted model origin that the auto-loader uses to
resolve and dispatch `initialize_viewer`.

Auto-load is opt-in for a newly imported package. Neutral templates and Activity
Viewer therefore do not warn on every Play when no development context is
selected. Report Viewer can keep its explicit committed project setting enabled.

## Consumer composition

### Shared product connection and initialization

Product viewer features expose their project-owned connection explicitly by
implementing `ISimultriaViewerConnectionSettingsSource`. The sole member,
`SimultriaViewerConnectionSettings`, lets Editor build validation compare the
feature with `SimultriaViewerBuildConfiguration` without field-name reflection
or knowledge of a Report/Activity component type.

Use `SimultriaViewerModelInitializationCoordinator` instead of independently
creating an `ApiComposition`, API client, environment choice, and
`SimultriaViewerModelInitializationResolver` in each product. Its `Prepare`
overloads accept either project-owned `ApiConnectionSettings` plus an
`ApiClientConfig` and `IApiAuthProvider`, an already-created composition and
client, or the active `SimultriaViewerRuntimeConnectionContext`.

The returned `SimultriaViewerModelInitializationPlan` contains the exact
effective `ApiEnvironmentId`, resolved primary client, composition, API client,
and canonical resolver. Call `ResolveAsync` on a successful plan to resolve the
model. When `SimultriaViewerRuntimeEnvironment` is active, that immutable value
wins and any explicit payload mismatch is rejected. Without it, the payload
must contain a valid environment ID. No overload chooses Development,
Production, or any other fallback. A configured Local payload therefore remains
`simultria.local`; an unconfigured Local slot fails closed.

For a complete product initialization, call the coordinator's `ExecuteAsync`.
It owns payload and stale-revision validation, active-lease selection, the
explicit Editor-test fallback policy, model resolution, application-failure
propagation, and canonical success payload assembly. The product supplies only
an application-neutral async delegate that maps the resolved model into its
viewer application. This keeps Activity and Report orchestration identical
without adding a Template Viewer dependency to this package.

The optional runtime provider also publishes a
`SimultriaViewerRuntimeConnectionContext` for exactly the lifetime of its
`ViewerRuntimeConnection`. This is the client-bearing companion to the
sanitized `SimultriaViewerRuntimeEnvironment`: it exposes the lease's exact
composition, primary client, API client, and environment, but no authentication
session or bearer value. Consumers may reuse those objects and must never
dispose or replace them. `TryGetCurrent` returns false outside the active lease
and never creates a client. Plans recheck the owning context after awaited model
resolution and again immediately before the product delegate, failing closed if
the exact lease was released in either interval.

A viewer composition root injects its existing Command Routing runtime into the
shared scene port:

```csharp
CommandRoutePortBehaviour routePort =
    gameObject.AddComponent<CommandRoutePortBehaviour>();
routePort.Initialize(commandRoutingRuntime);
```

Register the package handler when the product wants the typed Simultria payload:

```csharp
new SimultriaViewerInitializationCommandHandler<MyApplication>(
    async (application, payload, metadata, cancellationToken) =>
        await application.InitializeFromSimultriaAsync(
            payload,
            metadata,
            cancellationToken));
```

The delegate maps only into the consumer's existing application command path.
Simultria API resolves environment/project/model data; product-specific Report
markers, Activity visualization, media, and commands remain in their products.

For authentication composition, call
`SimultriaViewerConnectionAuthentication.TryRegister`. It resolves the profile's
environment through Simultria API, creates one
`SimultriaAuthenticationProvider`, and registers that same provider for
generic acquisition and server validation in Viewer Authentication.

The package default contains no deployment host, so merely installing this
package does not claim the fail-closed runtime provider registry. A project or
coupling package that uses the generic runtime can explicitly register its
project-owned connection settings:

```csharp
IDisposable runtimeProviderRegistration =
    ViewerRuntimeConnectionProviderRegistry.Register(
        new SimultriaViewerRuntimeConnectionProvider(
            apiConnectionSettings,
            environmentId));
```

The provider creates no live authentication target until a generic viewer
resolves it. The resulting lease owns the stable `simultria-viewer` session,
an API client backed by that exact session, the profile-resolved API base URL,
and its exact authenticated origin. Dispose the registration with the owning
composition. There is no package-level connection fallback: a project must
explicitly own and select its connection settings before registration can
succeed.

Single-viewer runtimes should use the overload without target strings so Edit
Mode and Play Mode keep the same stable identity:

```csharp
SimultriaViewerConnectionAuthentication.TryRegister(
    apiConnectionSettings,
    environmentId,
    authenticationSession,
    out IDisposable authenticationRegistration,
    out ApiEnvironmentStatus environmentStatus,
    out string error);
```

For automatic mode, resolve first and pass the same effective environment to
each consumer:

```csharp
SimultriaViewerEnvironmentResolution resolution =
    await SimultriaViewerEnvironmentResolver.CreateDefault()
        .ResolveAsync(developmentContext, cancellationToken);

if (!resolution.Succeeded)
{
    // Show resolution.Message and stop; never choose a fallback environment.
    return;
}

SimultriaViewerConnectionAuthentication.TryRegister(
    developmentContext,
    resolution.EnvironmentId,
    authenticationSession,
    out IDisposable authenticationRegistration,
    out ApiEnvironmentStatus environmentStatus,
    out string error);
```

This explicit connection-profile/environment overload does not require a
development project/model profile. Existing products migrating from another target ID
should switch to the stable package identity; the package includes the guarded
one-time `report-viewer` owner migration. Multi-viewer products may use the
explicit target-ID overload but do not participate in that migration.

The package deliberately does not reflect into a generic template bootstrap or
create a second application session. A generic template or a dedicated
template-to-Simultria coupling package should call the registration overload
for the template's authoritative session and register
`SimultriaViewerInitializationCommandHandler<TApplication>` in that template's
existing Command Routing composition. Activity consumers then need no local
runtime code; project/model payload mapping remains in the shared composition,
not in the Activity project. This avoids a Template dependency or package
cycle here.

The resolved model URL is useful only together with that runtime connection.
The download route requires bearer authentication, while a generic template
must attach bearer credentials only to the explicitly trusted exact origin.
Provider creation failure or provider ambiguity therefore fails closed rather
than falling back to an unrelated local API client.

## Local WebGL development context

The editor window can explicitly export:

`Assets/StreamingAssets/simultria-viewer-context.json`

The file contains the same canonical command envelope, but no token, credential,
base URL, or authentication route. Explicit export and clear remain available
for local harnesses.

Build Pipeline 0.6.0 automatically discovers
`SimultriaViewerBuildLifecycleContributor` when the selected scene contains a
`SimultriaViewerBuildConnectionGate`. Validation requires one gate and build
configuration, one or more feature connection sources bound to the same
settings, an explicit resolved build-directory environment, and every remote
promotable environment. Development additionally requires the selected Manual
context and its explicit resolved environment; Automatic resolution fails
closed during synchronous build preparation.

Preparation snapshots and removes the current and legacy context files and
their metadata. Development exports only the selected credential-free current
context; Production exports neither. The exact prior file state is restored
after success, build failure, validation failure, or partial preparation.
Generated Development artifacts must contain one safe current context at the
exact WebGL-loadable `StreamingAssets/simultria-viewer-context.json` path.
Production artifacts containing either context filename anywhere are rejected,
and their contained build output is removed through Build Pipeline's safe output
policy. The contributor claims a request only after successful inspection
detects a Simultria connection gate or canonical connection-settings source,
so unrelated and zero-scene builds remain owned by their normal contributors.
Once either viewer marker is detected, a missing gate plus selection,
partial-inspection, and configuration issues enter validation and fail closed.

## Report Viewer migration

Report Viewer can delete its local default-selection, auto-load, context window,
and secret-bearing WebGL export logic after it:

1. migrates each old `ViewerDevProjectContextAsset` to a
   `SimultriaViewerDevelopmentContext` and selects the shared project default;
2. maps the package initialization payload into its current project context;
3. registers the package's initialization handler in its existing Command
   Routing runtime;
4. injects that runtime into one `CommandRoutePortBehaviour`; and
5. replaces its project-local endpoint assets/provider factory with the
   Simultria API environment/authentication composition.

Report-only marker, attachment, media, and command tools do not move here.

## Validation

Run the Package Registry validator, package EditMode tests, and
`git diff --check`. This package does not require a player build for ordinary
contract validation.
