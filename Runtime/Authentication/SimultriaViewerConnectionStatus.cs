using Deucarian.API.Core;
using Deucarian.Authentication;

#if UNITY_EDITOR
namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>Sanitized aggregate status for editor and diagnostics presentation.</summary>
    public sealed class SimultriaViewerConnectionStatus
    {
        internal SimultriaViewerConnectionStatus(
            ApiEnvironmentStatus environment,
            string environmentMessage,
            AuthenticationStatusSnapshot authentication)
        {
            Environment = environment;
            EnvironmentMessage = environmentMessage;
            Authentication = authentication;
        }

        public ApiEnvironmentStatus Environment { get; }
        public string EnvironmentMessage { get; }
        public AuthenticationStatusSnapshot Authentication { get; }

        public static SimultriaViewerConnectionStatus Capture(
            SimultriaViewerDevelopmentContext profile)
        {
            if (profile?.EnvironmentResolutionMode ==
                SimultriaViewerEnvironmentResolutionMode
                    .AutomaticFromUnityBuildVersion)
            {
                profile.TryResolveEnvironment(
                    out ApiEnvironmentStatus unresolved,
                    out string resolutionMessage);
                return new SimultriaViewerConnectionStatus(
                    unresolved,
                    resolutionMessage,
                    null);
            }

            return Capture(
                profile,
                profile == null
                    ? default(Deucarian.API.Models.ApiEnvironmentId)
                    : profile.EnvironmentId);
        }

        public static SimultriaViewerConnectionStatus Capture(
            SimultriaViewerDevelopmentContext profile,
            Deucarian.API.Models.ApiEnvironmentId effectiveEnvironmentId)
        {
            ApiEnvironmentStatus environment = null;
            string environmentMessage =
                "No Simultria viewer development context is selected.";
            profile?.TryResolveEnvironment(
                effectiveEnvironmentId,
                out environment,
                out environmentMessage);
            AuthenticationStatusSnapshot authentication = null;
            if (TryResolveAuthenticationTarget(
                    profile,
                    effectiveEnvironmentId,
                    out AuthenticationTarget authenticationTarget,
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
            out AuthenticationTarget target)
        {
            return TryResolveAuthenticationTarget(
                null,
                out target,
                out _);
        }

        internal static bool TryResolveAuthenticationTarget(
            SimultriaViewerDevelopmentContext profile,
            out AuthenticationTarget target,
            out string error)
        {
            if (profile?.EnvironmentResolutionMode ==
                SimultriaViewerEnvironmentResolutionMode
                    .AutomaticFromUnityBuildVersion)
            {
                target = null;
                error = "Resolve the automatic Simultria environment before " +
                        "validating its authentication target.";
                return false;
            }

            return TryResolveAuthenticationTarget(
                profile,
                profile == null
                    ? default(Deucarian.API.Models.ApiEnvironmentId)
                    : profile.EnvironmentId,
                out target,
                out error);
        }

        internal static bool TryResolveAuthenticationTarget(
            SimultriaViewerDevelopmentContext profile,
            Deucarian.API.Models.ApiEnvironmentId effectiveEnvironmentId,
            out AuthenticationTarget target,
            out string error)
        {
            var targets = AuthenticationTargetRegistry.Targets;
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
                    effectiveEnvironmentId,
                    out error))
            {
                target = null;
                return false;
            }

            return true;
        }
    }
}
#endif
