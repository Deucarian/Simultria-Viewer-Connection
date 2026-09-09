using System.Threading.Tasks;
using Deucarian.API;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Deucarian.Simultria.API.Models;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed partial class SimultriaViewerEnvironmentResolverTests
    {
        private const string MissingRecordBody =
            "{\"code\":\"build_version_not_found\"}";

        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingPlayerRecordUsesCapturedProfile(bool development)
        {
            var fallback = development ? SimultriaEnvironmentIds.Development
                : SimultriaEnvironmentIds.Production;
            var client = ClassifiedFailure(404, MissingRecordBody);
            var configuration = FallbackConfiguration();
            // Neither the legacy lookup selection nor an Editor Local context
            // participates in the compiled player's environment decision.
            configuration.BuildDirectoryEnvironmentId = SimultriaEnvironmentIds.Local;
            profile.EnvironmentId = SimultriaEnvironmentIds.Local;
            var result = await BuildResolver(client, fallback)
                .ResolveForCurrentRuntimeAsync(configuration, profile);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.EnvironmentId, Is.EqualTo(fallback));
            Assert.That(result.UsedBuildProfileFallback, Is.True);
            Assert.That(result.EditorOverrideActive, Is.False);
            Assert.That(result.Source, Does.Contain("Build profile fallback"));
            Assert.That(result.BuildVersion, Is.EqualTo("compiled-1.0"));
            Assert.That(result.Product, Is.EqualTo("activity_viewer"));
            Assert.That(client.RequestCount, Is.EqualTo(1));
            Assert.That(client.LastEndpoint.Path,
                Is.EqualTo("https://buildingvirtualitysuite.com/api/v2/unity/builds/versions/compiled-1.0/activity_viewer"));
        }

        [Test]
        public async Task ExactRecordWinsOverProductionBuildProfile()
        {
            var client = BuildDirectoryClient.Success(
                "compiled-1.0", "activity_viewer", "development");
            var result = await BuildResolver(client, SimultriaEnvironmentIds.Production)
                .ResolveForCurrentRuntimeAsync(FallbackConfiguration());
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.EnvironmentId, Is.EqualTo(SimultriaEnvironmentIds.Development));
            Assert.That(result.UsedBuildProfileFallback, Is.False);
            Assert.That(result.Source, Is.EqualTo("Central Production Unity build directory"));
        }

        [TestCase(404, "")]
        [TestCase(404, "{\"code\":\"unsupported_product\"}")]
        [TestCase(404, "<html>Not found</html>")]
        [TestCase(401, MissingRecordBody)]
        [TestCase(403, MissingRecordBody)]
        [TestCase(503, MissingRecordBody)]
        public async Task OtherFailuresNeverUseBuildProfile(long status, string body)
        {
            var result = await BuildResolver(ClassifiedFailure(status, body),
                    SimultriaEnvironmentIds.Production)
                .ResolveForCurrentRuntimeAsync(FallbackConfiguration());
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.UsedBuildProfileFallback, Is.False);
            Assert.That(result.EnvironmentId.IsEmpty, Is.True);
        }

        [TestCase("another-version", "activity_viewer", "production")]
        [TestCase("compiled-1.0", "another-product", "production")]
        [TestCase("compiled-1.0", "activity_viewer", "deprecated")]
        public async Task InvalidRecordNeverUsesBuildProfile(
            string version, string product, string environment)
        {
            var result = await BuildResolver(
                    BuildDirectoryClient.Success(version, product, environment),
                    SimultriaEnvironmentIds.Production)
                .ResolveForCurrentRuntimeAsync(FallbackConfiguration());
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.UsedBuildProfileFallback, Is.False);
        }

        [TestCase(null)]
        [TestCase("simultria.local")]
        public async Task MissingOrInvalidStampDoesNotGuessProduction(string stamp)
        {
            var id = string.IsNullOrEmpty(stamp) ? default(ApiEnvironmentId)
                : new ApiEnvironmentId(stamp);
            var result = await BuildResolver(ClassifiedFailure(404, MissingRecordBody), id)
                .ResolveForCurrentRuntimeAsync(FallbackConfiguration());
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("build_profile_environment_missing"));
        }

        [Test]
        public async Task UnconfiguredFallbackDoesNotUseAnotherBackend()
        {
            var configuration = FallbackConfiguration();
            configuration.ConnectionSettings = CreateConnectionSettings(SimultriaEnvironmentIds.Development);
            var result = await BuildResolver(ClassifiedFailure(404, MissingRecordBody),
                    SimultriaEnvironmentIds.Production)
                .ResolveForCurrentRuntimeAsync(configuration);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("build_profile_environment_unavailable"));
        }

        [Test]
        public async Task EditorAutomaticMissingRecordDoesNotUsePlayerFallback()
        {
            ConfigureAutomaticProfile();
            var resolver = new SimultriaViewerEnvironmentResolver(
                ClassifiedFailure(404, MissingRecordBody),
                new FixedBuildMetadataProvider("compiled-1.0"),
                new FixedRuntimeContext(true, "Editor"), SimultriaEnvironmentIds.Production);
            var result = await resolver.ResolveAsync(profile);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("build_version_not_found"));
            Assert.That(result.UsedBuildProfileFallback, Is.False);
        }

        [Test]
        public async Task EditorManualLocalRemainsIndependentOfLookupAndFallback()
        {
            profile.ConnectionSettingsReference = CreateConnectionSettings(SimultriaEnvironmentIds.Local);
            profile.EnvironmentId = SimultriaEnvironmentIds.Local;
            var client = new BuildDirectoryClient();
            var resolver = new SimultriaViewerEnvironmentResolver(client,
                new FixedBuildMetadataProvider("compiled-1.0"),
                new FixedRuntimeContext(true, "Editor"), SimultriaEnvironmentIds.Production);
            var result = await resolver.ResolveAsync(profile);
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.EnvironmentId, Is.EqualTo(SimultriaEnvironmentIds.Local));
            Assert.That(result.EditorOverrideActive, Is.True);
            Assert.That(result.UsedBuildProfileFallback, Is.False);
            Assert.That(client.RequestCount, Is.Zero);
        }

        [Test]
        public void LegacySerializedDirectoryHasNoInspectorOrRuntimeSelection()
        {
            var configuration = FallbackConfiguration();
            JsonUtility.FromJsonOverwrite(
                "{\"buildDirectoryEnvironmentId\":{\"value\":\"simultria.local\"}}", configuration);
            Assert.That(configuration.BuildDirectoryEnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Production));
            Assert.That(new SerializedObject(configuration).FindProperty("buildDirectoryEnvironmentId"), Is.Null);
            Assert.That(new SerializedObject(profile).FindProperty("buildDirectoryEnvironmentId"), Is.Null);
        }

        private SimultriaViewerBuildConfiguration FallbackConfiguration()
        {
            var configuration = Own(ScriptableObject.CreateInstance<SimultriaViewerBuildConfiguration>());
            configuration.ConnectionSettings = CreateConnectionSettings(
                SimultriaEnvironmentIds.Production, SimultriaEnvironmentIds.Development);
            configuration.Product = "activity_viewer";
            return configuration;
        }

        private static SimultriaViewerEnvironmentResolver BuildResolver(
            BuildDirectoryClient client, ApiEnvironmentId fallback)
        {
            return new SimultriaViewerEnvironmentResolver(client,
                new FixedBuildMetadataProvider("compiled-1.0"),
                new FixedRuntimeContext(false, "Activity Viewer"), fallback);
        }

        private static BuildDirectoryClient ClassifiedFailure(long status, string body)
        {
            return new BuildDirectoryClient(ApiResult<SimultriaResourceResponse<
                SimultriaUnityBuildVersionDto>>.Failure(new ApiError
                { HttpStatusCode = status, RawResponseBody = body }, HttpMethod.GET));
        }
    }
}
