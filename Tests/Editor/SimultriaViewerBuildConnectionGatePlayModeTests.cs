using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.API;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Deucarian.Simultria.API.Models;
using Deucarian.SimultriaViewerIntegration.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    /// <summary>
    /// Editor-launched real Play Mode lifecycle coverage. No shared EditMode
    /// fixture assets are created before the domain transition.
    /// </summary>
    public sealed class SimultriaViewerBuildConnectionGatePlayModeTests
    {
        private Scene scene;

        [UnityTest]
        public IEnumerator DestroyingGateGameObjectDuringLookupKeepsExplicitStartupOwnerDisabled()
        {
            yield return new EnterPlayMode();
            Assert.That(Application.isPlaying, Is.True);
            var owned = new List<Object>();
            var client = new DeferredDirectoryClient();
            GameObject root = null;
            GameObject startupRoot = null;
            IDisposable suspension = null;
            try
            {
                suspension = SimultriaViewerEditorAuthenticationHost.SuspendForTests();
                SimultriaViewerRuntimeEnvironment.ResetForLifecycle();
                scene = SceneManager.CreateScene("Gate lifetime " + Guid.NewGuid().ToString("N"));
                root = new GameObject("Gate under test");
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                startupRoot = new GameObject("Explicit startup owner");
                SceneManager.MoveGameObjectToScene(startupRoot, scene);
                Behaviour startup = startupRoot.AddComponent<Camera>();
                startup.enabled = true;
                var configuration = ScriptableObject.CreateInstance<SimultriaViewerBuildConfiguration>();
                owned.Add(configuration);
                configuration.Product = "activity_viewer";
                configuration.ConnectionSettings = CreateConnection(owned);
                var metadata = new BuildMetadata();
                var resolver = new SimultriaViewerEnvironmentResolver(client, metadata, metadata);
                var gate = root.AddComponent<SimultriaViewerBuildConnectionGate>();
                gate.ConfigureForTests(configuration, new[] { startup }, resolver);
                var phases = new List<SimultriaViewerBuildStartupPhase>();
                gate.StartupStatusChanged += value => phases.Add(value.Phase);

                root.SetActive(true); // Real Awake starts the pending resolver.
                Assert.That(client.Requested, Is.True);
                Assert.That(gate.StartupStatus.Phase,
                    Is.EqualTo(SimultriaViewerBuildStartupPhase.Resolving));
                Assert.That(startup.enabled, Is.False);
                Task pending = gate.PendingResolution;
                Assert.That(pending.IsCompleted, Is.False);

                Object.Destroy(root); // Real deferred OnDestroy, not a test seam.
                yield return null;
                Assert.That(root == null, Is.True);
                Assert.That(gate.StartupStatus.Phase,
                    Is.EqualTo(SimultriaViewerBuildStartupPhase.Disposed));
                Assert.That(client.ReceivedCancellation.IsCancellationRequested, Is.True);

                client.Completion.SetResult(true); // Transport ignores cancellation.
                for (int frame = 0; frame < 120 && !pending.IsCompleted; frame++)
                    yield return null;
                Assert.That(pending.IsCompleted, Is.True, "Late lookup did not finish within 120 frames.");
                Assert.That(pending.IsFaulted, Is.False);
                Assert.That(pending.IsCanceled, Is.False);
                Assert.That(startup.enabled, Is.False);
                Assert.That(gate.Resolution, Is.Null);
                Assert.That(SimultriaViewerRuntimeEnvironment.Current, Is.Null);
                Assert.That(phases, Is.EqualTo(new[]
                {
                    SimultriaViewerBuildStartupPhase.Resolving,
                    SimultriaViewerBuildStartupPhase.Disposed
                }));
            }
            finally
            {
                // Immediate destruction is cleanup only, never the assertion's
                // lifecycle trigger. This also closes a failed pending fixture.
                if (root != null)
                    Object.DestroyImmediate(root);
                client.Completion.TrySetResult(true);
                if (startupRoot != null)
                    Object.DestroyImmediate(startupRoot);
                for (int i = owned.Count - 1; i >= 0; i--)
                    Object.DestroyImmediate(owned[i]);
                SimultriaViewerRuntimeEnvironment.ResetForLifecycle();
                suspension?.Dispose();
            }
        }

        [UnityTearDown]
        public IEnumerator ExitFixturePlayMode()
        {
            if (Application.isPlaying && scene.IsValid() && scene.isLoaded)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            scene = default(Scene);
            if (Application.isPlaying)
                yield return new ExitPlayMode();
        }

        private static ApiConnectionSettings CreateConnection(List<Object> owned)
        {
            ApiServiceDefinition definition = SimultriaApiDefinitionDefaults.LoadServiceDefinition();
            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.TryGetEnvironmentDescriptors(
                out IReadOnlyList<ApiEnvironmentDescriptor> descriptors, out string error), Is.True, error);
            var environments = new List<ApiEnvironmentProfile>();
            foreach (ApiEnvironmentDescriptor descriptor in descriptors)
            {
                var environment = ScriptableObject.CreateInstance<ApiEnvironmentProfile>();
                owned.Add(environment);
                environment.EnvironmentId = descriptor.EnvironmentId.Value;
                environment.DisplayName = descriptor.DisplayName;
                environment.Clients.Add(new ApiNamedClientDefinition
                {
                    ClientId = SimultriaClientIds.Primary.Value,
                    BaseUrl = descriptor.EnvironmentId == SimultriaEnvironmentIds.Local
                        ? "https://gate-fixture.invalid" : string.Empty
                });
                environments.Add(environment);
            }
            ApiConnectionSettings connection = ApiConnectionSettings.CreateTransient(environments, definition);
            owned.Add(connection);
            return connection;
        }

        private sealed class BuildMetadata : ISimultriaViewerBuildMetadataProvider,
            ISimultriaViewerRuntimeContext
        {
            public string BuildVersion => "1.0.0";
            public bool IsEditor => false;
            public string ApplicationName => "Gate lifetime fixture";
        }

        private sealed class DeferredDirectoryClient : IApiClient
        {
            internal readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>();
            internal bool Requested { get; private set; }
            internal CancellationToken ReceivedCancellation { get; private set; }

            public async Task<ApiResult<T>> SendAsync<T>(ApiEndpoint endpoint,
                CancellationToken cancellationToken = default(CancellationToken))
            {
                Requested = true;
                ReceivedCancellation = cancellationToken;
                await Completion.Task;
                var response = new SimultriaResourceResponse<SimultriaUnityBuildVersionDto>
                {
                    Data = new SimultriaUnityBuildVersionDto
                    {
                        Version = "1.0.0", Product = "activity_viewer", Environment = "local"
                    }
                };
                return (ApiResult<T>)(object)ApiResult<SimultriaResourceResponse<
                    SimultriaUnityBuildVersionDto>>.Success(response, HttpMethod.GET, 200, null, null);
            }

            public Task<ApiResult<T>> SendAsync<T>(ApiRequest request,
                CancellationToken cancellationToken = default(CancellationToken)) =>
                throw new NotSupportedException();
            public Task<ApiResult<T>> SendAsync<T>(ApiEndpoint endpoint, object body,
                CancellationToken cancellationToken = default(CancellationToken)) =>
                throw new NotSupportedException();
            public Task<ApiResult<T>> GetAsync<T>(string endpoint,
                CancellationToken cancellationToken = default(CancellationToken)) =>
                throw new NotSupportedException();
            public Task<ApiResult<T>> PostAsync<T>(string endpoint, object body,
                CancellationToken cancellationToken = default(CancellationToken)) =>
                throw new NotSupportedException();
            public Task<ApiResult<T>> PutAsync<T>(string endpoint, object body,
                CancellationToken cancellationToken = default(CancellationToken)) =>
                throw new NotSupportedException();
            public Task<ApiResult<T>> PatchAsync<T>(string endpoint, object body,
                CancellationToken cancellationToken = default(CancellationToken)) =>
                throw new NotSupportedException();
            public Task<ApiResult<T>> DeleteAsync<T>(string endpoint,
                CancellationToken cancellationToken = default(CancellationToken)) =>
                throw new NotSupportedException();
        }
    }
}
