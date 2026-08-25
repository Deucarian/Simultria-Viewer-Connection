using System;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Deucarian.Simultria.API.Models;
using Deucarian.Simultria.API.Services;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection
{
    /// <summary>Reads immutable identity from the running Unity application.</summary>
    public sealed class SimultriaViewerApplicationBuildMetadataProvider :
        ISimultriaViewerBuildMetadataProvider,
        ISimultriaViewerRuntimeContext
    {
        public string BuildVersion => Application.version;

        public bool IsEditor => Application.isEditor;

        public string ApplicationName => Application.productName;
    }

    /// <summary>
    /// Resolves one effective environment without storing deployment hosts,
    /// credentials, or a second environment mapping in the viewer package.
    /// Editor profiles may select an explicit environment. Player builds are
    /// always resolved from Application.version and the build configuration.
    /// </summary>
    public sealed class SimultriaViewerEnvironmentResolver
    {
        private readonly IApiClient apiClient;
        private readonly ISimultriaViewerBuildMetadataProvider metadataProvider;
        private readonly ISimultriaViewerRuntimeContext runtimeContext;

        public SimultriaViewerEnvironmentResolver(
            IApiClient apiClient,
            ISimultriaViewerBuildMetadataProvider metadataProvider)
            : this(
                apiClient,
                metadataProvider,
                metadataProvider as ISimultriaViewerRuntimeContext ??
                new SimultriaViewerApplicationBuildMetadataProvider())
        {
        }

        public SimultriaViewerEnvironmentResolver(
            IApiClient apiClient,
            ISimultriaViewerBuildMetadataProvider metadataProvider,
            ISimultriaViewerRuntimeContext runtimeContext)
        {
            this.apiClient = apiClient;
            this.metadataProvider = metadataProvider ??
                throw new ArgumentNullException(nameof(metadataProvider));
            this.runtimeContext = runtimeContext ??
                throw new ArgumentNullException(nameof(runtimeContext));
        }

        /// <summary>Creates the normal Unity-backed resolver.</summary>
        public static SimultriaViewerEnvironmentResolver CreateDefault()
        {
            var application =
                new SimultriaViewerApplicationBuildMetadataProvider();
            return new SimultriaViewerEnvironmentResolver(
                ApiClientFactory.CreateDefault(),
                application,
                application);
        }

        /// <summary>
        /// Returns the generic connection paired with the same runtime input
        /// used by ResolveForCurrentRuntimeAsync. Player
        /// builds can only receive the build configuration connection.
        /// </summary>
        public bool TryResolveConnectionProfileForCurrentRuntime(
            SimultriaViewerBuildConfiguration buildConfiguration,
            out Deucarian.API.Configuration.ApiConnectionProfile
                connectionProfile,
            out string error)
        {
#if UNITY_EDITOR
            if (runtimeContext.IsEditor)
            {
                if (!SimultriaViewerEditorProfileProvider.TryResolve(
                        out SimultriaViewerDevelopmentProfile editorProfile,
                        out _,
                        out error))
                {
                    connectionProfile = null;
                    return false;
                }

                connectionProfile = editorProfile.ConnectionProfileReference;
                if (connectionProfile == null)
                {
                    error = "The selected Editor override must reference a " +
                            "generic API connection profile.";
                    return false;
                }

                error = string.Empty;
                return true;
            }
#endif

            connectionProfile = buildConfiguration?.ConnectionProfile;
            error = connectionProfile == null
                ? "The player build configuration has no API connection " +
                  "profile."
                : string.Empty;
            return connectionProfile != null;
        }

        /// <summary>
        /// Resolves the project/user Editor profile in the Editor and the
        /// immutable build configuration in an actual player build.
        /// </summary>
        public Task<SimultriaViewerEnvironmentResolution>
            ResolveForCurrentRuntimeAsync(
                SimultriaViewerBuildConfiguration buildConfiguration,
                CancellationToken cancellationToken =
                    default(CancellationToken))
        {
#if UNITY_EDITOR
            if (runtimeContext.IsEditor)
            {
                return ResolveForCurrentRuntimeAsync(
                    buildConfiguration,
                    null,
                    cancellationToken);
            }
#endif
            return ResolveBuildConfigurationAsync(
                buildConfiguration,
                cancellationToken);
        }

#if UNITY_EDITOR
        /// <summary>
        /// The explicit Editor profile parameter exists for tests and custom
        /// Editor hosts. It is never read when the runtime is a player build.
        /// </summary>
        public Task<SimultriaViewerEnvironmentResolution>
            ResolveForCurrentRuntimeAsync(
                SimultriaViewerBuildConfiguration buildConfiguration,
                SimultriaViewerDevelopmentProfile editorProfile,
                CancellationToken cancellationToken =
                    default(CancellationToken))
        {
            if (!runtimeContext.IsEditor)
            {
                return ResolveBuildConfigurationAsync(
                    buildConfiguration,
                    cancellationToken);
            }

            string source = "Explicit Editor override";
            if (editorProfile == null &&
                !SimultriaViewerEditorProfileProvider.TryResolve(
                    out editorProfile,
                    out source,
                    out string providerError))
            {
                return Task.FromResult(Failure(
                    SimultriaViewerEnvironmentResolutionMode.Manual,
                    CurrentBuildVersion(),
                    buildConfiguration?.Product,
                    "editor_profile_unavailable",
                    providerError,
                    true));
            }

            return ResolveEditorProfileAsync(
                editorProfile,
                source,
                cancellationToken);
        }

        /// <summary>
        /// Backward-compatible Editor profile entry point. This API and the
        /// development-profile type are absent from player compilation.
        /// </summary>
        public Task<SimultriaViewerEnvironmentResolution> ResolveAsync(
            SimultriaViewerDevelopmentProfile profile,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (runtimeContext.IsEditor)
            {
                return ResolveEditorProfileAsync(
                    profile,
                    "Explicit Editor override",
                    cancellationToken);
            }

            if (profile == null)
            {
                return Task.FromResult(Failure(
                    SimultriaViewerEnvironmentResolutionMode
                        .AutomaticFromUnityBuildVersion,
                    CurrentBuildVersion(),
                    null,
                    "profile_missing",
                    "A Simultria viewer profile is required for the build " +
                    "directory lookup.",
                    false));
            }

            if (!profile.TryCreateComposition(
                    out ApiComposition composition,
                    out string compositionError))
            {
                return Task.FromResult(Failure(
                    SimultriaViewerEnvironmentResolutionMode
                        .AutomaticFromUnityBuildVersion,
                    CurrentBuildVersion(),
                    profile.BuildProduct,
                    "api_composition_unavailable",
                    compositionError,
                    false));
            }

            return ResolveAutomaticAsync(
                composition,
                profile.BuildDirectoryEnvironmentId,
                CurrentBuildVersion(),
                profile.BuildProduct,
                false,
                cancellationToken);
        }
#endif

        private Task<SimultriaViewerEnvironmentResolution>
            ResolveBuildConfigurationAsync(
                SimultriaViewerBuildConfiguration configuration,
                CancellationToken cancellationToken)
        {
            if (configuration == null)
            {
                return Task.FromResult(Failure(
                    SimultriaViewerEnvironmentResolutionMode
                        .AutomaticFromUnityBuildVersion,
                    CurrentBuildVersion(),
                    null,
                    "build_configuration_missing",
                    "A Simultria viewer build configuration is required.",
                    false));
            }

            if (!configuration.TryCreateComposition(
                    out ApiComposition composition,
                    out string compositionError))
            {
                return Task.FromResult(Failure(
                    SimultriaViewerEnvironmentResolutionMode
                        .AutomaticFromUnityBuildVersion,
                    CurrentBuildVersion(),
                    configuration.Product,
                    "api_composition_unavailable",
                    compositionError,
                    false));
            }

            return ResolveAutomaticAsync(
                composition,
                configuration.BuildDirectoryEnvironmentId,
                CurrentBuildVersion(),
                configuration.Product,
                false,
                cancellationToken);
        }

#if UNITY_EDITOR
        private Task<SimultriaViewerEnvironmentResolution>
            ResolveEditorProfileAsync(
                SimultriaViewerDevelopmentProfile profile,
                string source,
                CancellationToken cancellationToken)
        {
            if (profile == null)
            {
                return Task.FromResult(Failure(
                    SimultriaViewerEnvironmentResolutionMode.Manual,
                    CurrentBuildVersion(),
                    null,
                    "profile_missing",
                    "A Simultria viewer development profile is required.",
                    true));
            }

            SimultriaViewerEnvironmentResolutionMode mode =
                profile.EnvironmentResolutionMode;
            if (!profile.TryCreateComposition(
                    out ApiComposition composition,
                    out string compositionError))
            {
                return Task.FromResult(Failure(
                    mode,
                    ResolveEditorBuildVersion(profile),
                    profile.BuildProduct,
                    "api_composition_unavailable",
                    compositionError,
                    mode == SimultriaViewerEnvironmentResolutionMode.Manual));
            }

            if (mode == SimultriaViewerEnvironmentResolutionMode.Manual)
            {
                ApiEnvironmentId environment = profile.EnvironmentId;
                ApiEnvironmentStatus status =
                    composition.GetEnvironmentStatus(environment);
                return Task.FromResult(status.IsResolved
                    ? SimultriaViewerEnvironmentResolution.Success(
                        mode,
                        environment,
                        CurrentBuildVersion(),
                        profile.BuildProduct.Trim(),
                        string.IsNullOrWhiteSpace(source)
                            ? "Editor environment override"
                            : source,
                        SimultriaViewerRuntimeKind.Editor,
                        ApplicationName(),
                        true)
                    : Failure(
                        mode,
                        CurrentBuildVersion(),
                        profile.BuildProduct,
                        "manual_environment_unavailable",
                        status.Message,
                        true));
            }

            return ResolveAutomaticAsync(
                composition,
                profile.BuildDirectoryEnvironmentId,
                ResolveEditorBuildVersion(profile),
                profile.BuildProduct,
                false,
                cancellationToken);
        }
#endif

        private async Task<SimultriaViewerEnvironmentResolution>
            ResolveAutomaticAsync(
                ApiComposition composition,
                ApiEnvironmentId directoryEnvironment,
                string buildVersion,
                string productValue,
                bool editorOverrideActive,
                CancellationToken cancellationToken)
        {
            const SimultriaViewerEnvironmentResolutionMode mode =
                SimultriaViewerEnvironmentResolutionMode
                    .AutomaticFromUnityBuildVersion;
            string product = (productValue ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(buildVersion))
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_version_missing",
                    "Automatic environment resolution requires a Unity " +
                    "build version.",
                    editorOverrideActive);
            }

            if (string.IsNullOrWhiteSpace(product))
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_product_missing",
                    "Automatic environment resolution requires a canonical " +
                    "Simultria build product.",
                    editorOverrideActive);
            }

            if (directoryEnvironment.IsEmpty)
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_directory_environment_missing",
                    "Choose the configured API environment that hosts the " +
                    "Simultria Unity build directory.",
                    editorOverrideActive);
            }

            ApiEnvironmentStatus directoryStatus =
                composition.GetEnvironmentStatus(directoryEnvironment);
            if (!directoryStatus.IsResolved)
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_directory_environment_unavailable",
                    directoryStatus.Message,
                    editorOverrideActive);
            }

            if (apiClient == null)
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_directory_client_missing",
                    "A generic API client is required for automatic " +
                    "environment resolution.",
                    editorOverrideActive);
            }

            ApiResult<SimultriaResourceResponse<SimultriaUnityBuildVersionDto>>
                lookup;
            try
            {
                var service = new SimultriaUnityBuildVersionLookupService(
                    apiClient,
                    composition,
                    directoryEnvironment);
                lookup = await service.GetBuildVersionAsync(
                    buildVersion,
                    product,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_directory_lookup_failed",
                    "The Simultria Unity build directory lookup failed (" +
                    exception.GetType().Name + ").",
                    editorOverrideActive);
            }

            if (lookup?.IsSuccess != true || lookup.Data?.Data == null)
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_directory_lookup_failed",
                    BuildLookupFailureMessage(lookup),
                    editorOverrideActive);
            }

            SimultriaUnityBuildVersionDto response = lookup.Data.Data;
            if (!string.Equals(
                    response.Version?.Trim(),
                    buildVersion,
                    StringComparison.Ordinal))
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_version_mismatch",
                    "The Simultria Unity build directory returned a " +
                    "different build version. No fallback version was used.",
                    editorOverrideActive);
            }

            if (!string.Equals(
                    response.Product?.Trim(),
                    product,
                    StringComparison.Ordinal))
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_product_mismatch",
                    "The Simultria Unity build directory returned a " +
                    "different product.",
                    editorOverrideActive);
            }

            if (!SimultriaBuildEnvironmentNameMapper.TryMap(
                    response.Environment,
                    out ApiEnvironmentId resolvedEnvironment,
                    out string mappingError))
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_environment_unknown",
                    mappingError,
                    editorOverrideActive);
            }

            ApiEnvironmentStatus resolvedStatus =
                composition.GetEnvironmentStatus(resolvedEnvironment);
            if (!resolvedStatus.IsResolved)
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "resolved_environment_unavailable",
                    resolvedStatus.Message,
                    editorOverrideActive);
            }

            return SimultriaViewerEnvironmentResolution.Success(
                mode,
                resolvedEnvironment,
                buildVersion,
                product,
                "Simultria Unity build directory",
                RuntimeKind(),
                ApplicationName(),
                editorOverrideActive);
        }

#if UNITY_EDITOR
        private string ResolveEditorBuildVersion(
            SimultriaViewerDevelopmentProfile profile)
        {
            string configured = profile.BuildVersionOverride;
            return string.IsNullOrWhiteSpace(configured)
                ? CurrentBuildVersion()
                : configured.Trim();
        }
#endif

        private string CurrentBuildVersion() =>
            (metadataProvider.BuildVersion ?? string.Empty).Trim();

        private string ApplicationName() =>
            (runtimeContext.ApplicationName ?? string.Empty).Trim();

        private SimultriaViewerRuntimeKind RuntimeKind() =>
            runtimeContext.IsEditor
                ? SimultriaViewerRuntimeKind.Editor
                : SimultriaViewerRuntimeKind.Build;

        private SimultriaViewerEnvironmentResolution Failure(
            SimultriaViewerEnvironmentResolutionMode mode,
            string buildVersion,
            string product,
            string errorCode,
            string message,
            bool editorOverrideActive)
        {
            return SimultriaViewerEnvironmentResolution.Failure(
                mode,
                buildVersion,
                product,
                errorCode,
                string.IsNullOrWhiteSpace(message)
                    ? "The effective Simultria environment could not be " +
                      "resolved."
                    : message,
                RuntimeKind(),
                ApplicationName(),
                editorOverrideActive);
        }

        private static string BuildLookupFailureMessage(
            ApiResult<SimultriaResourceResponse<SimultriaUnityBuildVersionDto>>
                result)
        {
            return result?.HttpStatusCode.HasValue == true
                ? "The Simultria Unity build directory rejected the lookup " +
                  "(HTTP " + result.HttpStatusCode.Value + ")."
                : "The Simultria Unity build directory lookup did not return " +
                  "a usable response.";
        }
    }
}
