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

The repository is intentionally local-only during its first implementation
pass. Add it as a local package while validating consumers:

```json
"com.deucarian.simultria-viewer-connection": "file:C:/Repositories/Simultria-Viewer-Connection"
```

Required package versions are declared in `package.json`, including API 1.2.0,
Simultria API 0.1.0, Command Routing 0.1.2, and Viewer Authentication 0.4.0.

## Development profile

Create a profile from:

`Assets > Create > Deucarian > Viewer > Simultria Development Profile`

The profile stores only:

- optional serializable `ApiEnvironmentId` and/or `SimultriaApiProfile`
  reference (the package default is used when the reference is empty);
- project, model, and optional model-version IDs;
- placement position, rotation, and scale;
- the development-only force-show option; and
- optional non-sensitive JSON metadata.

It has no base URL, access token, credentials, login/validation route, Report
marker/media/command data, or historical `is_default_context` flag. Secret-like
metadata keys are rejected recursively.

Open `Tools > Deucarian > Viewer > Simultria Connection` to select one project
default. That selection is stored in:

`ProjectSettings/DeucarianSimultriaViewerConnection.asset`

A developer may enable a local override. It is stored in the project's
gitignored `UserSettings` and does not change the shared default.

The window uses the shared Deucarian Editor chrome and shows:

- the selected profile source;
- sanitized resolved Simultria environment status;
- sanitized Viewer Authentication status; and
- whether the scene's canonical command ingress is ready.

It never displays or serializes an access token.

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
    "remote_endpoint": "development-profile"
  }
}
```

Play Mode auto-load waits for exactly one live Viewer Authentication target with
an access token and exactly one initialized `CommandRoutePortBehaviour`. It then
routes the JSON through `ICommandRoutePort.RouteMessageAsync` using transport
`editor-local` and remote endpoint `development-profile`. It never invokes a
Report, Activity, or template bootstrap directly and stores no active-viewer
dropdown or target ID.

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
