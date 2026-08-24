using Deucarian.API.Models;

namespace Deucarian.SimultriaViewerConnection
{
    /// <summary>How a viewer chooses its effective Simultria environment.</summary>
    public enum SimultriaViewerEnvironmentResolutionMode
    {
        /// <summary>Use the environment selected directly on the profile.</summary>
        Manual = 0,

        /// <summary>
        /// Ask the Simultria Unity build directory which environment owns the
        /// current application version and product.
        /// </summary>
        AutomaticFromUnityBuildVersion = 1
    }

    /// <summary>
    /// Supplies credential-free Unity build metadata without coupling tests
    /// or integrations directly to <c>Application.version</c>.
    /// </summary>
    public interface ISimultriaViewerBuildMetadataProvider
    {
        string BuildVersion { get; }
    }

    /// <summary>Sanitized outcome of effective-environment resolution.</summary>
    public sealed class SimultriaViewerEnvironmentResolution
    {
        private SimultriaViewerEnvironmentResolution(
            bool succeeded,
            SimultriaViewerEnvironmentResolutionMode mode,
            ApiEnvironmentId environmentId,
            string buildVersion,
            string product,
            string source,
            string errorCode,
            string message)
        {
            Succeeded = succeeded;
            Mode = mode;
            EnvironmentId = environmentId;
            BuildVersion = buildVersion ?? string.Empty;
            Product = product ?? string.Empty;
            Source = source ?? string.Empty;
            ErrorCode = errorCode ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; }

        public SimultriaViewerEnvironmentResolutionMode Mode { get; }

        public ApiEnvironmentId EnvironmentId { get; }

        public string BuildVersion { get; }

        public string Product { get; }

        public string Source { get; }

        public string ErrorCode { get; }

        public string Message { get; }

        internal static SimultriaViewerEnvironmentResolution Success(
            SimultriaViewerEnvironmentResolutionMode mode,
            ApiEnvironmentId environmentId,
            string buildVersion,
            string product,
            string source)
        {
            return new SimultriaViewerEnvironmentResolution(
                true,
                mode,
                environmentId,
                buildVersion,
                product,
                source,
                null,
                null);
        }

        internal static SimultriaViewerEnvironmentResolution Failure(
            SimultriaViewerEnvironmentResolutionMode mode,
            string buildVersion,
            string product,
            string errorCode,
            string message)
        {
            return new SimultriaViewerEnvironmentResolution(
                false,
                mode,
                default(ApiEnvironmentId),
                buildVersion,
                product,
                mode == SimultriaViewerEnvironmentResolutionMode.Manual
                    ? "Manual profile selection"
                    : "Simultria Unity build directory",
                errorCode,
                message);
        }
    }
}
