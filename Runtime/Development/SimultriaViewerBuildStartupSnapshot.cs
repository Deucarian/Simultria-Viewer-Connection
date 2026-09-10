using System;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;

namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>Connection-gate phases, not engine or model readiness.</summary>
    public enum SimultriaViewerBuildStartupPhase
    {
        NotStarted = 0,
        Resolving = 1,
        Routed = 2,
        Fallback = 3,
        Failed = 4,
        Disposed = 5
    }

    /// <summary>Bounded public failure codes without diagnostic payloads.</summary>
    public enum SimultriaViewerBuildStartupFailureCode
    {
        None = 0,
        EnvironmentResolutionFailed = 1,
        ProviderCreationFailed = 2,
        ProviderRegistrationFailed = 3,
        EnvironmentActivationFailed = 4,
        StartupCancelled = 5
    }

    /// <summary>
    /// Immutable, credential-free connection-gate status for explicitly bound
    /// observers. This does not grant permission to start or change routing.
    /// </summary>
    public sealed class SimultriaViewerBuildStartupSnapshot
    {
        /// <summary>
        /// Creates a bounded status value. Only Routed/Fallback retain a known
        /// built-in environment; custom identifiers are never published.
        /// Failed requires a non-None code; every other phase requires None.
        /// </summary>
        public SimultriaViewerBuildStartupSnapshot(
            SimultriaViewerBuildStartupPhase phase,
            SimultriaViewerBuildStartupFailureCode failureCode =
                SimultriaViewerBuildStartupFailureCode.None,
            ApiEnvironmentId environmentId = default(ApiEnvironmentId))
        {
            if (phase < SimultriaViewerBuildStartupPhase.NotStarted ||
                phase > SimultriaViewerBuildStartupPhase.Disposed)
            {
                throw new ArgumentOutOfRangeException(nameof(phase));
            }

            if (failureCode < SimultriaViewerBuildStartupFailureCode.None ||
                failureCode > SimultriaViewerBuildStartupFailureCode.StartupCancelled ||
                (phase == SimultriaViewerBuildStartupPhase.Failed) ==
                (failureCode == SimultriaViewerBuildStartupFailureCode.None))
            {
                throw new ArgumentOutOfRangeException(nameof(failureCode));
            }

            Phase = phase;
            FailureCode = failureCode;
            EnvironmentId = phase == SimultriaViewerBuildStartupPhase.Routed ||
                            phase == SimultriaViewerBuildStartupPhase.Fallback
                ? SafeEnvironment(environmentId)
                : default(ApiEnvironmentId);
        }

        public SimultriaViewerBuildStartupPhase Phase { get; }
        public SimultriaViewerBuildStartupFailureCode FailureCode { get; }
        public ApiEnvironmentId EnvironmentId { get; }

        private static ApiEnvironmentId SafeEnvironment(ApiEnvironmentId value)
        {
            if (value == SimultriaEnvironmentIds.Local)
                return SimultriaEnvironmentIds.Local;
            if (value == SimultriaEnvironmentIds.Development)
                return SimultriaEnvironmentIds.Development;
            if (value == SimultriaEnvironmentIds.Testing)
                return SimultriaEnvironmentIds.Testing;
            if (value == SimultriaEnvironmentIds.Acceptance)
                return SimultriaEnvironmentIds.Acceptance;
            if (value == SimultriaEnvironmentIds.Production)
                return SimultriaEnvironmentIds.Production;
            return default(ApiEnvironmentId);
        }
    }
}
