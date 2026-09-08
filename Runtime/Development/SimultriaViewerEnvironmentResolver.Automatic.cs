using System.Threading;
using System.Threading.Tasks;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Deucarian.Simultria.UnityBuildRouting;

namespace Deucarian.SimultriaViewerIntegration
{
    public sealed partial class SimultriaViewerEnvironmentResolver
    {
        private async Task<SimultriaViewerEnvironmentResolution>
            ResolveAutomaticAsync(
                ApiComposition composition,
                string buildVersion,
                string productValue,
                bool editorOverrideActive,
                CancellationToken cancellationToken)
        {
            const SimultriaViewerEnvironmentResolutionMode mode =
                SimultriaViewerEnvironmentResolutionMode
                    .AutomaticFromUnityBuildVersion;
            SimultriaUnityBuildRoutingResult routing =
                await new SimultriaUnityBuildRoutingService(apiClient, composition)
                    .ResolveAsync(buildVersion, productValue, cancellationToken);
            if (!routing.Succeeded)
            {
                if (routing.IsVersionMissing && !runtimeContext.IsEditor)
                {
                    return ResolveBuildProfileFallback(composition, routing);
                }

                return Failure(mode, routing.BuildVersion, routing.Product,
                    routing.ErrorCode, routing.Message, editorOverrideActive);
            }

            return SimultriaViewerEnvironmentResolution.Success(
                mode, routing.EnvironmentId, routing.BuildVersion, routing.Product,
                "Central Production Unity build directory", RuntimeKind(),
                ApplicationName(), editorOverrideActive);
        }

        private SimultriaViewerEnvironmentResolution ResolveBuildProfileFallback(
            ApiComposition composition,
            SimultriaUnityBuildRoutingResult missingRecord)
        {
            // These are the two environments supported by Build Pipeline.
            // A developer's manual Local selection is never a build default.
            if (buildProfileEnvironmentId != SimultriaEnvironmentIds.Production &&
                buildProfileEnvironmentId != SimultriaEnvironmentIds.Development)
            {
                return Failure(
                    SimultriaViewerEnvironmentResolutionMode.AutomaticFromUnityBuildVersion,
                    missingRecord.BuildVersion, missingRecord.Product,
                    "build_profile_environment_missing",
                    "No exact version record was found and this player has no " +
                    "valid captured build-profile environment. Make a full build " +
                    "with the current Simultria build integration.", false);
            }

            ApiEnvironmentStatus status =
                composition.GetEnvironmentStatus(buildProfileEnvironmentId);
            if (!status.IsResolved)
            {
                return Failure(
                    SimultriaViewerEnvironmentResolutionMode.AutomaticFromUnityBuildVersion,
                    missingRecord.BuildVersion, missingRecord.Product,
                    "build_profile_environment_unavailable", status.Message, false);
            }

            return SimultriaViewerEnvironmentResolution.Success(
                SimultriaViewerEnvironmentResolutionMode.AutomaticFromUnityBuildVersion,
                buildProfileEnvironmentId, missingRecord.BuildVersion,
                missingRecord.Product,
                "Build profile fallback (version record missing)",
                SimultriaViewerRuntimeKind.Build, ApplicationName(), false,
                usedBuildProfileFallback: true);
        }
    }
}
