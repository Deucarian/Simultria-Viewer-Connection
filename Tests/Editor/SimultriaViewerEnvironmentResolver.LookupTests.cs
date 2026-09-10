using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.Simultria.API.Configuration;
using Deucarian.SimultriaViewerIntegration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed partial class SimultriaViewerEnvironmentResolverTests
    {
        [Test]
        public void LookupDefaultsToProductionAndIgnoresLegacySerializedLocal()
        {
            var configuration = FallbackConfiguration();
            const string legacy = "{\"buildDirectoryEnvironmentId\":{\"value\":\"simultria.local\"}}";
            JsonUtility.FromJsonOverwrite(legacy, configuration);
            JsonUtility.FromJsonOverwrite(legacy, profile);
            Assert.That(configuration.LookupEnvironment,
                Is.EqualTo(SimultriaUnityBuildLookupEnvironment.Production));
            Assert.That(profile.LookupEnvironment,
                Is.EqualTo(SimultriaUnityBuildLookupEnvironment.Production));
            Assert.That(new SerializedObject(configuration).FindProperty("lookupEnvironment")
                .propertyType, Is.EqualTo(SerializedPropertyType.Enum));

            configuration.LookupEnvironment = SimultriaUnityBuildLookupEnvironment.Development;
            var restored = Own(ScriptableObject.CreateInstance<SimultriaViewerBuildConfiguration>());
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(configuration), restored);
            Assert.That(restored.LookupEnvironment,
                Is.EqualTo(SimultriaUnityBuildLookupEnvironment.Development));
        }

        [TestCase("production")]
        [TestCase("development")]
        public async Task DevelopmentDirectoryDoesNotDetermineRuntimeBackend(string runtime)
        {
            var configuration = FallbackConfiguration();
            configuration.LookupEnvironment = SimultriaUnityBuildLookupEnvironment.Development;
            profile.LookupEnvironment = SimultriaUnityBuildLookupEnvironment.Production;
            var client = BuildDirectoryClient.Success("compiled-1.0", "activity_viewer", runtime);
            var result = await BuildResolver(client, SimultriaEnvironmentIds.Development)
                .ResolveForCurrentRuntimeAsync(configuration, profile);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.EnvironmentId, Is.EqualTo(EnvironmentIdFor(runtime)));
            Assert.That(result.UsedBuildProfileFallback, Is.False);
            Assert.That(client.RequestCount, Is.EqualTo(1));
            Assert.That(client.LastEndpoint.Path, Is.EqualTo(
                "https://backend.dev-buildingvirtuality.com/api/v2/unity/builds/versions/compiled-1.0/activity_viewer"));
            Assert.That(result.Source, Is.EqualTo("Central Development Unity build directory"));
        }

        [Test]
        public async Task PlayerNeverInheritsEditorLookupSelection()
        {
            profile.LookupEnvironment = SimultriaUnityBuildLookupEnvironment.Development;
            var client = BuildDirectoryClient.Success("compiled-1.0", "activity_viewer", "development");
            var result = await BuildResolver(client, SimultriaEnvironmentIds.Production)
                .ResolveForCurrentRuntimeAsync(FallbackConfiguration(), profile);
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(client.LastEndpoint.Path, Does.StartWith(SimultriaUnityBuildDirectory.BaseUrl + "/"));
        }

        [Test]
        public async Task AutomaticEditorUsesItsOwnLookupSelection()
        {
            ConfigureAutomaticProfile();
            profile.BuildProduct = "activity_viewer";
            profile.BuildVersionOverride = "editor-version";
            profile.LookupEnvironment = SimultriaUnityBuildLookupEnvironment.Development;
            var client = BuildDirectoryClient.Success("editor-version", "activity_viewer", "testing");
            var resolver = new SimultriaViewerEnvironmentResolver(client,
                new FixedBuildMetadataProvider("compiled-1.0"), new FixedRuntimeContext(true, "Editor"));
            var result = await resolver.ResolveForCurrentRuntimeAsync(FallbackConfiguration(), profile);
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.EnvironmentId, Is.EqualTo(SimultriaEnvironmentIds.Testing));
            Assert.That(client.LastEndpoint.Path, Is.EqualTo(
                "https://backend.dev-buildingvirtuality.com/api/v2/unity/builds/versions/editor-version/activity_viewer"));
        }

        [TestCase(-1)]
        [TestCase(99)]
        public async Task InvalidLookupCannotUseNetworkOrProfileFallback(int invalid)
        {
            var configuration = FallbackConfiguration();
            configuration.LookupEnvironment = (SimultriaUnityBuildLookupEnvironment)invalid;
            var client = ClassifiedFailure(404, MissingRecordBody);
            var result = await BuildResolver(client, SimultriaEnvironmentIds.Production)
                .ResolveForCurrentRuntimeAsync(configuration);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("build_lookup_environment_invalid"));
            Assert.That(result.UsedBuildProfileFallback, Is.False);
            Assert.That(client.RequestCount, Is.Zero);
        }

        [Test]
        public async Task MissingDevelopmentRecordUsesCapturedProductionNotDirectoryEnvironment()
        {
            var configuration = FallbackConfiguration();
            configuration.LookupEnvironment = SimultriaUnityBuildLookupEnvironment.Development;
            var client = ClassifiedFailure(404, MissingRecordBody);
            var result = await BuildResolver(client, SimultriaEnvironmentIds.Production)
                .ResolveForCurrentRuntimeAsync(configuration);
            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.EnvironmentId, Is.EqualTo(SimultriaEnvironmentIds.Production));
            Assert.That(result.UsedBuildProfileFallback, Is.True);
            Assert.That(client.RequestCount, Is.EqualTo(1));
        }

        [TestCase(404, "")]
        [TestCase(401, MissingRecordBody)]
        [TestCase(503, MissingRecordBody)]
        public async Task DevelopmentLookupFailuresNeverRetryProduction(long status, string body)
        {
            var configuration = FallbackConfiguration();
            configuration.LookupEnvironment = SimultriaUnityBuildLookupEnvironment.Development;
            var client = ClassifiedFailure(status, body);
            var result = await BuildResolver(client, SimultriaEnvironmentIds.Production)
                .ResolveForCurrentRuntimeAsync(configuration);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.UsedBuildProfileFallback, Is.False);
            Assert.That(client.RequestCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ReplacingPendingLookupCancelsAndRejectsItsLateCompletion()
        {
            ConfigureAutomaticProfile();
            SimultriaViewerEditorAuthenticationHost.RequestRefresh();
            var first = new TaskCompletionSource<SimultriaViewerEnvironmentResolution>();
            var second = new TaskCompletionSource<SimultriaViewerEnvironmentResolution>();
            CancellationToken firstToken = default;
            Task firstRequest = SimultriaViewerEditorAuthenticationHost.StartEnvironmentResolution(
                profile, (context, token) => { firstToken = token; return first.Task; });
            try
            {
                bool duplicateStarted = false;
                await SimultriaViewerEditorAuthenticationHost.StartEnvironmentResolution(
                    profile, (context, token) => { duplicateStarted = true; return first.Task; });
                Assert.That(duplicateStarted, Is.False, "Same-key pending requests are reused.");

                profile.LookupEnvironment = SimultriaUnityBuildLookupEnvironment.Development;
                Task secondRequest = SimultriaViewerEditorAuthenticationHost.StartEnvironmentResolution(
                    profile, (context, token) => second.Task);
                Assert.That(firstToken.IsCancellationRequested, Is.True);
                var newResult = SimultriaViewerEnvironmentResolution.Success(
                    profile.EnvironmentResolutionMode, SimultriaEnvironmentIds.Testing,
                    "version", "activity_viewer", "new directory", SimultriaViewerRuntimeKind.Editor,
                    "Editor", false);
                second.SetResult(newResult);
                await secondRequest;
                Assert.That(SimultriaViewerEditorAuthenticationHost.TryGetEffectiveEnvironment(
                    profile, out _, out var current, out _), Is.True);
                Assert.That(current, Is.SameAs(newResult));

                first.SetResult(SimultriaViewerEnvironmentResolution.Success(
                    profile.EnvironmentResolutionMode, SimultriaEnvironmentIds.Development,
                    "version", "activity_viewer", "old directory", SimultriaViewerRuntimeKind.Editor,
                    "Editor", false));
                await firstRequest;
                Assert.That(SimultriaViewerEditorAuthenticationHost.TryGetEffectiveEnvironment(
                    profile, out _, out current, out _), Is.True);
                Assert.That(current, Is.SameAs(newResult), "Late obsolete lookup cannot replace the new result.");
            }
            finally
            {
                first.TrySetCanceled();
                second.TrySetCanceled();
                SimultriaViewerEditorAuthenticationHost.RequestRefresh();
            }
        }

        [Test]
        public void ChangingLookupInvalidatesCachedEditorEnvironment()
        {
            ConfigureAutomaticProfile();
            var host = typeof(SimultriaViewerEditorAuthenticationHost);
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var keyMethod = host.GetMethod("BuildEnvironmentResolutionKey", flags);
            string previousKey = (string)keyMethod.Invoke(null, new object[] { profile });
            var cached = SimultriaViewerEnvironmentResolution.Success(
                profile.EnvironmentResolutionMode, SimultriaEnvironmentIds.Development,
                "version", "activity_viewer", "directory", SimultriaViewerRuntimeKind.Editor,
                "Editor", false);
            try
            {
                host.GetField("environmentResolutionProfile", flags).SetValue(null, profile);
                host.GetField("environmentResolutionKey", flags).SetValue(null, previousKey);
                host.GetField("environmentResolution", flags).SetValue(null, cached);
                Assert.That(SimultriaViewerEditorAuthenticationHost.TryGetEffectiveEnvironment(
                    profile, out _, out _, out _), Is.True);

                profile.LookupEnvironment = SimultriaUnityBuildLookupEnvironment.Development;
                Assert.That(keyMethod.Invoke(null, new object[] { profile }), Is.Not.EqualTo(previousKey));
                Assert.That(SimultriaViewerEditorAuthenticationHost.TryGetEffectiveEnvironment(
                    profile, out var environment, out var resolution, out _), Is.False);
                Assert.That(environment.IsEmpty, Is.True);
                Assert.That(resolution, Is.Null);
            }
            finally
            {
                SimultriaViewerEditorAuthenticationHost.RequestRefresh();
            }
        }
    }
}
