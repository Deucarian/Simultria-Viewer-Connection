using System;
using System.Collections.Generic;
using Deucarian.API;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Deucarian.SimultriaViewerIntegration.Editor;
using Deucarian.Authentication;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class SimultriaViewerRuntimeConnectionContextTests
    {
        private readonly List<UnityEngine.Object> ownedObjects =
            new List<UnityEngine.Object>();
        private IDisposable hostSuspension;

        [SetUp]
        public void SetUp()
        {
            SimultriaViewerRuntimeEnvironment.ResetForLifecycle();
            SimultriaViewerRuntimeConnectionContext.ResetForLifecycle();
            hostSuspension =
                SimultriaViewerEditorAuthenticationHost.SuspendForTests();
        }

        [TearDown]
        public void TearDown()
        {
            hostSuspension?.Dispose();
            hostSuspension = null;
            SimultriaViewerRuntimeConnectionContext.ResetForLifecycle();
            SimultriaViewerRuntimeEnvironment.ResetForLifecycle();
            for (int index = ownedObjects.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(ownedObjects[index]);
            }

            ownedObjects.Clear();
        }

        [Test]
        public void ProviderLeasePublishesExactClientAndClearsOnDispose()
        {
            ActivateRuntime(SimultriaEnvironmentIds.Development);
            ApiConnectionSettings settings = CreateConnectionSettings(
                SimultriaEnvironmentIds.Development);
            var provider = new SimultriaViewerRuntimeConnectionProvider(
                settings,
                SimultriaEnvironmentIds.Development);
            ViewerRuntimeConnection connection = null;
            try
            {
                Assert.That(
                    provider.TryCreate(out connection, out string error),
                    Is.True,
                    error);
                Assert.That(
                    SimultriaViewerRuntimeConnectionContext.TryGetCurrent(
                        out SimultriaViewerRuntimeConnectionContext context),
                    Is.True);
                Assert.That(context, Is.Not.Null);
                Assert.That(
                    context.EnvironmentId,
                    Is.EqualTo(SimultriaEnvironmentIds.Development));
                Assert.That(context.ApiClient, Is.SameAs(connection.ApiClient));
                Assert.That(context.Composition, Is.Not.Null);
                Assert.That(
                    context.PrimaryClient.ClientId,
                    Is.EqualTo(SimultriaClientIds.Primary));
            }
            finally
            {
                connection?.Dispose();
            }

            Assert.That(
                SimultriaViewerRuntimeConnectionContext.TryGetCurrent(out _),
                Is.False);
        }

        [Test]
        public void DuplicateActivationFailsWithoutReplacingOwner()
        {
            RuntimeInputs inputs = CreateInputs(
                SimultriaEnvironmentIds.Development);
            IDisposable firstRegistration = null;
            try
            {
                Assert.That(
                    TryActivate(
                        inputs,
                        out SimultriaViewerRuntimeConnectionContext first,
                        out firstRegistration,
                        out string firstError),
                    Is.True,
                    firstError);
                Assert.That(
                    TryActivate(
                        inputs,
                        out SimultriaViewerRuntimeConnectionContext duplicate,
                        out IDisposable duplicateRegistration,
                        out string duplicateError),
                    Is.False);
                Assert.That(duplicate, Is.Null);
                Assert.That(duplicateRegistration, Is.Null);
                Assert.That(duplicateError, Does.Contain("already active"));
                Assert.That(
                    SimultriaViewerRuntimeConnectionContext.TryGetCurrent(
                        out SimultriaViewerRuntimeConnectionContext current),
                    Is.True);
                Assert.That(current, Is.SameAs(first));
            }
            finally
            {
                firstRegistration?.Dispose();
            }
        }

        [Test]
        public void EnvironmentMismatchFailsClosed()
        {
            ActivateRuntime(SimultriaEnvironmentIds.Production);
            RuntimeInputs inputs = CreateInputs(
                SimultriaEnvironmentIds.Local);

            Assert.That(
                TryActivate(
                    inputs,
                    out SimultriaViewerRuntimeConnectionContext context,
                    out IDisposable registration,
                    out string error),
                Is.False);
            Assert.That(context, Is.Null);
            Assert.That(registration, Is.Null);
            Assert.That(error, Does.Contain("active runtime environment"));
            Assert.That(
                SimultriaViewerRuntimeConnectionContext.TryGetCurrent(out _),
                Is.False);
        }

        [Test]
        public void PrimaryClientMismatchFailsClosed()
        {
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Development,
                SimultriaEnvironmentIds.Testing);
            Assert.That(
                composition.TryResolveClient(
                    SimultriaEnvironmentIds.Testing,
                    SimultriaClientIds.Primary,
                    out ApiResolvedClient testingClient,
                    out string resolveError),
                Is.True,
                resolveError);

            Assert.That(
                SimultriaViewerRuntimeConnectionContext.TryActivate(
                    SimultriaEnvironmentIds.Development,
                    testingClient,
                    composition,
                    CreateApiClient(),
                    out SimultriaViewerRuntimeConnectionContext context,
                    out IDisposable registration,
                    out string error),
                Is.False);
            Assert.That(context, Is.Null);
            Assert.That(registration, Is.Null);
            Assert.That(error, Does.Contain("incomplete or inconsistent"));
        }

        [Test]
        public void SameBaseUrlWithDifferentHeadersFailsClosed()
        {
            ApiComposition expectedComposition = CreateCompositionWithHeader(
                "expected");
            ApiComposition otherComposition = CreateCompositionWithHeader(
                "other");
            Assert.That(
                otherComposition.TryResolveClient(
                    SimultriaEnvironmentIds.Development,
                    SimultriaClientIds.Primary,
                    out ApiResolvedClient otherClient,
                    out string resolveError),
                Is.True,
                resolveError);

            Assert.That(
                SimultriaViewerRuntimeConnectionContext.TryActivate(
                    SimultriaEnvironmentIds.Development,
                    otherClient,
                    expectedComposition,
                    CreateApiClient(),
                    out SimultriaViewerRuntimeConnectionContext context,
                    out IDisposable registration,
                    out string error),
                Is.False);
            Assert.That(context, Is.Null);
            Assert.That(registration, Is.Null);
            Assert.That(error, Does.Contain("does not match"));
        }

        [Test]
        public void SameBaseUrlWithDifferentRequestPolicyFailsClosed()
        {
            ApiComposition expectedComposition = CreateCompositionWithPolicy(
                20);
            ApiComposition otherComposition = CreateCompositionWithPolicy(40);
            Assert.That(
                otherComposition.TryResolveClient(
                    SimultriaEnvironmentIds.Development,
                    SimultriaClientIds.Primary,
                    out ApiResolvedClient otherClient,
                    out string resolveError),
                Is.True,
                resolveError);

            Assert.That(
                SimultriaViewerRuntimeConnectionContext.TryActivate(
                    SimultriaEnvironmentIds.Development,
                    otherClient,
                    expectedComposition,
                    CreateApiClient(),
                    out SimultriaViewerRuntimeConnectionContext context,
                    out IDisposable registration,
                    out string error),
                Is.False);
            Assert.That(context, Is.Null);
            Assert.That(registration, Is.Null);
            Assert.That(error, Does.Contain("does not match"));
        }

        [Test]
        public void StaleDisposalCannotClearNewLeaseIdentity()
        {
            RuntimeInputs inputs = CreateInputs(
                SimultriaEnvironmentIds.Acceptance);
            Assert.That(
                TryActivate(
                    inputs,
                    out SimultriaViewerRuntimeConnectionContext first,
                    out IDisposable firstRegistration,
                    out string firstError),
                Is.True,
                firstError);

            SimultriaViewerRuntimeConnectionContext.ResetForLifecycle(first);
            Assert.That(
                TryActivate(
                    inputs,
                    out SimultriaViewerRuntimeConnectionContext second,
                    out IDisposable secondRegistration,
                    out string secondError),
                Is.True,
                secondError);
            try
            {
                firstRegistration.Dispose();
                Assert.That(
                    SimultriaViewerRuntimeConnectionContext.TryGetCurrent(
                        out SimultriaViewerRuntimeConnectionContext current),
                    Is.True);
                Assert.That(current, Is.SameAs(second));
                Assert.That(current, Is.Not.SameAs(first));
            }
            finally
            {
                secondRegistration.Dispose();
            }
        }

        [Test]
        public void CoordinatorContextOverloadReusesAllLeaseObjects()
        {
            ActivateRuntime(SimultriaEnvironmentIds.Local);
            RuntimeInputs inputs = CreateInputs(SimultriaEnvironmentIds.Local);
            Assert.That(
                TryActivate(
                    inputs,
                    out SimultriaViewerRuntimeConnectionContext context,
                    out IDisposable registration,
                    out string error),
                Is.True,
                error);
            try
            {
                var coordinator =
                    new SimultriaViewerModelInitializationCoordinator();
                SimultriaViewerModelInitializationPlan plan =
                    coordinator.Prepare(
                        new SimultriaViewerInitializationPayload
                        {
                            Revision = 1,
                            EnvironmentId =
                                SimultriaEnvironmentIds.Local.Value,
                            ProjectId = 1,
                            ModelId = 2
                        },
                        context,
                        new PresentAuthProvider());

                Assert.That(plan.Succeeded, Is.True, plan.Message);
                Assert.That(plan.Composition, Is.SameAs(inputs.Composition));
                Assert.That(plan.ApiClient, Is.SameAs(inputs.ApiClient));
                Assert.That(
                    plan.PrimaryClient,
                    Is.SameAs(inputs.PrimaryClient));
                Assert.That(
                    plan.EnvironmentId,
                    Is.EqualTo(SimultriaEnvironmentIds.Local));
            }
            finally
            {
                registration.Dispose();
            }
        }

        [Test]
        public void LifetimeClearsContextBeforeAuthenticationRegistration()
        {
            RuntimeInputs inputs = CreateInputs(
                SimultriaEnvironmentIds.Development);
            Assert.That(
                TryActivate(
                    inputs,
                    out _,
                    out IDisposable contextRegistration,
                    out string error),
                Is.True,
                error);
            bool contextVisibleDuringTargetDisposal = true;
            bool released = false;
            var targetRegistration = new CallbackDisposable(() =>
            {
                contextVisibleDuringTargetDisposal =
                    SimultriaViewerRuntimeConnectionContext.TryGetCurrent(
                        out _);
            });
            var lifetime = new SimultriaViewerRuntimeConnectionLifetime(
                targetRegistration,
                contextRegistration,
                AuthenticationSession.CreateTransient(),
                () => released = true);

            lifetime.Dispose();

            Assert.That(contextVisibleDuringTargetDisposal, Is.False);
            Assert.That(released, Is.True);
            Assert.That(
                SimultriaViewerRuntimeConnectionContext.TryGetCurrent(out _),
                Is.False);
        }

        private RuntimeInputs CreateInputs(ApiEnvironmentId environmentId)
        {
            ApiComposition composition = CreateComposition(environmentId);
            Assert.That(
                composition.TryResolveClient(
                    environmentId,
                    SimultriaClientIds.Primary,
                    out ApiResolvedClient primaryClient,
                    out string error),
                Is.True,
                error);
            return new RuntimeInputs(
                environmentId,
                primaryClient,
                composition,
                CreateApiClient());
        }

        private static bool TryActivate(
            RuntimeInputs inputs,
            out SimultriaViewerRuntimeConnectionContext context,
            out IDisposable registration,
            out string error) =>
            SimultriaViewerRuntimeConnectionContext.TryActivate(
                inputs.EnvironmentId,
                inputs.PrimaryClient,
                inputs.Composition,
                inputs.ApiClient,
                out context,
                out registration,
                out error);

        private IApiClient CreateApiClient()
        {
            ApiClientConfig config = Own(
                ApiClientConfig.CreateRuntimeDefault());
            return ApiClientFactory.Create(config, new PresentAuthProvider());
        }

        private ApiComposition CreateComposition(
            params ApiEnvironmentId[] configuredIds)
        {
            ApiConnectionSettings settings = CreateConnectionSettings(
                configuredIds);
            Assert.That(
                SimultriaApiConnectionSettingsAdapter.TryCreateComposition(
                    settings,
                    out ApiComposition composition,
                    out string error),
                Is.True,
                error);
            return composition;
        }

        private ApiComposition CreateCompositionWithHeader(string value)
        {
            ApiConnectionSettings settings = CreateConnectionSettings(
                value,
                SimultriaEnvironmentIds.Development);
            Assert.That(
                SimultriaApiConnectionSettingsAdapter.TryCreateComposition(
                    settings,
                    out ApiComposition composition,
                    out string error),
                Is.True,
                error);
            return composition;
        }

        private ApiComposition CreateCompositionWithPolicy(
            int timeoutSeconds)
        {
            ApiConnectionSettings settings = CreateConnectionSettings(
                null,
                timeoutSeconds,
                SimultriaEnvironmentIds.Development);
            Assert.That(
                SimultriaApiConnectionSettingsAdapter.TryCreateComposition(
                    settings,
                    out ApiComposition composition,
                    out string error),
                Is.True,
                error);
            return composition;
        }

        private ApiConnectionSettings CreateConnectionSettings(
            params ApiEnvironmentId[] configuredIds)
        {
            return CreateConnectionSettings(null, configuredIds);
        }

        private ApiConnectionSettings CreateConnectionSettings(
            string headerValue,
            params ApiEnvironmentId[] configuredIds)
        {
            return CreateConnectionSettings(
                headerValue,
                null,
                configuredIds);
        }

        private ApiConnectionSettings CreateConnectionSettings(
            string headerValue,
            int? timeoutSeconds,
            params ApiEnvironmentId[] configuredIds)
        {
            ApiServiceDefinition definition =
                SimultriaApiDefinitionDefaults.LoadServiceDefinition();
            Assert.That(
                definition.TryGetEnvironmentDescriptors(
                    out IReadOnlyList<ApiEnvironmentDescriptor> descriptors,
                    out string error),
                Is.True,
                error);
            var configured = new HashSet<ApiEnvironmentId>(configuredIds);
            var environments = new List<ApiEnvironmentProfile>();
            foreach (ApiEnvironmentDescriptor descriptor in descriptors)
            {
                ApiEnvironmentProfile environment = Own(
                    ScriptableObject.CreateInstance<ApiEnvironmentProfile>());
                environment.EnvironmentId = descriptor.EnvironmentId.Value;
                environment.DisplayName = descriptor.DisplayName;
                var client = new ApiNamedClientDefinition
                {
                    ClientId = SimultriaClientIds.Primary.Value,
                    BaseUrl = configured.Contains(descriptor.EnvironmentId)
                        ? "https://runtime-context.example.invalid"
                        : string.Empty
                };
                if (headerValue != null &&
                    descriptor.EnvironmentId ==
                    SimultriaEnvironmentIds.Development)
                {
                    client.DefaultHeaders.Add(new ApiKeyValuePair
                    {
                        Key = "X-Context-Test",
                        Value = headerValue
                    });
                }

                if (timeoutSeconds.HasValue &&
                    descriptor.EnvironmentId ==
                    SimultriaEnvironmentIds.Development)
                {
                    client.RequestPolicy.TimeoutSeconds =
                        timeoutSeconds.Value;
                }

                environment.Clients.Add(client);
                environments.Add(environment);
            }

            return Own(ApiConnectionSettings.CreateTransient(
                environments,
                definition));
        }

        private static void ActivateRuntime(ApiEnvironmentId environmentId)
        {
            SimultriaViewerEnvironmentResolution resolution =
                SimultriaViewerEnvironmentResolution.Success(
                    SimultriaViewerEnvironmentResolutionMode.Manual,
                    environmentId,
                    "test-build",
                    "viewer",
                    "test",
                    SimultriaViewerRuntimeKind.Editor,
                    "Test Viewer",
                    true);
            Assert.That(
                SimultriaViewerRuntimeEnvironment.TryActivate(
                    resolution,
                    out string error),
                Is.True,
                error);
        }

        private T Own<T>(T instance) where T : UnityEngine.Object
        {
            ownedObjects.Add(instance);
            return instance;
        }

        private sealed class RuntimeInputs
        {
            internal RuntimeInputs(
                ApiEnvironmentId environmentId,
                ApiResolvedClient primaryClient,
                ApiComposition composition,
                IApiClient apiClient)
            {
                EnvironmentId = environmentId;
                PrimaryClient = primaryClient;
                Composition = composition;
                ApiClient = apiClient;
            }

            internal ApiEnvironmentId EnvironmentId { get; }
            internal ApiResolvedClient PrimaryClient { get; }
            internal ApiComposition Composition { get; }
            internal IApiClient ApiClient { get; }
        }

        private sealed class PresentAuthProvider :
            Deucarian.API.Authentication.IApiAuthProvider
        {
            public System.Threading.Tasks.Task<string> GetAccessTokenAsync(
                System.Threading.CancellationToken cancellationToken) =>
                System.Threading.Tasks.Task.FromResult(string.Empty);
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action callback;

            internal CallbackDisposable(Action disposeCallback)
            {
                callback = disposeCallback;
            }

            public void Dispose()
            {
                Action current = callback;
                callback = null;
                current?.Invoke();
            }
        }
    }
}
