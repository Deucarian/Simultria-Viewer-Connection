using System;
using System.Collections.Generic;
using Deucarian.API.Authentication;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Authentication;
using Deucarian.Simultria.API.Configuration;
using Deucarian.Authentication;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>
    /// Composes one environment-specific Simultria provider into the generic
    /// Authentication registry without duplicating token/session logic.
    /// </summary>
    public static partial class SimultriaViewerConnectionAuthentication
    {
        private static readonly object BindingGate = new object();
        private static readonly Dictionary<
            SimultriaAuthenticationProvider,
            AuthenticationBinding> Bindings =
            new Dictionary<
                SimultriaAuthenticationProvider,
                AuthenticationBinding>();

        /// <summary>
        /// Stable target ID shared by the package's Edit Mode workspace and
        /// the ordinary single-viewer runtime composition.
        /// </summary>
        public const string DefaultTargetId = "simultria-viewer";

        public const string DefaultDisplayName = "Simultria Viewer";

#if UNITY_EDITOR
        /// <summary>
        /// Registers a single-viewer runtime session using the package's
        /// stable target identity.
        /// </summary>
        public static bool TryRegister(
            SimultriaViewerDevelopmentContext developmentContext,
            IAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient = null)
        {
            if (developmentContext?.EnvironmentResolutionMode ==
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
                developmentContext,
                developmentContext == null
                    ? default(ApiEnvironmentId)
                    : developmentContext.EnvironmentId,
                DefaultTargetId,
                DefaultDisplayName,
                session,
                out registration,
                out environmentStatus,
                out error,
                apiClient);
        }

        public static bool TryRegister(
            SimultriaViewerDevelopmentContext developmentContext,
            ApiEnvironmentId effectiveEnvironmentId,
            IAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient = null)
        {
            return TryRegister(
                developmentContext,
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
            SimultriaViewerDevelopmentContext developmentContext,
            string targetId,
            string displayName,
            IAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient = null)
        {
            if (developmentContext?.EnvironmentResolutionMode ==
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
                developmentContext,
                developmentContext == null
                    ? default(ApiEnvironmentId)
                    : developmentContext.EnvironmentId,
                targetId,
                displayName,
                session,
                out registration,
                out environmentStatus,
                out error,
                apiClient);
        }

        private static bool TryRegister(
            SimultriaViewerDevelopmentContext developmentContext,
            ApiEnvironmentId effectiveEnvironmentId,
            string targetId,
            string displayName,
            IAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient)
        {
            registration = null;
            if (developmentContext == null)
            {
                environmentStatus = null;
                error = "A Simultria viewer development context is required.";
                return false;
            }

            ApiConnectionSettings connectionSettings =
                developmentContext.ConnectionSettingsReference;
            return TryRegister(
                connectionSettings,
                effectiveEnvironmentId,
                targetId,
                displayName,
                session,
                out registration,
                out environmentStatus,
                out error,
                apiClient);
        }
#endif

        /// <summary>
        /// Registers a single-viewer runtime session from a project-owned
        /// generic API connection settings and environment.
        /// </summary>
        public static bool TryRegister(
            ApiConnectionSettings connectionSettings,
            ApiEnvironmentId environmentId,
            IAuthenticationSession session,
            out IDisposable registration,
            out ApiEnvironmentStatus environmentStatus,
            out string error,
            IApiClient apiClient = null)
        {
            return TryRegister(
                connectionSettings,
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
        /// API connection settings. The adapter validates the catalog and all
        /// standard environment slots before a target can be registered.
        /// </summary>
        public static bool TryRegister(
            ApiConnectionSettings connectionSettings,
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
            if (connectionSettings == null)
            {
                environmentStatus = null;
                error = "Assign Simultria API connection settings.";
                return false;
            }

            if (!SimultriaApiConnectionSettingsAdapter.TryCreateComposition(
                    connectionSettings,
                    out ApiComposition composition,
                    out error))
            {
                environmentStatus = null;
                return false;
            }

            return TryRegister(
                connectionSettings,
                composition,
                environmentId,
                targetId,
                displayName,
                session,
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
            IAuthenticationSession session,
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
                        out SimultriaAuthenticationProvider provider,
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
            out SimultriaAuthenticationProvider provider,
            out ApiEnvironmentStatus environmentStatus,
            out string error);

        internal static IApiClient CreateSessionApiClient(
            IAuthenticationSession session,
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

        private static IDisposable RegisterBoundTarget(
            string targetId,
            string displayName,
            IAuthenticationSession session,
            SimultriaAuthenticationProvider provider,
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
                if (!provider.Composition.TryResolveClient(
                        environmentId,
                        SimultriaClientIds.Primary,
                        out ApiResolvedClient client,
                        out string identityError))
                {
                    throw new InvalidOperationException(identityError);
                }

                if (string.IsNullOrWhiteSpace(
                        binding.CompositionFingerprint))
                {
                    throw new InvalidOperationException(
                        "The Simultria authentication target requires an " +
                        "exact composition fingerprint.");
                }

                var persistenceIdentity =
                    new AuthenticationPersistenceIdentity(
                        SimultriaServiceIds.ApiV2.Value,
                        environmentId.Value,
                        client.BaseUrl,
                        SimultriaClientIds.Primary.Value,
                        accountId: null,
                        configurationFingerprint:
                            binding.CompositionFingerprint);
                IDisposable targetRegistration =
                    AuthenticationTargetRegistry.Register(
                        targetId,
                        displayName,
                        session,
                        provider,
                        provider,
                        persistenceIdentity);
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
            SimultriaAuthenticationProvider provider)
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
                if (profile is ApiConnectionSettings settings)
                {
                    SimultriaViewerConnectionCompositionFingerprint.TryCreate(
                        settings,
                        composition,
                        environmentId,
                        out string fingerprint);
                    CompositionFingerprint = fingerprint ?? string.Empty;
                }
                else
                {
                    CompositionFingerprint = string.Empty;
                }
            }

            internal ScriptableObject Profile { get; }
            internal ApiEnvironmentId EnvironmentId { get; }
            internal ApiComposition Composition { get; }
            internal string CompositionFingerprint { get; }
        }

        private sealed class BoundRegistration : IDisposable
        {
            private IDisposable registration;
            private SimultriaAuthenticationProvider provider;

            internal BoundRegistration(
                IDisposable targetRegistration,
                SimultriaAuthenticationProvider authenticationProvider)
            {
                registration = targetRegistration;
                provider = authenticationProvider;
            }

            public void Dispose()
            {
                IDisposable currentRegistration = registration;
                SimultriaAuthenticationProvider currentProvider =
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
