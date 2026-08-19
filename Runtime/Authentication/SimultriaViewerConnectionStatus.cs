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
                    profile,
                    out ViewerAuthenticationTarget authenticationTarget,
                    out _))
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
            return TryResolveAuthenticationTarget(
                null,
                out target,
                out _);
        }

        internal static bool TryResolveAuthenticationTarget(
            SimultriaViewerDevelopmentProfile profile,
            out ViewerAuthenticationTarget target,
            out string error)
        {
            var targets = ViewerAuthenticationTargetRegistry.Targets;
            if (targets.Count != 1)
            {
                target = null;
                error = targets.Count == 0
                    ? "The stable Simultria viewer authentication target is not registered."
                    : "Multiple Viewer Authentication targets are registered; exactly one is required.";
                return false;
            }

            target = targets[0];
            if (!SimultriaViewerConnectionAuthentication.TryValidateTarget(
                    target,
                    profile,
                    profile == null
                        ? default(Deucarian.API.Models.ApiEnvironmentId)
                        : profile.EnvironmentId,
                    out error))
            {
                target = null;
                return false;
            }

            return true;
        }
    }
}
