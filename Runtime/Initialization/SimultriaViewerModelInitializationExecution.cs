using System;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.API.Authentication;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Newtonsoft.Json.Linq;

namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>
    /// Pinned inputs supplied to a product's application-initialization
    /// delegate after the canonical Simultria model has been resolved.
    /// </summary>
    public sealed class SimultriaViewerModelInitializationExecutionContext
    {
        internal SimultriaViewerModelInitializationExecutionContext(
            SimultriaViewerInitializationPayload initializationPayload,
            ApiEnvironmentId environmentId,
            ApiResolvedClient primaryClient,
            ApiComposition composition,
            IApiClient apiClient,
            SimultriaViewerModelInitializationResolution resolution)
        {
            InitializationPayload = initializationPayload;
            EnvironmentId = environmentId;
            PrimaryClient = primaryClient;
            Composition = composition;
            ApiClient = apiClient;
            Resolution = resolution;
        }

        /// <summary>
        /// An invocation-owned snapshot of the validated initialization
        /// payload. Mutating it cannot change the coordinator's stored plan.
        /// </summary>
        public SimultriaViewerInitializationPayload InitializationPayload
        {
            get;
        }

        public ApiEnvironmentId EnvironmentId { get; }
        public ApiResolvedClient PrimaryClient { get; }
        public ApiComposition Composition { get; }
        public IApiClient ApiClient { get; }
        public SimultriaViewerModelInitializationResolution Resolution
        {
            get;
        }
    }

    /// <summary>
    /// Transport-neutral outcome returned by a product after it applies the
    /// resolved model to its viewer application.
    /// </summary>
    public sealed class SimultriaViewerApplicationInitializationResult
    {
        private SimultriaViewerApplicationInitializationResult(
            bool succeeded,
            string errorCode,
            string message,
            JToken payload)
        {
            Succeeded = succeeded;
            ErrorCode = errorCode ?? string.Empty;
            Message = message ?? string.Empty;
            Payload = payload?.DeepClone();
        }

        public bool Succeeded { get; }
        public string ErrorCode { get; }
        public string Message { get; }
        public JToken Payload { get; }

        public static SimultriaViewerApplicationInitializationResult Success(
            JToken payload = null) =>
            new SimultriaViewerApplicationInitializationResult(
                true,
                string.Empty,
                string.Empty,
                payload);

        public static SimultriaViewerApplicationInitializationResult Failure(
            string errorCode,
            string message,
            JToken payload = null) =>
            new SimultriaViewerApplicationInitializationResult(
                false,
                errorCode,
                message,
                payload);
    }

    /// <summary>
    /// Complete package-owned resolve-and-initialize outcome. Successful
    /// payloads include the canonical environment and resolved model fields.
    /// </summary>
    public sealed class SimultriaViewerModelInitializationExecutionResult
    {
        private SimultriaViewerModelInitializationExecutionResult(
            bool succeeded,
            string errorCode,
            string message,
            JToken payload,
            ApiEnvironmentId environmentId,
            ApiResolvedClient primaryClient,
            SimultriaViewerModelInitializationResolution resolution)
        {
            Succeeded = succeeded;
            ErrorCode = errorCode ?? string.Empty;
            Message = message ?? string.Empty;
            Payload = payload?.DeepClone();
            EnvironmentId = environmentId;
            PrimaryClient = primaryClient;
            Resolution = resolution;
        }

        public bool Succeeded { get; }
        public string ErrorCode { get; }
        public string Message { get; }
        public JToken Payload { get; }
        public ApiEnvironmentId EnvironmentId { get; }
        public ApiResolvedClient PrimaryClient { get; }
        public SimultriaViewerModelInitializationResolution Resolution
        {
            get;
        }

        internal static SimultriaViewerModelInitializationExecutionResult
            Success(
                JObject payload,
                SimultriaViewerModelInitializationPlan plan,
                SimultriaViewerModelInitializationResolution resolution) =>
            new SimultriaViewerModelInitializationExecutionResult(
                true,
                string.Empty,
                string.Empty,
                payload,
                plan.EnvironmentId,
                plan.PrimaryClient,
                resolution);

        internal static SimultriaViewerModelInitializationExecutionResult
            Failure(
                string errorCode,
                string message,
                JToken payload = null,
                SimultriaViewerModelInitializationPlan plan = null,
                SimultriaViewerModelInitializationResolution resolution =
                    null) =>
            new SimultriaViewerModelInitializationExecutionResult(
                false,
                string.IsNullOrWhiteSpace(errorCode)
                    ? "model_initialization_failed"
                    : errorCode,
                string.IsNullOrWhiteSpace(message)
                    ? "Simultria viewer initialization failed."
                    : message,
                payload,
                plan?.EnvironmentId ?? default,
                plan?.PrimaryClient,
                resolution);
    }

    public sealed partial class SimultriaViewerModelInitializationCoordinator
    {
        /// <summary>
        /// Runs the common viewer initialization sequence without depending on
        /// a product application or transport: validates freshness, selects the
        /// active lease (or an explicitly allowed unleased test composition),
        /// resolves the model, invokes the product delegate, and assembles the
        /// canonical success payload.
        /// </summary>
        public async Task<SimultriaViewerModelInitializationExecutionResult>
            ExecuteAsync(
                SimultriaViewerInitializationPayload payload,
                long latestRevision,
                ApiConnectionSettings connectionSettings,
                ApiClientConfig clientConfig,
                IApiAuthProvider authProvider,
                bool allowUnleasedSettingsFallback,
                Func<
                    SimultriaViewerModelInitializationExecutionContext,
                    CancellationToken,
                    Task<SimultriaViewerApplicationInitializationResult>>
                    initializeApplication,
                CancellationToken cancellationToken = default)
        {
            if (payload == null)
            {
                return ExecutionFailure(
                    "invalid_payload",
                    "The Simultria viewer initialization payload is invalid.");
            }

            if (payload.Revision <= latestRevision)
            {
                return ExecutionFailure(
                    "stale_revision",
                    "The initialization revision is stale.");
            }

            if (connectionSettings == null)
            {
                return ExecutionFailure(
                    "connection_settings_missing",
                    "Simultria API connection settings are required.");
            }

            if (initializeApplication == null)
            {
                return ExecutionFailure(
                    "application_initialization_unavailable",
                    "The viewer application initializer is unavailable.");
            }

            SimultriaViewerModelInitializationPlan plan = PrepareCurrent(
                payload,
                connectionSettings,
                clientConfig,
                authProvider,
                allowUnleasedSettingsFallback);
            if (plan == null || !plan.Succeeded)
            {
                return ExecutionFailure(
                    plan?.ErrorCode,
                    plan?.Message);
            }

            SimultriaViewerModelInitializationResolution resolution =
                await plan.ResolveAsync(cancellationToken);
            if (resolution == null || !resolution.Succeeded)
            {
                return SimultriaViewerModelInitializationExecutionResult
                    .Failure(
                        resolution?.ErrorCode ?? "model_resolution_failed",
                        resolution?.Message ??
                        "The Simultria model source could not be resolved.",
                        plan: plan,
                        resolution: resolution);
            }

            SimultriaViewerApplicationInitializationResult applicationResult;
            try
            {
                var context =
                    new SimultriaViewerModelInitializationExecutionContext(
                        plan.CreatePayloadSnapshot(),
                        plan.EnvironmentId,
                        plan.PrimaryClient,
                        plan.Composition,
                        plan.ApiClient,
                        resolution);
                if (!plan.TryValidateRuntimeConnection(out
                        SimultriaViewerModelInitializationResolution
                            runtimeFailure))
                {
                    return SimultriaViewerModelInitializationExecutionResult
                        .Failure(
                            runtimeFailure.ErrorCode,
                            runtimeFailure.Message,
                            plan: plan,
                            resolution: runtimeFailure);
                }

                applicationResult = await initializeApplication(
                    context,
                    cancellationToken);

                if (!plan.TryValidateRuntimeConnection(out
                        SimultriaViewerModelInitializationResolution
                            runtimeFailureAfterInitialization))
                {
                    return SimultriaViewerModelInitializationExecutionResult
                        .Failure(
                            runtimeFailureAfterInitialization.ErrorCode,
                            runtimeFailureAfterInitialization.Message,
                            plan: plan,
                            resolution:
                                runtimeFailureAfterInitialization);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return SimultriaViewerModelInitializationExecutionResult
                    .Failure(
                        "application_initialization_failed",
                        "The viewer application could not be initialized.",
                        plan: plan,
                        resolution: resolution);
            }

            if (applicationResult?.Succeeded != true)
            {
                return SimultriaViewerModelInitializationExecutionResult
                    .Failure(
                        applicationResult?.ErrorCode ??
                        "application_initialization_failed",
                        applicationResult?.Message ??
                        "The viewer application could not be initialized.",
                        applicationResult?.Payload,
                        plan,
                        resolution);
            }

            JObject response = applicationResult.Payload is JObject objectValue
                ? (JObject)objectValue.DeepClone()
                : new JObject();
            response["environment_id"] = plan.EnvironmentId.Value;
            response["project_id"] = resolution.ProjectId;
            response["model_id"] = resolution.ModelId;
            response["model_version_id"] = resolution.ModelVersionId;
            response["used_requested_model_version"] =
                resolution.UsedRequestedVersion;
            return SimultriaViewerModelInitializationExecutionResult.Success(
                response,
                plan,
                resolution);
        }

        private SimultriaViewerModelInitializationPlan PrepareCurrent(
            SimultriaViewerInitializationPayload payload,
            ApiConnectionSettings connectionSettings,
            ApiClientConfig clientConfig,
            IApiAuthProvider authProvider,
            bool allowUnleasedSettingsFallback)
        {
            if (SimultriaViewerRuntimeConnectionContext.TryGetCurrent(
                    out SimultriaViewerRuntimeConnectionContext runtime))
            {
                return Prepare(payload, runtime, authProvider);
            }

            if (SimultriaViewerRuntimeEnvironment.TryGetCurrent(out _))
            {
                return Failure(
                    "runtime_connection_unavailable",
                    "The active Simultria runtime connection is unavailable.");
            }

            if (!allowUnleasedSettingsFallback)
            {
                return Failure(
                    "environment_unresolved",
                    "The Simultria environment is unresolved.");
            }

            return Prepare(
                payload,
                connectionSettings,
                clientConfig,
                authProvider);
        }

        private static SimultriaViewerModelInitializationExecutionResult
            ExecutionFailure(
                string errorCode,
                string message) =>
            SimultriaViewerModelInitializationExecutionResult.Failure(
                errorCode,
                message);
    }
}
