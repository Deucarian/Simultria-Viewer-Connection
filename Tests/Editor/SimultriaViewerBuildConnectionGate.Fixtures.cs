using System;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.API;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Authentication;
using Deucarian.Simultria.API.Configuration;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed partial class SimultriaViewerEnvironmentResolverTests
    {
        private GateHarness CreateGate(IApiClient client = null,
            ApiEnvironmentId stamp = default(ApiEnvironmentId))
        {
            var configuration = FallbackConfiguration();
            configuration.ConnectionSettings = CreateConnectionSettings(
                SimultriaEnvironmentIds.Local, SimultriaEnvironmentIds.Development,
                SimultriaEnvironmentIds.Testing, SimultriaEnvironmentIds.Acceptance,
                SimultriaEnvironmentIds.Production);
            var resolver = new SimultriaViewerEnvironmentResolver(
                client ?? BuildDirectoryClient.Success("compiled-1.0", "activity_viewer", "local"),
                new FixedBuildMetadataProvider("compiled-1.0"),
                new FixedRuntimeContext(false, "Viewer"), stamp);
            return new GateHarness(configuration, resolver);
        }

        private static SimultriaViewerEnvironmentResolution GateResolution() =>
            SimultriaViewerEnvironmentResolution.Success(
                SimultriaViewerEnvironmentResolutionMode.AutomaticFromUnityBuildVersion,
                SimultriaEnvironmentIds.Production, "compiled-1.0", "activity_viewer",
                "directory", SimultriaViewerRuntimeKind.Build, "Viewer", false);

        private sealed class GateHarness : IDisposable
        {
            private readonly Scene scene;

            internal GateHarness(SimultriaViewerBuildConfiguration configuration,
                SimultriaViewerEnvironmentResolver resolver)
            {
                Configuration = configuration;
                Resolver = resolver;
                scene = EditorSceneManager.NewPreviewScene();
                var root = new GameObject("Gate lifecycle test");
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                var startupRoot = new GameObject("Explicit startup owner");
                startupRoot.SetActive(false);
                SceneManager.MoveGameObjectToScene(startupRoot, scene);
                Startup = startupRoot.AddComponent<Camera>();
                Startup.enabled = true;
                Gate = root.AddComponent<SimultriaViewerBuildConnectionGate>();
                Gate.ConfigureForTests(configuration, new Behaviour[] { Startup }, resolver,
                    (settings, id) => new GateProvider());
            }

            internal SimultriaViewerBuildConfiguration Configuration { get; }
            internal SimultriaViewerEnvironmentResolver Resolver { get; }
            internal SimultriaViewerBuildConnectionGate Gate { get; }
            internal Behaviour Startup { get; }

            public void Dispose()
            {
                Gate.DisposeStartup();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private sealed class GateProvider : IViewerRuntimeConnectionProvider
        {
            private readonly Action onId;
            internal GateProvider(Action onId = null) => this.onId = onId;
            public string Id
            {
                get
                {
                    onId?.Invoke();
                    return "test.gate-lifecycle";
                }
            }

            public bool TryCreate(out ViewerRuntimeConnection connection, out string error)
            {
                connection = null;
                error = "This fixture only exercises registration ownership.";
                return false;
            }
        }

        private sealed class DeferredGateClient : IApiClient
        {
            internal readonly TaskCompletionSource<bool> Completion =
                new TaskCompletionSource<bool>();
            private readonly BuildDirectoryClient result = BuildDirectoryClient.Success(
                "compiled-1.0", "activity_viewer", "local");

            public async Task<ApiResult<T>> SendAsync<T>(ApiEndpoint endpoint,
                CancellationToken cancellationToken = default(CancellationToken))
            {
                // Deliberately ignores cancellation to exercise a transport
                // completion arriving after the scene owner was disposed.
                await Completion.Task;
                return await result.SendAsync<T>(endpoint, cancellationToken);
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
