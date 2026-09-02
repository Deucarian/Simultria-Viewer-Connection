using System;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.API.Core;
using Deucarian.API.Models;

namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>
    /// A validated, immutable-environment model-initialization composition.
    /// It retains the exact API objects selected by the coordinator and
    /// delegates model lookup to the package's canonical resolver.
    /// </summary>
    public sealed class SimultriaViewerModelInitializationPlan
    {
        private readonly SimultriaViewerInitializationPayload payload;
        private readonly SimultriaViewerRuntimeConnectionContext
            runtimeConnection;

        private SimultriaViewerModelInitializationPlan(
            bool succeeded,
            string errorCode,
            string message,
            SimultriaViewerInitializationPayload initializationPayload,
            ApiEnvironmentId environmentId,
            ApiResolvedClient primaryClient,
            ApiComposition composition,
            IApiClient apiClient,
            SimultriaViewerModelInitializationResolver resolver,
            SimultriaViewerRuntimeConnectionContext owningRuntimeConnection)
        {
            Succeeded = succeeded;
            ErrorCode = errorCode ?? string.Empty;
            Message = message ?? string.Empty;
            payload = ClonePayload(initializationPayload);
            EnvironmentId = environmentId;
            PrimaryClient = primaryClient;
            Composition = composition;
            ApiClient = apiClient;
            Resolver = resolver;
            runtimeConnection = owningRuntimeConnection;
        }

        public bool Succeeded { get; }

        public string ErrorCode { get; }

        public string Message { get; }

        public ApiEnvironmentId EnvironmentId { get; }

        public ApiResolvedClient PrimaryClient { get; }

        public ApiComposition Composition { get; }

        public IApiClient ApiClient { get; }

        public SimultriaViewerModelInitializationResolver Resolver { get; }

        /// <summary>
        /// Resolves the model through the canonical resolver selected by this
        /// plan. Call only after checking <see cref="Succeeded"/>.
        /// </summary>
        public async Task<SimultriaViewerModelInitializationResolution>
            ResolveAsync(
                CancellationToken cancellationToken = default)
        {
            if (!Succeeded || Resolver == null || payload == null)
            {
                return SimultriaViewerModelInitializationResolution.Failure(
                    string.IsNullOrWhiteSpace(ErrorCode)
                        ? "model_initialization_unavailable"
                        : ErrorCode,
                    string.IsNullOrWhiteSpace(Message)
                        ? "Simultria model initialization is unavailable."
                        : Message);
            }

            if (!TryValidateRuntimeConnection(out
                    SimultriaViewerModelInitializationResolution
                        runtimeFailure))
            {
                return runtimeFailure;
            }

            try
            {
                SimultriaViewerModelInitializationResolution resolution =
                    await Resolver.ResolveAsync(
                        payload,
                        cancellationToken);
                return TryValidateRuntimeConnection(out runtimeFailure)
                    ? resolution
                    : runtimeFailure;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return SimultriaViewerModelInitializationResolution.Failure(
                    "model_resolution_failed",
                    "The Simultria model source could not be resolved.");
            }
        }

        internal static SimultriaViewerModelInitializationPlan Success(
            SimultriaViewerInitializationPayload payload,
            ApiEnvironmentId environmentId,
            ApiResolvedClient primaryClient,
            ApiComposition composition,
            IApiClient apiClient,
            SimultriaViewerModelInitializationResolver resolver,
            SimultriaViewerRuntimeConnectionContext runtimeConnection = null) =>
            new SimultriaViewerModelInitializationPlan(
                true,
                string.Empty,
                string.Empty,
                payload,
                environmentId,
                primaryClient,
                composition,
                apiClient,
                resolver,
                runtimeConnection);

        internal static SimultriaViewerModelInitializationPlan Failure(
            string errorCode,
            string message) =>
            new SimultriaViewerModelInitializationPlan(
                false,
                errorCode,
                message,
                null,
                default,
                null,
                null,
                null,
                null,
                null);

        internal SimultriaViewerInitializationPayload CreatePayloadSnapshot()
        {
            return ClonePayload(payload);
        }

        internal bool TryValidateRuntimeConnection(
            out SimultriaViewerModelInitializationResolution failure)
        {
            failure = null;
            if (runtimeConnection == null)
            {
                return true;
            }

            if (SimultriaViewerRuntimeConnectionContext.TryGetCurrent(
                    out SimultriaViewerRuntimeConnectionContext current) &&
                ReferenceEquals(current, runtimeConnection))
            {
                return true;
            }

            failure = SimultriaViewerModelInitializationResolution.Failure(
                "runtime_connection_unavailable",
                "The active Simultria runtime connection is unavailable.");
            return false;
        }

        private static SimultriaViewerInitializationPayload ClonePayload(
            SimultriaViewerInitializationPayload source)
        {
            if (source == null)
            {
                return null;
            }

            return new SimultriaViewerInitializationPayload
            {
                Revision = source.Revision,
                EnvironmentId = source.EnvironmentId,
                ProjectId = source.ProjectId,
                ModelId = source.ModelId,
                ModelVersionId = source.ModelVersionId,
                ModelUrl = source.ModelUrl,
                ModelVersion = source.ModelVersion,
                Placement = ClonePlacement(source.Placement),
                ForceShowLoadedModelObjects =
                    source.ForceShowLoadedModelObjects,
                Metadata = source.Metadata?.DeepClone()
            };
        }

        private static SimultriaViewerPlacementAlignment ClonePlacement(
            SimultriaViewerPlacementAlignment source)
        {
            return source == null
                ? null
                : new SimultriaViewerPlacementAlignment(
                    CloneVector(source.Position),
                    CloneVector(source.RotationEuler),
                    CloneVector(source.Scale));
        }

        private static SimultriaViewerVector3 CloneVector(
            SimultriaViewerVector3 source)
        {
            return source == null
                ? null
                : new SimultriaViewerVector3(
                    source.X,
                    source.Y,
                    source.Z);
        }
    }
}
