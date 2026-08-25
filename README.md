# Deucarian Simultria Viewer Connection

`com.deucarian.simultria-viewer-connection` is the optional connection between
a generic Deucarian viewer and the Simultria backend ecosystem. Install it for
Simultria-backed viewers; omit it from a vendor-neutral viewer.

The package deliberately does not turn authentication into a Simultria feature:

- Deucarian API owns HTTP transport and generic endpoint/environment mechanics.
- Simultria API owns Simultria environment resolution, route catalogs, DTOs, and
  the backend-specific authentication provider.
- Viewer Authentication owns sessions, token lifecycle, secure local storage,
  and the shared authentication menu.
- Command Routing owns the canonical command envelope and local scene ingress.
- This package owns only the connection, credential-free development profile,
  and development-time handoff into a running viewer.

## Install

Install the stable package branch in a Simultria-backed viewer:

```json
"com.deucarian.simultria-viewer-connection": "https://github.com/Deucarian/Simultria-Viewer-Connection.git#main"
```

Required package versions are declared in `package.json`, including API 1.4.2,
Simultria API 0.4.1, Command Routing 0.1.2, and Viewer Authentication 0.5.1.

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
Development profiles and their manual/version override fields compile only in
the Unity Editor and should live in an `Editor` folder or Editor-only settings.

## Development profile

Create a profile from:

`Assets > Create > Deucarian > Viewer > Simultria Development Profile`

The profile stores only:

- an explicit Manual or Automatic-from-Unity-build-version mode;
- a manual `ApiEnvironmentId` and a project-owned generic
  `ApiConnectionProfile` reference;
- for automatic mode only, the environment whose configured host exposes the
  public build directory, the portal product ID, and an optional local/editor
  build-version override;
- a legacy `SimultriaApiProfile` reference retained for existing serialized
  assets (the package default is used only when both references are empty);
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
accepted when the connection profile configures it.

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
connection profile.

Create the referenced connection from:

`Assets > Create > Deucarian > Simultria > API Profile`

Then enter each environment host in that asset's inspector. The Simultria
factory supplies the four blank slots and API v2 catalog; it supplies no
deployment URL and never copies one into the development profile.

Open `Tools > Deucarian > Viewer > Simultria Connection` to select one project
default. That selection is stored in:

`ProjectSettings/DeucarianSimultriaViewerConnection.asset`

A developer may enable a local override. It is stored in the project's
gitignored `UserSettings` and does not change the shared default.

The window uses the shared Deucarian Editor chrome and shows:

- the selected profile source;
- the automatic build input and resolution source when enabled;
- sanitized resolved Simultria environment status;
- sanitized Viewer Authentication status; and
- whether the scene's canonical command ingress is ready.

It never displays or serializes an access token.

## Edit Mode authentication

The package registers one editor-lifetime Viewer Authentication target backed
by a private transient session and a resolved Simultria API provider. A
selected development profile overrides its API composition and environment;
automatic profiles register only after their portal result has resolved. When
none is selected, the credential-free Simultria API package profile and
`simultria.development` are used. Project and model IDs are never required
merely to sign in or refresh authentication. This makes the shared
`Tools > Deucarian > Viewer > Authentication` window usable outside Play Mode
without any product-local login or validation endpoint assets.

The target uses the stable ID
`SimultriaViewerConnectionAuthentication.DefaultTargetId`. Viewer
Authentication remains the sole owner of optional remembered-token storage in
ignored `UserSettings`; this package never reads a token into the development
profile. A remembered token owned by the legacy `report-viewer` target is
rebound once to the stable package target through Viewer Authentication's
token-opaque Editor facade. Other target owners are never claimed. The editor target is
synchronously removed before Play Mode and as soon as any real viewer target
is registered. It is restored only after the runtime target is gone and the
editor has returned to Edit Mode, so the menu never receives an intentional
editor/runtime target pair and the package never reads or copies the token.

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
dispatch, the package resolves the selected model version with the sole live
session's authenticated API client and adds the generic `model_url` and
`model_version` fields. Resolution failure fails closed. Bearer values are
never copied into the command or URL query, and bearer-like URL query fields
are rejected.

Automatic mode resolves the effective environment before command creation.
That exact environment ID is written to `initialize_viewer`, used to validate
the authentication binding, and used for model resolution. The synchronous
payload and registration overloads intentionally reject an unresolved
automatic profile; consumers pass the explicit result from
`SimultriaViewerEnvironmentResolver.ResolveAsync` instead.

Before that request, the package verifies that the sole target is the stable
`simultria-viewer` target and is backed by the same Simultria provider, API
profile, environment, and resolved authentication composition selected by the
development profile. An arbitrary viewer target or environment mismatch fails
closed instead of forwarding its bearer.

Play Mode auto-load waits for exactly one live Viewer Authentication target with
an access token and exactly one initialized `CommandRoutePortBehaviour`. It then
routes the JSON through `ICommandRoutePort.RouteMessageAsync` using transport
`editor-local` and the direct-page response endpoint `direct`. It never invokes a
Report, Activity, or template bootstrap directly and stores no active-viewer
dropdown or target ID.

For a selected manual development profile, the editor also registers that
profile's generic runtime connection before the viewer scene starts. Generic
viewer templates therefore lease the same stable authentication target, API
client, environment, and trusted model origin that the auto-loader uses to
resolve and dispatch `initialize_viewer`.

Auto-load is opt-in for a newly imported package. Neutral templates and Activity
Viewer therefore do not warn on every Play when no development profile is
selected. Report Viewer can keep its explicit committed project setting enabled.

## Consumer composition

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
`SimultriaViewerAuthenticationProvider`, and registers that same provider for
generic acquisition and server validation in Viewer Authentication.

The package default contains no deployment host, so merely installing this
package does not claim the fail-closed runtime provider registry. A project or
coupling package that uses the generic runtime can explicitly register its
project-owned connection profile:

```csharp
IDisposable runtimeProviderRegistration =
    ViewerRuntimeConnectionProviderRegistry.Register(
        new SimultriaViewerRuntimeConnectionProvider(
            apiConnectionProfile,
            environmentId));
```

The provider creates no live authentication target until a generic viewer
resolves it. The resulting lease owns the stable `simultria-viewer` session,
an API client backed by that exact session, the profile-resolved API base URL,
and its exact authenticated origin. Dispose the registration with the owning
composition. Existing installations with a configured legacy package default
retain automatic registration; blank defaults deliberately leave consumer
fallback available.

The legacy `SimultriaApiProfile` constructor and registration overloads remain
available. Because the generic and legacy profile types are unrelated
overloads, pass a typed profile variable (or an explicit cast) rather than a
bare `null` literal when testing missing configuration.

Single-viewer runtimes should use the overload without target strings so Edit
Mode and Play Mode keep the same stable identity:

```csharp
SimultriaViewerConnectionAuthentication.TryRegister(
    apiConnectionProfile,
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
        .ResolveAsync(developmentProfile, cancellationToken);

if (!resolution.Succeeded)
{
    // Show resolution.Message and stop; never choose a fallback environment.
    return;
}

SimultriaViewerConnectionAuthentication.TryRegister(
    developmentProfile,
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
base URL, or authentication route. There is no automatic build hook: export and
clear are deliberate local actions. A consumer may ignore this generated path,
but accidental versioning does not expose credentials.

## Report Viewer migration

Report Viewer can delete its local default-selection, auto-load, context window,
and secret-bearing WebGL export logic after it:

1. migrates each old `ViewerDevProjectContextAsset` to a
   `SimultriaViewerDevelopmentProfile` and selects the shared project default;
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
