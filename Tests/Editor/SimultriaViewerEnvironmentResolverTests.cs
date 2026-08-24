using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.API;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Deucarian.Simultria.API.Models;
using Deucarian.SimultriaViewerConnection.Editor;
using Deucarian.ViewerAuthentication;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection.Tests
{
    public sealed class SimultriaViewerEnvironmentResolverTests
    {
        private readonly List<UnityEngine.Object> ownedObjects =
            new List<UnityEngine.Object>();
        private SimultriaViewerDevelopmentProfile profile;
        private IDisposable hostSuspension;

        [SetUp]
        public void SetUp()
        {
            hostSuspension =
                SimultriaViewerEditorAuthenticationHost.SuspendForTests();
            profile = Own(
                ScriptableObject.CreateInstance<
                    SimultriaViewerDevelopmentProfile>());
            profile.ConnectionProfileReference = CreateConnectionProfile(
                SimultriaEnvironmentIds.Development,
                SimultriaEnvironmentIds.Testing);
            profile.EnvironmentId = SimultriaEnvironmentIds.Development;
            profile.ProjectId = 832;
            profile.ModelId = 41;
        }

        [TearDown]
        public void TearDown()
        {
            hostSuspension?.Dispose();
            hostSuspension = null;
            for (int i = ownedObjects.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(ownedObjects[i]);
            }

            ownedObjects.Clear();
        }

        [Test]
        public async Task ManualModePreservesEmptyDevelopmentFallback()
        {
            profile.EnvironmentId = default(ApiEnvironmentId);
            profile.EnvironmentResolutionMode =
                SimultriaViewerEnvironmentResolutionMode.Manual;
            var client = new BuildDirectoryClient();
            var resolver = CreateResolver(client, "unused");

            SimultriaViewerEnvironmentResolution result =
                await resolver.ResolveAsync(profile);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(
                result.EnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Development));
            Assert.That(result.Source, Is.EqualTo("Manual profile selection"));
            Assert.That(client.RequestCount, Is.Zero);
        }

        [Test]
        public async Task AutomaticModeUsesPortalResponseAndResolvedPayload()
        {
            ConfigureAutomaticProfile();
            var client = BuildDirectoryClient.Success(
                "build-42",
                "report_viewer",
                "testing");
            var resolver = CreateResolver(client, "ignored-by-override");

            SimultriaViewerEnvironmentResolution result =
                await resolver.ResolveAsync(profile);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(
                result.EnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Testing));
            Assert.That(result.BuildVersion, Is.EqualTo("build-42"));
            Assert.That(result.Product, Is.EqualTo("report_viewer"));
            Assert.That(
                result.Source,
                Is.EqualTo("Simultria Unity build directory"));
            Assert.That(client.RequestCount, Is.EqualTo(1));
            Assert.That(
                client.LastEndpoint.Path,
                Does.EndWith(
                    "/api/v2/unity/builds/versions/build-42/report_viewer"));
            Assert.That(
                profile.TryCreatePayload(
                    12,
                    result.EnvironmentId,
                    out SimultriaViewerInitializationPayload payload,
                    out string error),
                Is.True,
                error);
            Assert.That(
                payload.EnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Testing.Value));
        }

        [Test]
        public async Task AutomaticModeUsesInjectedApplicationVersion()
        {
            ConfigureAutomaticProfile();
            profile.BuildVersionOverride = string.Empty;
            var client = BuildDirectoryClient.Success(
                "injected-build",
                "report_viewer",
                "development");
            var resolver = CreateResolver(client, "injected-build");

            SimultriaViewerEnvironmentResolution result =
                await resolver.ResolveAsync(profile);

            Assert.That(result.Succeeded, Is.True, result.Message);
            Assert.That(result.BuildVersion, Is.EqualTo("injected-build"));
        }

        [TestCase(null, "report_viewer", "build_version_missing")]
        [TestCase("build-42", null, "build_product_missing")]
        public async Task MissingAutomaticInputFailsBeforeTransport(
            string buildVersion,
            string product,
            string expectedErrorCode)
        {
            ConfigureAutomaticProfile();
            profile.BuildVersionOverride = buildVersion;
            profile.BuildProduct = product;
            var client = new BuildDirectoryClient();
            var resolver = CreateResolver(client, string.Empty);

            SimultriaViewerEnvironmentResolution result =
                await resolver.ResolveAsync(profile);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.EnvironmentId.IsEmpty, Is.True);
            Assert.That(result.ErrorCode, Is.EqualTo(expectedErrorCode));
            Assert.That(client.RequestCount, Is.Zero);
        }

        [Test]
        public async Task MissingDirectoryEnvironmentNeverDefaultsToProduction()
        {
            ConfigureAutomaticProfile();
            profile.BuildDirectoryEnvironmentId = default(ApiEnvironmentId);
            var client = BuildDirectoryClient.Success(
                "build-42",
                "report_viewer",
                "production");

            SimultriaViewerEnvironmentResolution result =
                await CreateResolver(client, "unused").ResolveAsync(profile);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(
                result.ErrorCode,
                Is.EqualTo("build_directory_environment_missing"));
            Assert.That(result.EnvironmentId.IsEmpty, Is.True);
            Assert.That(client.RequestCount, Is.Zero);
        }

        [TestCase("deprecated", "build_environment_unknown")]
        [TestCase("production-like", "build_environment_unknown")]
        [TestCase("testing,production", "build_environment_unknown")]
        [TestCase("production", "resolved_environment_unavailable")]
        public async Task UnknownOrUnconfiguredResponseFailsClosed(
            string backendEnvironment,
            string expectedErrorCode)
        {
            ConfigureAutomaticProfile();
            var client = BuildDirectoryClient.Success(
                "build-42",
                "report_viewer",
                backendEnvironment);

            SimultriaViewerEnvironmentResolution result =
                await CreateResolver(client, "unused").ResolveAsync(profile);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(expectedErrorCode));
            Assert.That(result.EnvironmentId.IsEmpty, Is.True);
        }

        [TestCase("different-build", "report_viewer", "build_version_mismatch")]
        [TestCase("build-42", "activity_viewer", "build_product_mismatch")]
        public async Task MismatchedPortalIdentityFailsClosed(
            string responseVersion,
            string responseProduct,
            string expectedErrorCode)
        {
            ConfigureAutomaticProfile();
            var client = BuildDirectoryClient.Success(
                responseVersion,
                responseProduct,
                "testing");

            SimultriaViewerEnvironmentResolution result =
                await CreateResolver(client, "unused").ResolveAsync(profile);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(expectedErrorCode));
            Assert.That(result.EnvironmentId.IsEmpty, Is.True);
        }

        [Test]
        public async Task TransportFailureIsSanitizedAndFailsClosed()
        {
            ConfigureAutomaticProfile();
            var client = BuildDirectoryClient.Failure(503);

            SimultriaViewerEnvironmentResolution result =
                await CreateResolver(client, "unused").ResolveAsync(profile);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(
                result.ErrorCode,
                Is.EqualTo("build_directory_lookup_failed"));
            Assert.That(result.Message, Does.Contain("HTTP 503"));
            Assert.That(result.Message, Does.Not.Contain("https://"));
            Assert.That(result.EnvironmentId.IsEmpty, Is.True);
        }

        [Test]
        public void AutomaticModeRequiresExplicitResolvedPayloadEnvironment()
        {
            ConfigureAutomaticProfile();

            bool created = profile.TryCreatePayload(
                12,
                out _,
                out string error);

            Assert.That(created, Is.False);
            Assert.That(error, Does.Contain("Resolve"));
        }

        [Test]
        public void UnresolvedAutomaticProfileCannotRegisterOrCreateCommand()
        {
            ConfigureAutomaticProfile();
            ViewerAuthenticationSession session =
                ViewerAuthenticationSession.CreateTransient();

            bool registered =
                SimultriaViewerConnectionAuthentication.TryRegister(
                    profile,
                    session,
                    out IDisposable registration,
                    out _,
                    out string registrationError);
            bool created =
                SimultriaViewerDevelopmentCommandService.TryCreateCommand(
                    profile,
                    out _,
                    out string commandError);

            registration?.Dispose();
            Assert.That(registered, Is.False);
            Assert.That(registrationError, Does.Contain("Resolve"));
            Assert.That(created, Is.False);
            Assert.That(commandError, Does.Contain("Automatic"));
            Assert.That(
                SimultriaViewerConnectionStatus.Capture(profile)
                    .Authentication,
                Is.Null);
        }

        [Test]
        public async Task ResolvedEnvironmentControlsAuthenticationBinding()
        {
            ConfigureAutomaticProfile();
            var client = BuildDirectoryClient.Success(
                "build-42",
                "report_viewer",
                "testing");
            SimultriaViewerEnvironmentResolution result =
                await CreateResolver(client, "unused").ResolveAsync(profile);
            ViewerAuthenticationSession session =
                ViewerAuthenticationSession.CreateTransient();
            IDisposable registration = null;
            try
            {
                Assert.That(result.Succeeded, Is.True, result.Message);
                Assert.That(
                    SimultriaViewerConnectionAuthentication.TryRegister(
                        profile,
                        result.EnvironmentId,
                        session,
                        out registration,
                        out _,
                        out string registrationError),
                    Is.True,
                    registrationError);
                Assert.That(
                    SimultriaViewerConnectionStatus
                        .TryResolveAuthenticationTarget(
                            profile,
                            result.EnvironmentId,
                            out _,
                            out string resolvedError),
                    Is.True,
                    resolvedError);
                Assert.That(
                    SimultriaViewerConnectionStatus
                        .TryResolveAuthenticationTarget(
                            profile,
                            SimultriaEnvironmentIds.Development,
                            out _,
                            out string mismatchError),
                    Is.False);
                Assert.That(mismatchError, Does.Contain("does not match"));
            }
            finally
            {
                registration?.Dispose();
            }
        }

        [Test]
        public async Task AutomaticPreviewContainsResolvedIdButNoConnectionData()
        {
            ConfigureAutomaticProfile();
            var client = BuildDirectoryClient.Success(
                "build-42",
                "report_viewer",
                "testing");

            SimultriaViewerDevelopmentCommandService.DevelopmentCommandCreation
                creation = await SimultriaViewerDevelopmentCommandService
                    .CreateCommandAsync(
                        profile,
                        CreateResolver(client, "unused"),
                        CancellationToken.None);

            Assert.That(creation.Succeeded, Is.True, creation.Message);
            string json = SimultriaViewerInitializationCommand.Serialize(
                creation.Command);
            Assert.That(
                json,
                Does.Contain(SimultriaEnvironmentIds.Testing.Value));
            Assert.That(json, Does.Not.Contain("https://"));
            Assert.That(json.ToLowerInvariant(), Does.Not.Contain("token"));
            Assert.That(json.ToLowerInvariant(), Does.Not.Contain("credential"));
            Assert.That(json.ToLowerInvariant(), Does.Not.Contain("login"));
        }

        private void ConfigureAutomaticProfile()
        {
            profile.EnvironmentResolutionMode =
                SimultriaViewerEnvironmentResolutionMode
                    .AutomaticFromUnityBuildVersion;
            profile.BuildDirectoryEnvironmentId =
                SimultriaEnvironmentIds.Development;
            profile.BuildProduct = "report_viewer";
            profile.BuildVersionOverride = "build-42";
        }

        private SimultriaViewerEnvironmentResolver CreateResolver(
            IApiClient client,
            string buildVersion)
        {
            return new SimultriaViewerEnvironmentResolver(
                client,
                new FixedBuildMetadataProvider(buildVersion));
        }

        private ApiConnectionProfile CreateConnectionProfile(
            params ApiEnvironmentId[] configuredIds)
        {
            SimultriaApiProfile packageProfile =
                SimultriaApiProfileDefaults.Load();
            Assert.That(packageProfile, Is.Not.Null);
            var configured = new HashSet<ApiEnvironmentId>(configuredIds);
            var environments = new List<ApiEnvironmentProfile>();
            foreach (ApiEnvironmentProfile source in packageProfile.Environments)
            {
                if (source == null ||
                    !source.TryGetId(out ApiEnvironmentId environmentId) ||
                    !configured.Contains(environmentId))
                {
                    environments.Add(source);
                    continue;
                }

                ApiEnvironmentProfile clone = Own(
                    UnityEngine.Object.Instantiate(source));
                Assert.That(
                    clone.TryGetClient(
                        SimultriaClientIds.Primary,
                        out ApiNamedClientDefinition client),
                    Is.True);
                client.BaseUrl = "https://simultria-viewer.invalid";
                environments.Add(clone);
            }

            return Own(
                ApiConnectionProfile.CreateTransient(
                    environments,
                    packageProfile.EndpointCatalog,
                    SimultriaEnvironmentDescriptors.Standard));
        }

        private T Own<T>(T instance) where T : UnityEngine.Object
        {
            ownedObjects.Add(instance);
            return instance;
        }

        private sealed class FixedBuildMetadataProvider :
            ISimultriaViewerBuildMetadataProvider
        {
            internal FixedBuildMetadataProvider(string buildVersion)
            {
                BuildVersion = buildVersion;
            }

            public string BuildVersion { get; }
        }

        private sealed class BuildDirectoryClient : IApiClient
        {
            private readonly ApiResult<SimultriaResourceResponse<
                SimultriaUnityBuildVersionDto>> result;

            internal BuildDirectoryClient()
            {
            }

            private BuildDirectoryClient(
                ApiResult<SimultriaResourceResponse<
                    SimultriaUnityBuildVersionDto>> result)
            {
                this.result = result;
            }

            internal int RequestCount { get; private set; }

            internal ApiEndpoint LastEndpoint { get; private set; }

            internal static BuildDirectoryClient Success(
                string version,
                string product,
                string environment)
            {
                var response = new SimultriaResourceResponse<
                    SimultriaUnityBuildVersionDto>
                {
                    Data = new SimultriaUnityBuildVersionDto
                    {
                        Version = version,
                        Product = product,
                        Environment = environment
                    }
                };
                return new BuildDirectoryClient(
                    ApiResult<SimultriaResourceResponse<
                        SimultriaUnityBuildVersionDto>>.Success(
                        response,
                        HttpMethod.GET,
                        200,
                        null,
                        null));
            }

            internal static BuildDirectoryClient Failure(long statusCode)
            {
                return new BuildDirectoryClient(
                    ApiResult<SimultriaResourceResponse<
                        SimultriaUnityBuildVersionDto>>.Failure(
                        new ApiError { HttpStatusCode = statusCode },
                        HttpMethod.GET));
            }

            public Task<ApiResult<TResponse>> SendAsync<TResponse>(
                ApiEndpoint endpoint,
                CancellationToken cancellationToken =
                    default(CancellationToken))
            {
                RequestCount++;
                LastEndpoint = endpoint;
                return Task.FromResult(
                    (ApiResult<TResponse>)(object)result);
            }

            public Task<ApiResult<TResponse>> SendAsync<TResponse>(
                ApiRequest request,
                CancellationToken cancellationToken =
                    default(CancellationToken)) =>
                throw new NotSupportedException();

            public Task<ApiResult<TResponse>> SendAsync<TResponse>(
                ApiEndpoint endpoint,
                object body,
                CancellationToken cancellationToken =
                    default(CancellationToken)) =>
                throw new NotSupportedException();

            public Task<ApiResult<TResponse>> GetAsync<TResponse>(
                string endpoint,
                CancellationToken cancellationToken =
                    default(CancellationToken)) =>
                throw new NotSupportedException();

            public Task<ApiResult<TResponse>> PostAsync<TResponse>(
                string endpoint,
                object body,
                CancellationToken cancellationToken =
                    default(CancellationToken)) =>
                throw new NotSupportedException();

            public Task<ApiResult<TResponse>> PutAsync<TResponse>(
                string endpoint,
                object body,
                CancellationToken cancellationToken =
                    default(CancellationToken)) =>
                throw new NotSupportedException();

            public Task<ApiResult<TResponse>> PatchAsync<TResponse>(
                string endpoint,
                object body,
                CancellationToken cancellationToken =
                    default(CancellationToken)) =>
                throw new NotSupportedException();

            public Task<ApiResult<TResponse>> DeleteAsync<TResponse>(
                string endpoint,
                CancellationToken cancellationToken =
                    default(CancellationToken)) =>
                throw new NotSupportedException();
        }
    }
}
