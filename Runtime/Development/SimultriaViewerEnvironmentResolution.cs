using Deucarian.API.Models;

namespace Deucarian.SimultriaViewerConnection
{
    public enum SimultriaViewerRuntimeKind
    {
        Editor = 0,
        Build = 1
    }

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

    public interface ISimultriaViewerRuntimeContext
    {
        bool IsEditor { get; }

        string ApplicationName { get; }
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
            SimultriaViewerRuntimeKind runtimeKind,
            string applicationName,
            bool editorOverrideActive,
            string errorCode,
            string message)
        {
            Succeeded = succeeded;
            Mode = mode;
            EnvironmentId = environmentId;
            BuildVersion = buildVersion ?? string.Empty;
            Product = product ?? string.Empty;
            Source = source ?? string.Empty;
            RuntimeKind = runtimeKind;
            ApplicationName = applicationName ?? string.Empty;
            EditorOverrideActive = editorOverrideActive;
            ErrorCode = errorCode ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; }

        public SimultriaViewerEnvironmentResolutionMode Mode { get; }

        public ApiEnvironmentId EnvironmentId { get; }

        public string BuildVersion { get; }

        public string Product { get; }

        public string Source { get; }

        public SimultriaViewerRuntimeKind RuntimeKind { get; }

        public string ApplicationName { get; }

        public bool EditorOverrideActive { get; }

        public string ErrorCode { get; }

        public string Message { get; }

        internal static SimultriaViewerEnvironmentResolution Success(
            SimultriaViewerEnvironmentResolutionMode mode,
            ApiEnvironmentId environmentId,
            string buildVersion,
            string product,
            string source,
            SimultriaViewerRuntimeKind runtimeKind,
            string applicationName,
            bool editorOverrideActive)
        {
            return new SimultriaViewerEnvironmentResolution(
                true,
                mode,
                environmentId,
                buildVersion,
                product,
                source,
                runtimeKind,
                applicationName,
                editorOverrideActive,
                null,
                null);
        }

        internal static SimultriaViewerEnvironmentResolution Failure(
            SimultriaViewerEnvironmentResolutionMode mode,
            string buildVersion,
            string product,
            string errorCode,
            string message,
            SimultriaViewerRuntimeKind runtimeKind,
            string applicationName,
            bool editorOverrideActive)
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
                runtimeKind,
                applicationName,
                editorOverrideActive,
                errorCode,
                message);
        }

        public string ToDiagnosticString()
        {
            return "application=" + ApplicationName +
                   " build_version=" + BuildVersion +
                   " product=" + Product +
                   " environment=" +
                   (Succeeded ? EnvironmentId.Value : "unresolved") +
                   " runtime=" + RuntimeKind +
                   " editor_override=" + EditorOverrideActive +
                   " source=" + Source +
                   (string.IsNullOrWhiteSpace(ErrorCode)
                       ? string.Empty
                       : " error_code=" + ErrorCode);
        }
    }
}
