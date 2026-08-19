using System;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.CommandRouting;
using Deucarian.API.Core;
using Deucarian.ViewerAuthentication;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection.Editor
{
    internal static class SimultriaViewerDevelopmentCommandService
    {
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

            long revision = Math.Max(1L, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (!profile.TryCreatePayload(revision, out var payload, out error))
            {
                return false;
            }

            command = SimultriaViewerInitializationCommand.Create(payload);
            return true;
        }

        public static bool TryResolveLivePort(
            out CommandRoutePortBehaviour port,
            out string error)
        {
            port = null;
            if (!SimultriaViewerConnectionStatus.TryResolveAuthenticationTarget(
                    out ViewerAuthenticationTarget authenticationTarget))
            {
                int targetCount = ViewerAuthenticationTargetRegistry.Targets.Count;
                error = targetCount == 0
                    ? "Waiting for the running viewer to register its Viewer Authentication target."
                    : "Multiple Viewer Authentication targets are registered; exactly one is required for development auto-load.";
                return false;
            }

            if (authenticationTarget.Session?.Status.HasAccessToken != true)
            {
                error = "Waiting for authentication. Open Tools/Deucarian/Viewer/Authentication.";
                return false;
            }

            if (!TryResolveCommandRoute(out port, out int readyCount))
            {
                error = readyCount == 0
                    ? "Waiting for the running viewer to initialize its Command Routing scene port."
                    : "Multiple initialized Command Routing scene ports were found; exactly one is required for development auto-load.";
                return false;
            }

            error = null;
            return true;
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
            if (!TryResolveLivePort(out var port, out string error))
            {
                return CommandResult.Failure("viewer_not_ready", error);
            }

            CommandRouteOutcome outcome = await port.RouteMessageAsync(
                SimultriaViewerInitializationCommand.Serialize(command, false),
                SimultriaViewerInitializationCommand.DevelopmentTransport,
                SimultriaViewerInitializationCommand.DevelopmentRemoteEndpoint,
                cancellationToken);
            return outcome?.Result ?? CommandResult.Failure(
                "route_unavailable",
                "The command route returned no result.");
        }
    }
}
