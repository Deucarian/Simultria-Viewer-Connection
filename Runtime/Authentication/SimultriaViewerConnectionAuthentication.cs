using System;
using System.Collections.Generic;
using Deucarian.API.Authentication;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Authentication;
using Deucarian.Simultria.API.Configuration;
using Deucarian.ViewerAuthentication;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection
{
    /// <summary>
    /// Composes one environment-specific Simultria provider into the generic
    /// Viewer Authentication registry without duplicating token/session logic.
    /// </summary>
    public static class SimultriaViewerConnectionAuthentication
    {
        private static readonly object BindingGate = new object();
        private static readonly Dictionary<
            SimultriaViewerAuthenticationProvider,
            AuthenticationBinding> Bindings =
            new Dictionary<
                SimultriaViewerAuthenticationProvider,
                AuthenticationBinding>();

        /// <summary>
        /// Stable target ID shared by the package's Edit Mode workspace and
        /// the ordinary single-viewer runtime composition.
        /// </summary>
        public const string DefaultTargetId = "simultria-viewer";

        public const string DefaultDisplayName = "Simultria Viewer";

        /// <summary>
        /// Registers a single-viewer runtime session using the package's
        /// stable target identity.
        /// </summary>
        public static bool TryRegister(
            SimultriaViewerDevelopmentProfile developmentProfile,
            IViewerAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient = null)
        {
            if (developmentProfile?.EnvironmentResolutionMode ==
                SimultriaViewerEnvironmentResolutionMode
                    .AutomaticFromUnityBuildVersion)
            {
                registration = null;
                environmentStatus = null;
                error = "Resolve the automatic Simultria environment before " +
                        "registering authentication.";
                return false;
            }

            return TryRegister(
                developmentProfile,
                developmentProfile == null
                    ? default(ApiEnvironmentId)
                    : developmentProfile.EnvironmentId,
                DefaultTargetId,
                DefaultDisplayName,
                session,
                out registration,
                out environmentStatus,
                out error,
                apiClient);
        }

        public static bool TryRegister(
            SimultriaViewerDevelopmentProfile developmentProfile,
            ApiEnvironmentId effectiveEnvironmentId,
            IViewerAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient = null)
        {
            return TryRegister(
                developmentProfile,
                effectiveEnvironmentId,
                DefaultTargetId,
                DefaultDisplayName,
                session,
                out registration,
                out environmentStatus,
                out error,
                apiClient);
        }

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
            if (developmentProfile?.EnvironmentResolutionMode ==
                SimultriaViewerEnvironmentResolutionMode
                    .AutomaticFromUnityBuildVersion)
            {
                registration = null;
                environmentStatus = null;
                error = "Resolve the automatic Simultria environment before " +
                        "registering authentication.";
                return false;
            }

            return TryRegister(
                developmentProfile,
                developmentProfile == null
                    ? default(ApiEnvironmentId)
                    : developmentProfile.EnvironmentId,
                targetId,
                displayName,
                session,
                out registration,
                out environmentStatus,
                out error,
                apiClient);
        }

        private static bool TryRegister(
            SimultriaViewerDevelopmentProfile developmentProfile,
            ApiEnvironmentId effectiveEnvironmentId,
            string targetId,
            string displayName,
            IViewerAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient)
        {
            registration = null;
            if (developmentProfile == null)
            {
                environmentStatus = null;
                error = "A Simultria viewer development profile is required.";
                return false;
            }

            ApiConnectionProfile connectionProfile =
                developmentProfile.ConnectionProfileReference;
            if (connectionProfile != null)
            {
                return TryRegister(
                    connectionProfile,
                    effectiveEnvironmentId,
                    targetId,
                    displayName,
                    session,
                    out registration,
                    out environmentStatus,
                    out error,
                    apiClient);
            }

            SimultriaApiProfile legacyProfile =
                developmentProfile.EffectiveApiProfile;
            return TryRegister(
                legacyProfile,
                effectiveEnvironmentId,
                targetId,
                displayName,
                session,
                out registration,
                out environmentStatus,
                out error,
                apiClient);
        }

        /// <summary>
        /// Registers a single-viewer runtime session from a project-owned
        /// generic API connection profile and environment.
        /// </summary>
        public static bool TryRegister(
            ApiConnectionProfile connectionProfile,
            ApiEnvironmentId environmentId,
            IViewerAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient = null)
        {
            return TryRegister(
                connectionProfile,
                environmentId,
                DefaultTargetId,
                DefaultDisplayName,
                session,
                out registration,
                out environmentStatus,
                out error,
                apiClient);
        }

        /// <summary>
        /// Registers a runtime session from a Simultria-compatible generic
        /// API connection profile. The adapter validates the catalog and all
        /// standard environment slots before a target can be registered.
        /// </summary>
        public static bool TryRegister(
            ApiConnectionProfile connectionProfile,
            ApiEnvironmentId environmentId,
            string targetId,
            string displayName,
            IViewerAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient = null)
        {
            registration = null;
            if (connectionProfile == null)
            {
                environmentStatus = null;
                error = "Assign a Simultria API connection profile.";
                return false;
            }

            if (!SimultriaApiConnectionProfileAdapter.TryCreateComposition(
                    connectionProfile,
                    out ApiComposition composition,
                    out error))
            {
                environmentStatus = null;
                return false;
            }

            return TryRegisterResolvedProfile(
                connectionProfile,
                composition,
                environmentId,
                targetId,
                displayName,
                session,
                (IApiClient effectiveClient,
                    out SimultriaViewerAuthenticationProvider provider,
                    out ApiEnvironmentStatus status,
                    out string message) =>
                    SimultriaViewerAuthenticationProviderFactory.TryCreate(
                        connectionProfile,
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

        /// <summary>
        /// Registers a single-viewer runtime session from an explicit
        /// Simultria API profile and environment, without requiring a
        /// development project/model profile.
        /// </summary>
        public static bool TryRegister(
            SimultriaApiProfile apiProfile,
            ApiEnvironmentId environmentId,
            IViewerAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient = null)
        {
            return TryRegister(
                apiProfile,
                environmentId,
                DefaultTargetId,
                DefaultDisplayName,
                session,
                out registration,
                out environmentStatus,
                out error,
                apiClient);
        }

        /// <summary>
        /// Registers a runtime session from explicit environment composition.
        /// Use the overload without target strings for the ordinary one-viewer
        /// case so Edit Mode and Play Mode share remembered-token ownership.
        /// </summary>
        public static bool TryRegister(
            SimultriaApiProfile apiProfile,
            ApiEnvironmentId environmentId,
            string targetId,
            string displayName,
            IViewerAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient = null)
        {
            registration = null;
            if (apiProfile == null)
            {
                environmentStatus = null;
                error = "The package-provided Simultria API profile is missing.";
                return false;
            }

            if (!apiProfile.TryCreateComposition(
                    out ApiComposition composition,
                    out error))
            {
                environmentStatus = null;
                return false;
            }

            return TryRegisterResolvedProfile(
                apiProfile,
                composition,
                environmentId,
                targetId,
                displayName,
                session,
                (IApiClient effectiveClient,
                    out SimultriaViewerAuthenticationProvider provider,
                    out ApiEnvironmentStatus status,
                    out string message) =>
                    SimultriaViewerAuthenticationProviderFactory.TryCreate(
                        apiProfile,
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

        private static bool TryRegisterResolvedProfile(
            ScriptableObject profile,
            ApiComposition composition,
            ApiEnvironmentId environmentId,
            string targetId,
            string displayName,
            IViewerAuthenticationSession session,
            TryCreateAuthenticationProvider createProvider,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient)
        {
            registration = null;
            if (session == null)
            {
                environmentStatus =
                    composition.GetEnvironmentStatus(environmentId);
                error = "A Viewer Authentication session is required.";
                return false;
            }

            try
            {
                string apiBaseUrl = null;
                if (apiClient == null &&
                    composition.TryResolveClient(
                        environmentId,
                        SimultriaClientIds.Primary,
                        out ApiResolvedClient resolvedClient,
                        out _))
                {
                    apiBaseUrl = resolvedClient.BaseUrl;
                }

                IApiClient effectiveClient = apiClient ??
                    CreateSessionApiClient(session, apiBaseUrl);
                if (!createProvider(
                        effectiveClient,
                        out SimultriaViewerAuthenticationProvider provider,
                        out environmentStatus,
                        out error))
                {
                    return false;
                }

                registration = RegisterBoundTarget(
                    targetId,
                    displayName,
                    session,
                    provider,
                    profile,
                    environmentId);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                registration?.Dispose();
                registration = null;
                environmentStatus = null;
                error =
                    "Could not register the Simultria viewer authentication " +
                    "target (" + exception.GetType().Name + ").";
                return false;
            }
        }

        private delegate bool TryCreateAuthenticationProvider(
            IApiClient apiClient,
            out SimultriaViewerAuthenticationProvider provider,
            out ApiEnvironmentStatus environmentStatus,
            out string error);

        internal static IApiClient CreateSessionApiClient(
            IViewerAuthenticationSession session,
            string apiBaseUrl = null,
            Func<ApiClientConfig, IApiAuthProvider, IApiClient> clientFactory =
                null)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            ApiClientConfig config = ApiClientConfig.CreateRuntimeDefault();
            config.BaseUrl = apiBaseUrl ?? string.Empty;
            Func<ApiClientConfig, IApiAuthProvider, IApiClient>
                effectiveFactory =
                clientFactory ??
                ((runtimeConfig, authProvider) =>
                    ApiClientFactory.Create(runtimeConfig, authProvider));
            return effectiveFactory(config, session.ApiAuthProvider) ??
                throw new InvalidOperationException(
                    "The authenticated API client factory returned no client.");
        }

        internal static bool TryValidateTarget(
            ViewerAuthenticationTarget target,
            SimultriaApiProfile expectedProfile,
            ApiEnvironmentId expectedEnvironmentId,
            out string error)
        {
            ApiComposition expectedComposition = null;
            if (expectedProfile != null &&
                !expectedProfile.TryCreateComposition(
                    out expectedComposition,
                    out error))
            {
                return false;
            }

            return TryValidateTarget(
                target,
                expectedProfile,
                expectedComposition,
                expectedEnvironmentId,
                out error);
        }

        internal static bool TryValidateTarget(
            ViewerAuthenticationTarget target,
            ApiConnectionProfile expectedProfile,
            ApiEnvironmentId expectedEnvironmentId,
            out string error)
        {
            ApiComposition expectedComposition = null;
            if (expectedProfile != null &&
                !SimultriaApiConnectionProfileAdapter.TryCreateComposition(
                    expectedProfile,
                    out expectedComposition,
                    out error))
            {
                return false;
            }

            return TryValidateTarget(
                target,
                expectedProfile,
                expectedComposition,
                expectedEnvironmentId,
                out error);
        }

        internal static bool TryValidateTarget(
            ViewerAuthenticationTarget target,
            SimultriaViewerDevelopmentProfile expectedProfile,
            ApiEnvironmentId expectedEnvironmentId,
            out string error)
        {
            if (expectedProfile?.ConnectionProfileReference != null)
            {
                return TryValidateTarget(
                    target,
                    expectedProfile.ConnectionProfileReference,
                    expectedEnvironmentId,
                    out error);
            }

            return TryValidateTarget(
                target,
                expectedProfile?.EffectiveApiProfile,
                expectedEnvironmentId,
                out error);
        }

        private static bool TryValidateTarget(
            ViewerAuthenticationTarget target,
            ScriptableObject expectedProfile,
            ApiComposition expectedComposition,
            ApiEnvironmentId expectedEnvironmentId,
            out string error)
        {
            if (target == null ||
                !string.Equals(
                    target.Id,
                    DefaultTargetId,
                    StringComparison.Ordinal))
            {
                error =
                    "The stable Simultria viewer authentication target is not registered.";
                return false;
            }

            if (!(target.AcquisitionProvider is
                    SimultriaViewerAuthenticationProvider provider) ||
                !(target.ValidationProvider is
                    SimultriaViewerAuthenticationProvider validator) ||
                !ReferenceEquals(provider, validator))
            {
                error =
                    "The stable viewer target is not backed by one authoritative Simultria authentication provider.";
                return false;
            }

            AuthenticationBinding binding;
            lock (BindingGate)
            {
                Bindings.TryGetValue(provider, out binding);
            }

            if (binding == null ||
                !ReferenceEquals(binding.Composition, provider.Composition) ||
                binding.EnvironmentId != provider.EnvironmentId)
            {
                error =
                    "The Simultria authentication target has no trusted connection binding.";
                return false;
            }

            if (expectedProfile == null)
            {
                error = null;
                return true;
            }

            if (!ReferenceEquals(binding.Profile, expectedProfile) ||
                binding.EnvironmentId != expectedEnvironmentId ||
                provider.EnvironmentId != expectedEnvironmentId ||
                !TryMatchCurrentComposition(
                    provider,
                    expectedComposition,
                    expectedEnvironmentId))
            {
                error =
                    "The registered Simultria authentication environment does not match the selected development profile.";
                return false;
            }

            error = null;
            return true;
        }

        private static bool TryMatchCurrentComposition(
            SimultriaViewerAuthenticationProvider provider,
            ApiComposition expectedComposition,
            ApiEnvironmentId expectedEnvironmentId)
        {
            if (expectedComposition == null ||
                expectedComposition.CatalogId != provider.Composition.CatalogId ||
                !expectedComposition.TryResolveClient(
                    expectedEnvironmentId,
                    SimultriaClientIds.Primary,
                    out ApiResolvedClient expectedClient,
                    out _) ||
                !provider.Composition.TryResolveClient(
                    provider.EnvironmentId,
                    SimultriaClientIds.Primary,
                    out ApiResolvedClient providerClient,
                    out _) ||
                !string.Equals(
                    expectedClient.BaseUrl,
                    providerClient.BaseUrl,
                    StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                ApiResolvedEndpoint expectedLogin =
                    expectedComposition.ResolveEndpoint(
                        expectedEnvironmentId,
                        SimultriaEndpointIds.Login);
                ApiResolvedEndpoint expectedValidation =
                    expectedComposition.ResolveEndpoint(
                        expectedEnvironmentId,
                        SimultriaEndpointIds.ValidateAuthentication);
                return string.Equals(
                           expectedLogin.Endpoint.Path,
                           provider.AcquisitionEndpoint,
                           StringComparison.Ordinal) &&
                       string.Equals(
                           expectedValidation.Endpoint.Path,
                           provider.ValidationEndpoint,
                           StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static IDisposable RegisterBoundTarget(
            string targetId,
            string displayName,
            IViewerAuthenticationSession session,
            SimultriaViewerAuthenticationProvider provider,
            ScriptableObject profile,
            ApiEnvironmentId environmentId)
        {
            var binding = new AuthenticationBinding(
                profile,
                environmentId,
                provider.Composition);
            lock (BindingGate)
            {
                Bindings.Add(provider, binding);
            }

            try
            {
                IDisposable targetRegistration =
                    ViewerAuthenticationTargetRegistry.Register(
                        targetId,
                        displayName,
                        session,
                        provider,
                        provider);
                return new BoundRegistration(
                    targetRegistration,
                    provider);
            }
            catch
            {
                RemoveBinding(provider);
                throw;
            }
        }

        private static void RemoveBinding(
            SimultriaViewerAuthenticationProvider provider)
        {
            lock (BindingGate)
            {
                Bindings.Remove(provider);
            }
        }

        private sealed class AuthenticationBinding
        {
            internal AuthenticationBinding(
                ScriptableObject profile,
                ApiEnvironmentId environmentId,
                ApiComposition composition)
            {
                Profile = profile;
                EnvironmentId = environmentId;
                Composition = composition;
            }

            internal ScriptableObject Profile { get; }
            internal ApiEnvironmentId EnvironmentId { get; }
            internal ApiComposition Composition { get; }
        }

        private sealed class BoundRegistration : IDisposable
        {
            private IDisposable registration;
            private SimultriaViewerAuthenticationProvider provider;

            internal BoundRegistration(
                IDisposable targetRegistration,
                SimultriaViewerAuthenticationProvider authenticationProvider)
            {
                registration = targetRegistration;
                provider = authenticationProvider;
            }

            public void Dispose()
            {
                IDisposable currentRegistration = registration;
                SimultriaViewerAuthenticationProvider currentProvider =
                    provider;
                registration = null;
                provider = null;
                try
                {
                    currentRegistration?.Dispose();
                }
                finally
                {
                    if (currentProvider != null)
                    {
                        RemoveBinding(currentProvider);
                    }
                }
            }
        }
    }
}
