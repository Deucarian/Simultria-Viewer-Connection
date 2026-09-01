using System;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Authentication;
using Deucarian.Simultria.API.Authentication;

namespace Deucarian.SimultriaViewerIntegration
{
    public static partial class SimultriaViewerConnectionAuthentication
    {
        internal static bool TryRegister(
            ApiConnectionSettings connectionSettings,
            ApiComposition composition,
            ApiEnvironmentId environmentId,
            string targetId,
            string displayName,
            IAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient = null)
        {
            registration = null;
            if (connectionSettings == null || composition == null)
            {
                environmentStatus = null;
                error = "Assign a resolved Simultria API connection.";
                return false;
            }

            return TryRegisterResolvedProfile(
                connectionSettings,
                composition,
                environmentId,
                targetId,
                displayName,
                session,
                (IApiClient effectiveClient,
                    out SimultriaAuthenticationProvider provider,
                    out ApiEnvironmentStatus status,
                    out string message) => TryCreateResolvedAuthenticationProvider(
                        composition,
                        environmentId,
                        effectiveClient,
                        out provider,
                        out status,
                        out message),
                out registration,
                out environmentStatus,
                out error,
                apiClient);
        }

        private static bool TryCreateResolvedAuthenticationProvider(
            ApiComposition composition,
            ApiEnvironmentId environmentId,
            IApiClient apiClient,
            out SimultriaAuthenticationProvider provider,
            out ApiEnvironmentStatus environmentStatus,
            out string error)
        {
            provider = null;
            environmentStatus = composition?.GetEnvironmentStatus(
                environmentId);
            if (environmentStatus?.IsResolved != true)
            {
                error = environmentStatus?.Message ??
                        "A resolved Simultria API composition is required.";
                return false;
            }

            if (apiClient == null)
            {
                error = "A Deucarian API client is required.";
                return false;
            }

            provider = SimultriaAuthenticationProviderFactory.Create(
                composition,
                environmentId,
                apiClient);
            error = null;
            return true;
        }
    }
}
