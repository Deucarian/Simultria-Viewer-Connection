using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Deucarian.API;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API;
using Deucarian.Simultria.API.Configuration;
using Deucarian.Simultria.API.Models;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed partial class SimultriaViewerRuntimeConnectionContextTests
    {
        #region Tests
        [Test]
        public async Task DefaultCoordinatorInjectsTheExactApiIntoModelResolutionAndApplication()
        {
            ModelClient client = new ModelClient();
            RuntimeInputs inputs = CreateInputs(SimultriaEnvironmentIds.Development, client);
            Assert.That(TryActivate(inputs, out SimultriaViewerRuntimeConnectionContext runtime,
                                    out IDisposable registration, out string error), Is.True, error);
            using (registration)
            {
                SimultriaApi suppliedApi = null;
                SimultriaViewerModelInitializationExecutionResult execution =
                    await new SimultriaViewerModelInitializationCoordinator().ExecuteAsync(
                        ModelPayload(), -1, CreateConnectionSettings(SimultriaEnvironmentIds.Development), null,
                        new PresentAuthProvider(), false,
                        (context, token) =>
                        {
                            suppliedApi = context.Api;
                            return Task.FromResult(SimultriaViewerApplicationInitializationResult.Success());
                        }
                    );
                Assert.That(execution.Succeeded, Is.True, execution.Message);
                Assert.That(suppliedApi, Is.SameAs(runtime.Api));
                Assert.That(execution.Resolution.ModelVersionId, Is.EqualTo(17));
                Assert.That(client.Request.Endpoint, Does.EndWith("/api/v2/projects/1"));
            }
        }

        [Test]
        public async Task RetiredConnectionCancelsPendingModelResolverWithoutWaitingForTransport()
        {
            ModelClient client = new ModelClient { Hold = true };
            RuntimeInputs inputs = CreateInputs(SimultriaEnvironmentIds.Development, client);
            Assert.That(TryActivate(inputs, out SimultriaViewerRuntimeConnectionContext runtime,
                                    out IDisposable registration, out string error), Is.True, error);
            using (registration)
            {
                SimultriaViewerModelInitializationPlan plan = new SimultriaViewerModelInitializationCoordinator()
                    .Prepare(ModelPayload(), runtime, new PresentAuthProvider());
                Task<SimultriaViewerModelInitializationResolution> pending = plan.Resolver.ResolveAsync(ModelPayload());
                Assert.That(pending.IsCompleted, Is.False);
                registration.Dispose();
                await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(5)));
                Assert.That(pending.IsCompleted, Is.True);
                Assert.That(client.Token.IsCancellationRequested, Is.True);
                Assert.That((await pending).ErrorCode, Is.EqualTo(SimultriaViewerModelErrorCodes.ProjectRequestCanceled));
                client.Pending.SetResult(ModelClient.Result());
            }
        }
        #endregion

        #region Methods
        private static SimultriaViewerInitializationPayload ModelPayload() => new SimultriaViewerInitializationPayload
        {
            Revision = 1, EnvironmentId = "simultria.development", ProjectId = 1, ModelId = 2, ModelVersionId = 17
        };
        #endregion

        #region Nested Types
        private sealed class ModelClient : IApiClient
        {
            internal bool Hold { get; set; }
            internal ApiRequest Request { get; private set; }
            internal CancellationToken Token { get; private set; }
            internal TaskCompletionSource<ApiResult<SimultriaResourceResponse<SimultriaProjectDto>>> Pending { get; } =
                new TaskCompletionSource<ApiResult<SimultriaResourceResponse<SimultriaProjectDto>>>();

            public Task<ApiResult<T>> SendAsync<T>(ApiRequest request, CancellationToken cancellationToken = default)
            {
                Request = request;
                Token = cancellationToken;
                Assert.That(typeof(T), Is.EqualTo(typeof(SimultriaResourceResponse<SimultriaProjectDto>)));
                return (Task<ApiResult<T>>)(object)(Hold ? Pending.Task : Task.FromResult(Result()));
            }

            internal static ApiResult<SimultriaResourceResponse<SimultriaProjectDto>> Result() =>
                ApiResult<SimultriaResourceResponse<SimultriaProjectDto>>.Success(
                    new SimultriaResourceResponse<SimultriaProjectDto>
                    {
                        Data = new SimultriaProjectDto
                        {
                            Id = 1,
                            Models = new List<SimultriaModelDto>
                            {
                                new SimultriaModelDto
                                {
                                    Id = 2,
                                    ActiveVersion = new SimultriaModelVersionDto
                                    {
                                        Id = 17, DownloadUrl = "https://models.example.invalid/17"
                                    }
                                }
                            }
                        }
                    }, HttpMethod.GET, 200, null, null
                );

            public Task<ApiResult<T>> SendAsync<T>(ApiEndpoint endpoint, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
            public Task<ApiResult<T>> SendAsync<T>(ApiEndpoint endpoint, object body, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
            public Task<ApiResult<T>> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
            public Task<ApiResult<T>> PostAsync<T>(string endpoint, object body, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
            public Task<ApiResult<T>> PutAsync<T>(string endpoint, object body, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
            public Task<ApiResult<T>> PatchAsync<T>(string endpoint, object body, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
            public Task<ApiResult<T>> DeleteAsync<T>(string endpoint, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
        }
        #endregion
    }
}
