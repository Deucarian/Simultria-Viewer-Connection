using System;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.CommandRouting;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Models;
using Deucarian.Simultria.API.Services;
using Deucarian.ViewerAuthentication;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection.Editor
{
    internal static class SimultriaViewerDevelopmentCommandService
    {
        internal const string AuthenticationRequiredMessage =
            "Waiting for authentication. Open Tools/Deucarian/Viewer/Authentication.";

        public static bool TryCreateCommand(
            SimultriaViewerDevelopmentProfile profile,
            out CommandEnvelope command,
            out string error)
        {
            command = null;
            if (profile == null)
            {
                error = "A Simultria viewer development profile is required.";
                return false;
            }

            if (!profile.TryResolveEnvironment(
                    out ApiEnvironmentStatus _,
                    out error))
            {
                return false;
            }

            return TryCreateCommand(
                profile,
                profile.EnvironmentId,
                out command,
                out error);
        }

        internal static bool TryCreateCommand(
            SimultriaViewerDevelopmentProfile profile,
            ApiEnvironmentId effectiveEnvironmentId,
            out CommandEnvelope command,
            out string error)
        {
            command = null;
            if (profile == null)
            {
                error = "A Simultria viewer development profile is required.";
                return false;
            }

            if (!profile.TryResolveEnvironment(
                    effectiveEnvironmentId,
                    out ApiEnvironmentStatus _,
                    out error))
            {
                return false;
            }

            long revision = Math.Max(1L, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (!profile.TryCreatePayload(
                    revision,
                    effectiveEnvironmentId,
                    out var payload,
                    out error))
            {
                return false;
            }

            command = SimultriaViewerInitializationCommand.Create(payload);
            return true;
        }

        internal static async Task<DevelopmentCommandCreation>
            CreateCommandAsync(
                SimultriaViewerDevelopmentProfile profile,
                SimultriaViewerEnvironmentResolver resolver,
                CancellationToken cancellationToken)
        {
            if (profile == null)
            {
                return DevelopmentCommandCreation.Failure(
                    null,
                    "A Simultria viewer development profile is required.");
            }

            if (resolver == null)
            {
                return DevelopmentCommandCreation.Failure(
                    null,
                    "A Simultria viewer environment resolver is required.");
            }

            SimultriaViewerEnvironmentResolution resolution =
                await resolver.ResolveAsync(profile, cancellationToken);
            if (resolution?.Succeeded != true)
            {
                return DevelopmentCommandCreation.Failure(
                    resolution,
                    resolution?.Message ??
                    "The effective Simultria environment could not be resolved.");
            }

            return TryCreateCommand(
                    profile,
                    resolution.EnvironmentId,
                    out CommandEnvelope command,
                    out string error)
                ? DevelopmentCommandCreation.Success(command, resolution)
                : DevelopmentCommandCreation.Failure(resolution, error);
        }

        public static bool TryResolveLivePort(
            out CommandRoutePortBehaviour port,
            out string error)
        {
            return TryResolveLivePort(
                out port,
                out _,
                out error);
        }

        public static bool TryResolveLivePort(
            out CommandRoutePortBehaviour port,
            out ViewerAuthenticationTarget authenticationTarget,
            out string error)
        {
            port = null;
            authenticationTarget = null;
            if (!SimultriaViewerConnectionStatus.TryResolveAuthenticationTarget(
                    null,
                    out authenticationTarget,
                    out string authenticationError))
            {
                error = "Waiting for the running viewer. " +
                        authenticationError;
                return false;
            }

            if (authenticationTarget.Session?.Status.HasAccessToken != true)
            {
                authenticationTarget = null;
                error = AuthenticationRequiredMessage;
                return false;
            }

            if (!TryResolveCommandRoute(out port, out int readyCount))
            {
                authenticationTarget = null;
                error = readyCount == 0
                    ? "Waiting for the running viewer to initialize its Command Routing scene port."
                    : "Multiple initialized Command Routing scene ports were found; exactly one is required for development auto-load.";
                return false;
            }

            error = null;
            return true;
        }

        internal static bool IsWaitingForAuthentication(string error)
        {
            return string.Equals(
                error,
                AuthenticationRequiredMessage,
                StringComparison.Ordinal);
        }

        public static bool TryResolveCommandRoute(
            out CommandRoutePortBehaviour route,
            out int readyCount)
        {
            route = null;
            readyCount = 0;
            CommandRoutePortBehaviour[] candidates =
                UnityEngine.Object.FindObjectsByType<CommandRoutePortBehaviour>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!candidates[i].IsReady)
                {
                    continue;
                }

                readyCount++;
                route = candidates[i];
            }

            if (readyCount == 1)
            {
                return true;
            }

            route = null;
            return false;
        }

        public static async Task<CommandResult> DispatchAsync(
            CommandEnvelope command,
            CancellationToken cancellationToken)
        {
            if (!TryResolveLivePort(
                    out CommandRoutePortBehaviour port,
                    out ViewerAuthenticationTarget authenticationTarget,
                    out string error))
            {
                return CommandResult.Failure("viewer_not_ready", error);
            }

            if (!SimultriaViewerDevelopmentProfileSelector.TryResolve(
                    out SimultriaViewerDevelopmentProfile profile,
                    out _,
                    out error))
            {
                return CommandResult.Failure(
                    "development_profile_unavailable",
                    error);
            }

            return await DispatchToPortAsync(
                command,
                profile,
                authenticationTarget,
                port,
                cancellationToken);
        }

        internal static async Task<CommandResult> DispatchToPortAsync(
            CommandEnvelope command,
            SimultriaViewerDevelopmentProfile profile,
            ViewerAuthenticationTarget authenticationTarget,
            CommandRoutePortBehaviour port,
            CancellationToken cancellationToken)
        {
            LiveCommandPreparation preparation = await PrepareLiveCommandAsync(
                command,
                profile,
                authenticationTarget,
                cancellationToken);
            if (!preparation.Succeeded)
            {
                return CommandResult.Failure(
                    preparation.ErrorCode,
                    preparation.Message);
            }

            CommandRouteOutcome outcome = await port.RouteMessageAsync(
                SimultriaViewerInitializationCommand.Serialize(
                    preparation.Command,
                    false),
                SimultriaViewerInitializationCommand.DevelopmentTransport,
                SimultriaViewerInitializationCommand.DevelopmentRemoteEndpoint,
                cancellationToken);
            return outcome?.Result ?? CommandResult.Failure(
                "route_unavailable",
                "The command route returned no result.");
        }

        internal static async Task<LiveCommandPreparation>
            PrepareLiveCommandAsync(
                CommandEnvelope command,
                SimultriaViewerDevelopmentProfile profile,
                ViewerAuthenticationTarget authenticationTarget,
                CancellationToken cancellationToken)
        {
            if (command == null || profile == null ||
                authenticationTarget?.Session == null)
            {
                return LiveCommandPreparation.Failure(
                    "live_context_unavailable",
                    "The live Simultria viewer context is incomplete.");
            }

            if (!command.TryReadPayload(
                    out SimultriaViewerInitializationPayload payload,
                    out string payloadError) ||
                !payload.IsValid(out payloadError))
            {
                return LiveCommandPreparation.Failure(
                    "invalid_initialization",
                    payloadError);
            }

            if (!ApiEnvironmentId.TryParse(
                    payload.EnvironmentId,
                    out ApiEnvironmentId effectiveEnvironmentId))
            {
                return LiveCommandPreparation.Failure(
                    "development_context_changed",
                    "The prepared command has no valid Simultria environment.");
            }

            if (profile.EnvironmentResolutionMode ==
                    SimultriaViewerEnvironmentResolutionMode.Manual &&
                effectiveEnvironmentId != profile.EnvironmentId)
            {
                return LiveCommandPreparation.Failure(
                    "development_context_changed",
                    "The selected Simultria environment changed after the " +
                    "development command was prepared.");
            }

            if (!SimultriaViewerConnectionAuthentication.TryValidateTarget(
                    authenticationTarget,
                    profile,
                    effectiveEnvironmentId,
                    out string authenticationError))
            {
                return LiveCommandPreparation.Failure(
                    "authentication_context_mismatch",
                    authenticationError);
            }

            if (!profile.TryCreateComposition(
                    out ApiComposition composition,
                    out string compositionError))
            {
                return LiveCommandPreparation.Failure(
                    "api_composition_unavailable",
                    compositionError);
            }

            IApiClient apiClient = ApiClientFactory.Create(
                null,
                authenticationTarget.Session.ApiAuthProvider);
            var resolver = new SimultriaViewerModelResolver(
                apiClient,
                composition,
                effectiveEnvironmentId);
            SimultriaViewerModelResolveResult resolved =
                await resolver.ResolveAsync(
                    payload.ProjectId,
                    payload.ModelId,
                    payload.ModelVersionId,
                    cancellationToken);
            if (resolved == null || !resolved.Succeeded)
            {
                return LiveCommandPreparation.Failure(
                    resolved?.ErrorCode ?? "model_resolution_failed",
                    resolved?.Message ??
                    "The Simultria model source could not be resolved.");
            }

            return TryEnrichLiveCommand(
                command,
                payload,
                resolved.DownloadUrl,
                resolved.ModelVersionId,
                out CommandEnvelope enriched,
                out string enrichmentError)
                ? LiveCommandPreparation.Success(enriched)
                : LiveCommandPreparation.Failure(
                    "unsafe_model_source",
                    enrichmentError);
        }

        internal static bool TryEnrichLiveCommand(
            CommandEnvelope command,
            SimultriaViewerInitializationPayload payload,
            string modelUrl,
            int modelVersionId,
            out CommandEnvelope enriched,
            out string error)
        {
            enriched = null;
            if (command == null || payload == null || modelVersionId <= 0 ||
                string.IsNullOrWhiteSpace(modelUrl))
            {
                error = "The resolved model source is incomplete.";
                return false;
            }

            if (ContainsBearerQuery(modelUrl))
            {
                error =
                    "The resolved model URL contains a bearer-like query value.";
                return false;
            }

            payload.ModelUrl = modelUrl.Trim();
            payload.ModelVersionId = modelVersionId;
            payload.ModelVersion =
                modelVersionId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            if (!payload.IsValid(out error))
            {
                return false;
            }

            var enrichedPayload = JObject.FromObject(payload);
            enriched = new CommandEnvelope(
                command.CommandName,
                enrichedPayload,
                command.CommandId,
                command.ProtocolVersion,
                command.Metadata,
                command.RawEnvelope);
            error = null;
            return true;
        }

        private static bool ContainsBearerQuery(string modelUrl)
        {
            if (!Uri.TryCreate(modelUrl, UriKind.Absolute, out Uri uri))
            {
                return true;
            }

            string query = uri.Query ?? string.Empty;
            return query.IndexOf(
                       "access_token=",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   query.IndexOf(
                       "bearer=",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal sealed class LiveCommandPreparation
        {
            private LiveCommandPreparation(
                bool succeeded,
                CommandEnvelope command,
                string errorCode,
                string message)
            {
                Succeeded = succeeded;
                Command = command;
                ErrorCode = errorCode;
                Message = message;
            }

            internal bool Succeeded { get; }
            internal CommandEnvelope Command { get; }
            internal string ErrorCode { get; }
            internal string Message { get; }

            internal static LiveCommandPreparation Success(
                CommandEnvelope command) =>
                new LiveCommandPreparation(true, command, null, null);

            internal static LiveCommandPreparation Failure(
                string errorCode,
                string message) =>
                new LiveCommandPreparation(
                    false,
                    null,
                    errorCode,
                    message);
        }

        internal sealed class DevelopmentCommandCreation
        {
            private DevelopmentCommandCreation(
                bool succeeded,
                CommandEnvelope command,
                SimultriaViewerEnvironmentResolution resolution,
                string message)
            {
                Succeeded = succeeded;
                Command = command;
                Resolution = resolution;
                Message = message;
            }

            internal bool Succeeded { get; }
            internal CommandEnvelope Command { get; }
            internal SimultriaViewerEnvironmentResolution Resolution { get; }
            internal string Message { get; }

            internal static DevelopmentCommandCreation Success(
                CommandEnvelope command,
                SimultriaViewerEnvironmentResolution resolution) =>
                new DevelopmentCommandCreation(
                    true,
                    command,
                    resolution,
                    null);

            internal static DevelopmentCommandCreation Failure(
                SimultriaViewerEnvironmentResolution resolution,
                string message) =>
                new DevelopmentCommandCreation(
                    false,
                    null,
                    resolution,
                    message);
        }
    }
}
