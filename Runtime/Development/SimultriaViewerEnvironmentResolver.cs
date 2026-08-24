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
    /// <summary>Reads the Unity build version from the running application.</summary>
    public sealed class SimultriaViewerApplicationBuildMetadataProvider :
        ISimultriaViewerBuildMetadataProvider
    {
        public string BuildVersion => Application.version;
    }

    /// <summary>
    /// Resolves one effective environment without storing deployment hosts,
    /// credentials, or a second environment mapping in the viewer package.
    /// </summary>
    public sealed class SimultriaViewerEnvironmentResolver
    {
        private readonly IApiClient apiClient;
        private readonly ISimultriaViewerBuildMetadataProvider metadataProvider;

        public SimultriaViewerEnvironmentResolver(
            IApiClient apiClient,
            ISimultriaViewerBuildMetadataProvider metadataProvider)
        {
            this.apiClient = apiClient;
            this.metadataProvider = metadataProvider ??
                throw new ArgumentNullException(nameof(metadataProvider));
        }

        /// <summary>Creates the normal Unity-backed resolver.</summary>
        public static SimultriaViewerEnvironmentResolver CreateDefault()
        {
            return new SimultriaViewerEnvironmentResolver(
                ApiClientFactory.CreateDefault(),
                new SimultriaViewerApplicationBuildMetadataProvider());
        }

        /// <summary>
        /// Resolves the manual selection or asks the public Simultria build
        /// directory for the environment assigned to this build.
        /// </summary>
        public async Task<SimultriaViewerEnvironmentResolution> ResolveAsync(
            SimultriaViewerDevelopmentProfile profile,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (profile == null)
            {
                return Failure(
                    SimultriaViewerEnvironmentResolutionMode.Manual,
                    null,
                    null,
                    "profile_missing",
                    "A Simultria viewer development profile is required.");
            }

            SimultriaViewerEnvironmentResolutionMode mode =
                profile.EnvironmentResolutionMode;
            if (!profile.TryCreateComposition(
                    out ApiComposition composition,
                    out string compositionError))
            {
                return Failure(
                    mode,
                    ResolveBuildVersion(profile),
                    profile.BuildProduct,
                    "api_composition_unavailable",
                    compositionError);
            }

            if (mode == SimultriaViewerEnvironmentResolutionMode.Manual)
            {
                ApiEnvironmentId manualEnvironment = profile.EnvironmentId;
                ApiEnvironmentStatus status =
                    composition.GetEnvironmentStatus(manualEnvironment);
                return status.IsResolved
                    ? SimultriaViewerEnvironmentResolution.Success(
                        mode,
                        manualEnvironment,
                        string.Empty,
                        string.Empty,
                        "Manual profile selection")
                    : Failure(
                        mode,
                        null,
                        null,
                        "manual_environment_unavailable",
                        status.Message);
            }

            string buildVersion = ResolveBuildVersion(profile);
            string product = profile.BuildProduct.Trim();
            if (string.IsNullOrWhiteSpace(buildVersion))
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_version_missing",
                    "Automatic environment resolution requires a Unity build version.");
            }

            if (string.IsNullOrWhiteSpace(product))
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_product_missing",
                    "Automatic environment resolution requires a Simultria build product.");
            }

            ApiEnvironmentId directoryEnvironment =
                profile.BuildDirectoryEnvironmentId;
            if (directoryEnvironment.IsEmpty)
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_directory_environment_missing",
                    "Choose the configured API environment that hosts the Simultria Unity build directory.");
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
                    directoryStatus.Message);
            }

            if (apiClient == null)
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_directory_client_missing",
                    "A generic API client is required for automatic environment resolution.");
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
                    exception.GetType().Name + ").");
            }

            if (lookup?.IsSuccess != true || lookup.Data?.Data == null)
            {
                return Failure(
                    mode,
                    buildVersion,
                    product,
                    "build_directory_lookup_failed",
                    BuildLookupFailureMessage(lookup));
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
                    "The Simultria Unity build directory returned a different build version.");
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
                    "The Simultria Unity build directory returned a different product.");
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
                    mappingError);
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
                    resolvedStatus.Message);
            }

            return SimultriaViewerEnvironmentResolution.Success(
                mode,
                resolvedEnvironment,
                buildVersion,
                product,
                "Simultria Unity build directory");
        }

        private string ResolveBuildVersion(
            SimultriaViewerDevelopmentProfile profile)
        {
            string configured = profile.BuildVersionOverride;
            return string.IsNullOrWhiteSpace(configured)
                ? (metadataProvider.BuildVersion ?? string.Empty).Trim()
                : configured.Trim();
        }

        private static SimultriaViewerEnvironmentResolution Failure(
            SimultriaViewerEnvironmentResolutionMode mode,
            string buildVersion,
            string product,
            string errorCode,
            string message)
        {
            return SimultriaViewerEnvironmentResolution.Failure(
                mode,
                buildVersion,
                product,
                errorCode,
                string.IsNullOrWhiteSpace(message)
                    ? "The effective Simultria environment could not be resolved."
                    : message);
        }

        private static string BuildLookupFailureMessage(
            ApiResult<SimultriaResourceResponse<SimultriaUnityBuildVersionDto>>
                result)
        {
            return result?.HttpStatusCode.HasValue == true
                ? "The Simultria Unity build directory rejected the lookup (HTTP " +
                  result.HttpStatusCode.Value + ")."
                : "The Simultria Unity build directory lookup did not return a usable response.";
        }
    }
}
