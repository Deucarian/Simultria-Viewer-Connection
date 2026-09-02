using System;
using Deucarian.API.Authentication;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;

namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>
    /// Composes the one authenticated Simultria API client, effective
    /// environment, primary client, and model resolver used by viewer
    /// products. It never chooses an implicit fallback environment.
    /// </summary>
    public sealed partial class SimultriaViewerModelInitializationCoordinator
    {
        private readonly Func<
            ApiClientConfig,
            IApiAuthProvider,
            IApiClient> createClient;
        private readonly Func<
            IApiClient,
            ApiComposition,
            ApiEnvironmentId,
            SimultriaViewerModelInitializationResolver> createResolver;

        public SimultriaViewerModelInitializationCoordinator()
            : this(
                ApiClientFactory.Create,
                (client, composition, environment) =>
                    new SimultriaViewerModelInitializationResolver(
                        client,
                        composition,
                        environment))
        {
        }

        internal SimultriaViewerModelInitializationCoordinator(
            Func<ApiClientConfig, IApiAuthProvider, IApiClient> clientFactory,
            Func<
                IApiClient,
                ApiComposition,
                ApiEnvironmentId,
                SimultriaViewerModelInitializationResolver> resolverFactory)
        {
            createClient = clientFactory ??
                throw new ArgumentNullException(nameof(clientFactory));
            createResolver = resolverFactory ??
                throw new ArgumentNullException(nameof(resolverFactory));
        }

        /// <summary>
        /// Composes project-owned settings and creates one authenticated API
        /// client through the standard Deucarian API factory.
        /// </summary>
        public SimultriaViewerModelInitializationPlan Prepare(
            SimultriaViewerInitializationPayload payload,
            ApiConnectionSettings connectionSettings,
            ApiClientConfig clientConfig,
            IApiAuthProvider authProvider)
        {
            if (!TryValidatePayloadAndEnvironment(
                    payload,
                    out ApiEnvironmentId environmentId,
                    out SimultriaViewerModelInitializationPlan failure))
            {
                return failure;
            }

            if (connectionSettings == null)
            {
                return Failure(
                    "connection_settings_missing",
                    "Simultria API connection settings are required.");
            }

            ApiComposition composition;
            try
            {
                if (!SimultriaApiConnectionSettingsAdapter
                        .TryCreateComposition(
                            connectionSettings,
                            out composition,
                            out _))
                {
                    return Failure(
                        "api_composition_unavailable",
                        "The Simultria API connection settings could not be " +
                        "composed.");
                }
            }
            catch (Exception)
            {
                return Failure(
                    "api_composition_unavailable",
                    "The Simultria API connection settings could not be " +
                    "composed.");
            }

            if (!TryResolvePrimary(
                    composition,
                    environmentId,
                    out ApiResolvedClient primaryClient,
                    out failure) ||
                !TryRequireAuth(authProvider, out failure))
            {
                return failure;
            }

            IApiClient apiClient;
            try
            {
                apiClient = createClient(clientConfig, authProvider);
            }
            catch (Exception)
            {
                return Failure(
                    "api_client_creation_failed",
                    "The authenticated Simultria API client could not be " +
                    "created.");
            }

            return CreatePlan(
                payload,
                environmentId,
                primaryClient,
                composition,
                apiClient,
                authProvider);
        }

        /// <summary>
        /// Reuses a caller-owned composition and authenticated API client.
        /// Neither object is replaced or disposed by the returned plan.
        /// </summary>
        public SimultriaViewerModelInitializationPlan Prepare(
            SimultriaViewerInitializationPayload payload,
            ApiComposition composition,
            IApiClient apiClient,
            IApiAuthProvider authProvider)
        {
            if (!TryValidatePayloadAndEnvironment(
                    payload,
                    out ApiEnvironmentId environmentId,
                    out SimultriaViewerModelInitializationPlan failure) ||
                !TryResolvePrimary(
                    composition,
                    environmentId,
                    out ApiResolvedClient primaryClient,
                    out failure))
            {
                return failure;
            }

            return CreatePlan(
                payload,
                environmentId,
                primaryClient,
                composition,
                apiClient,
                authProvider);
        }

        /// <summary>
        /// Reuses the exact composition and client carried by the active
        /// Simultria runtime connection lease.
        /// </summary>
        public SimultriaViewerModelInitializationPlan Prepare(
            SimultriaViewerInitializationPayload payload,
            SimultriaViewerRuntimeConnectionContext runtimeConnection,
            IApiAuthProvider authProvider)
        {
            if (runtimeConnection == null ||
                !SimultriaViewerRuntimeConnectionContext.TryGetCurrent(
                    out SimultriaViewerRuntimeConnectionContext current) ||
                !ReferenceEquals(current, runtimeConnection))
            {
                return Failure(
                    "runtime_connection_unavailable",
                    "The active Simultria runtime connection is unavailable.");
            }

            if (!TryValidatePayloadAndEnvironment(
                    payload,
                    out ApiEnvironmentId environmentId,
                    out SimultriaViewerModelInitializationPlan failure))
            {
                return failure;
            }

            if (environmentId != runtimeConnection.EnvironmentId)
            {
                return Failure(
                    "environment_mismatch",
                    "The initialization environment does not match the " +
                    "active Simultria environment.");
            }

            return CreatePlan(
                payload,
                environmentId,
                runtimeConnection.PrimaryClient,
                runtimeConnection.Composition,
                runtimeConnection.ApiClient,
                authProvider,
                runtimeConnection);
        }

        private SimultriaViewerModelInitializationPlan CreatePlan(
            SimultriaViewerInitializationPayload payload,
            ApiEnvironmentId environmentId,
            ApiResolvedClient primaryClient,
            ApiComposition composition,
            IApiClient apiClient,
            IApiAuthProvider authProvider,
            SimultriaViewerRuntimeConnectionContext runtimeConnection = null)
        {
            if (!TryRequireAuth(
                    authProvider,
                    out SimultriaViewerModelInitializationPlan failure))
            {
                return failure;
            }

            if (apiClient == null)
            {
                return Failure(
                    "api_client_unavailable",
                    "The authenticated Simultria API client is unavailable.");
            }

            try
            {
                SimultriaViewerModelInitializationResolver resolver =
                    createResolver(
                        apiClient,
                        composition,
                        environmentId);
                if (resolver == null)
                {
                    return Failure(
                        "model_resolver_unavailable",
                        "The Simultria model resolver is unavailable.");
                }

                return SimultriaViewerModelInitializationPlan.Success(
                    payload,
                    environmentId,
                    primaryClient,
                    composition,
                    apiClient,
                    resolver,
                    runtimeConnection);
            }
            catch (Exception)
            {
                return Failure(
                    "model_resolver_unavailable",
                    "The Simultria model resolver is unavailable.");
            }
        }

        private static bool TryValidatePayloadAndEnvironment(
            SimultriaViewerInitializationPayload payload,
            out ApiEnvironmentId environmentId,
            out SimultriaViewerModelInitializationPlan failure)
        {
            environmentId = default;
            if (payload == null || !payload.IsValid(out _))
            {
                failure = Failure(
                    "invalid_payload",
                    "The Simultria viewer initialization payload is invalid.");
                return false;
            }

            string requestedValue = payload.EnvironmentId;
            bool hasRequested = !string.IsNullOrWhiteSpace(requestedValue);
            ApiEnvironmentId requested = default;
            if (hasRequested &&
                !ApiEnvironmentId.TryParse(requestedValue, out requested))
            {
                failure = Failure(
                    "environment_invalid",
                    "The initialization environment identifier is invalid.");
                return false;
            }

            if (SimultriaViewerRuntimeEnvironment.TryGetCurrent(
                    out SimultriaViewerEnvironmentResolution runtime))
            {
                environmentId = runtime.EnvironmentId;
                if (hasRequested && requested != environmentId)
                {
                    failure = Failure(
                        "environment_mismatch",
                        "The initialization environment does not match the " +
                        "active Simultria environment.");
                    return false;
                }

                failure = null;
                return true;
            }

            if (!hasRequested)
            {
                failure = Failure(
                    "environment_unresolved",
                    "The Simultria environment is unresolved.");
                return false;
            }

            environmentId = requested;
            failure = null;
            return true;
        }

        private static bool TryResolvePrimary(
            ApiComposition composition,
            ApiEnvironmentId environmentId,
            out ApiResolvedClient primaryClient,
            out SimultriaViewerModelInitializationPlan failure)
        {
            primaryClient = null;
            if (composition == null)
            {
                failure = Failure(
                    "api_composition_unavailable",
                    "The Simultria API composition is unavailable.");
                return false;
            }

            ApiEnvironmentStatus status;
            try
            {
                status = composition.GetEnvironmentStatus(environmentId);
            }
            catch (Exception)
            {
                status = null;
            }

            if (status?.IsResolved != true)
            {
                failure = Failure(
                    "environment_not_configured",
                    "The selected Simultria environment is not configured.");
                return false;
            }

            try
            {
                if (!composition.TryResolveClient(
                        environmentId,
                        SimultriaClientIds.Primary,
                        out primaryClient,
                        out _))
                {
                    failure = Failure(
                        "primary_client_unavailable",
                        "The selected Simultria environment has no resolved " +
                        "primary API client.");
                    return false;
                }
            }
            catch (Exception)
            {
                failure = Failure(
                    "primary_client_unavailable",
                    "The selected Simultria environment has no resolved " +
                    "primary API client.");
                return false;
            }

            failure = null;
            return true;
        }

        private static bool TryRequireAuth(
            IApiAuthProvider authProvider,
            out SimultriaViewerModelInitializationPlan failure)
        {
            failure = authProvider == null
                ? Failure(
                    "authentication_unavailable",
                    "An authenticated Simultria API provider is required.")
                : null;
            return failure == null;
        }

        private static SimultriaViewerModelInitializationPlan Failure(
            string errorCode,
            string message) =>
            SimultriaViewerModelInitializationPlan.Failure(
                errorCode,
                message);
    }
}
