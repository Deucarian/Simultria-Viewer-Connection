using System;
using Deucarian.API.Core;
using Deucarian.Simultria.API.Authentication;
using Deucarian.Simultria.API.Configuration;
using Deucarian.ViewerAuthentication;

namespace Deucarian.SimultriaViewerConnection
{
    /// <summary>
    /// Composes one environment-specific Simultria provider into the generic
    /// Viewer Authentication registry without duplicating token/session logic.
    /// </summary>
    public static class SimultriaViewerConnectionAuthentication
    {
        public static bool TryRegister(
            SimultriaViewerDevelopmentProfile developmentProfile,
            string targetId,
            string displayName,
            IViewerAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient = null)
        {
            registration = null;
            if (developmentProfile == null)
            {
                environmentStatus = null;
                error = "A Simultria viewer development profile is required.";
                return false;
            }

            if (session == null)
            {
                developmentProfile.TryResolveEnvironment(
                    out environmentStatus,
                    out _);
                error = "A Viewer Authentication session is required.";
                return false;
            }

            SimultriaApiProfile apiProfile =
                developmentProfile.EffectiveApiProfile;
            if (apiProfile == null)
            {
                environmentStatus = null;
                error = "The package-provided Simultria API profile is missing.";
                return false;
            }

            try
            {
                IApiClient effectiveClient = apiClient ?? ApiClientFactory.CreateDefault();
                if (!SimultriaViewerAuthenticationProviderFactory.TryCreate(
                    apiProfile,
                    developmentProfile.EnvironmentId,
                    effectiveClient,
                    out SimultriaViewerAuthenticationProvider provider,
                    out environmentStatus,
                    out error))
                {
                    return false;
                }

                registration = ViewerAuthenticationTargetRegistry.Register(
                    targetId,
                    displayName,
                    session,
                    provider,
                    provider);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                registration?.Dispose();
                registration = null;
                environmentStatus = null;
                error = "Could not register the Simultria viewer authentication target: " +
                        exception.Message;
                return false;
            }
        }
    }
}
