using Deucarian.API.Core;
using Deucarian.ViewerAuthentication;

namespace Deucarian.SimultriaViewerConnection
{
    /// <summary>Sanitized aggregate status for editor and diagnostics presentation.</summary>
    public sealed class SimultriaViewerConnectionStatus
    {
        internal SimultriaViewerConnectionStatus(
            ApiEnvironmentStatus environment,
            string environmentMessage,
            ViewerAuthenticationStatusSnapshot authentication)
        {
            Environment = environment;
            EnvironmentMessage = environmentMessage;
            Authentication = authentication;
        }

        public ApiEnvironmentStatus Environment { get; }
        public string EnvironmentMessage { get; }
        public ViewerAuthenticationStatusSnapshot Authentication { get; }

        public static SimultriaViewerConnectionStatus Capture(
            SimultriaViewerDevelopmentProfile profile)
        {
            ApiEnvironmentStatus environment = null;
            string environmentMessage =
                "No Simultria viewer development profile is selected.";
            profile?.TryResolveEnvironment(
                out environment,
                out environmentMessage);
            ViewerAuthenticationStatusSnapshot authentication = null;
            if (TryResolveAuthenticationTarget(
                    out ViewerAuthenticationTarget authenticationTarget))
            {
                authentication = authenticationTarget.Session.Status;
            }

            return new SimultriaViewerConnectionStatus(
                environment,
                environmentMessage,
                authentication);
        }

        public static bool TryResolveAuthenticationTarget(
            out ViewerAuthenticationTarget target)
        {
            var targets = ViewerAuthenticationTargetRegistry.Targets;
            if (targets.Count == 1)
            {
                target = targets[0];
                return true;
            }

            target = null;
            return false;
        }
    }
}
