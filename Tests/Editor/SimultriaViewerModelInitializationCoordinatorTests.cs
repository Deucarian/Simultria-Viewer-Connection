using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.API;
using Deucarian.API.Authentication;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Deucarian.Simultria.API.Models;
using Deucarian.Simultria.API.Services;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class SimultriaViewerModelInitializationCoordinatorTests
    {
        private readonly List<UnityEngine.Object> ownedObjects =
            new List<UnityEngine.Object>();
        private readonly IApiAuthProvider authProvider =
            new PresentAuthProvider();

        [SetUp]
        public void SetUp()
        {
            SimultriaViewerRuntimeEnvironment.ResetForLifecycle();
            SimultriaViewerRuntimeConnectionContext.ResetForLifecycle();
        }

        [TearDown]
        public void TearDown()
        {
            SimultriaViewerRuntimeConnectionContext.ResetForLifecycle();
            SimultriaViewerRuntimeEnvironment.ResetForLifecycle();
            for (int index = ownedObjects.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(ownedObjects[index]);
            }

            ownedObjects.Clear();
        }

        [Test]
        public void InvalidPayloadFailsBeforeAnyCompositionWork()
        {
            bool clientCreated = false;
            var coordinator = new SimultriaViewerModelInitializationCoordinator(
                (config, auth) =>
                {
                    clientCreated = true;
                    return CreateApiClient();
                },
                (client, composition, environment) =>
                    CreateSuccessfulResolver());

            SimultriaViewerModelInitializationPlan plan = coordinator.Prepare(
                null,
                CreateConnectionSettings(
                    SimultriaEnvironmentIds.Development),
                null,
                authProvider);

            AssertFailure(plan, "invalid_payload");
            Assert.That(clientCreated, Is.False);
        }

        [Test]
        public void ActiveRuntimeEnvironmentAcceptsBlankPayloadEnvironment()
        {
            Activate(SimultriaEnvironmentIds.Testing);
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Testing);

            SimultriaViewerModelInitializationPlan plan = Coordinator()
                .Prepare(
                    Payload(string.Empty),
                    composition,
                    CreateApiClient(),
                    authProvider);

            Assert.That(plan.Succeeded, Is.True, plan.Message);
            Assert.That(
                plan.EnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Testing));
        }

        [Test]
        public void ActiveRuntimeEnvironmentAcceptsExplicitMatch()
        {
            Activate(SimultriaEnvironmentIds.Production);
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Production);

            SimultriaViewerModelInitializationPlan plan = Coordinator()
                .Prepare(
                    Payload(SimultriaEnvironmentIds.Production.Value),
                    composition,
                    CreateApiClient(),
                    authProvider);

            Assert.That(plan.Succeeded, Is.True, plan.Message);
            Assert.That(
                plan.EnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Production));
        }

        [Test]
        public void ActiveRuntimeEnvironmentRejectsExplicitMismatch()
        {
            Activate(SimultriaEnvironmentIds.Acceptance);
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Acceptance,
                SimultriaEnvironmentIds.Production);

            SimultriaViewerModelInitializationPlan plan = Coordinator()
                .Prepare(
                    Payload(SimultriaEnvironmentIds.Production.Value),
                    composition,
                    CreateApiClient(),
                    authProvider);

            AssertFailure(plan, "environment_mismatch");
        }

        [Test]
        public void PayloadEnvironmentIsRequiredWhenRuntimeIsAbsent()
        {
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Development);

            SimultriaViewerModelInitializationPlan plan = Coordinator()
                .Prepare(
                    Payload("  "),
                    composition,
                    CreateApiClient(),
                    authProvider);

            AssertFailure(plan, "environment_unresolved");
        }

        [Test]
        public void InvalidPayloadEnvironmentFailsClosed()
        {
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Development);

            SimultriaViewerModelInitializationPlan plan = Coordinator()
                .Prepare(
                    Payload("not a valid environment"),
                    composition,
                    CreateApiClient(),
                    authProvider);

            AssertFailure(plan, "environment_invalid");
        }

        [Test]
        public void PayloadEnvironmentIsUsedWithoutRuntimeFallback()
        {
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Testing);

            SimultriaViewerModelInitializationPlan plan = Coordinator()
                .Prepare(
                    Payload(SimultriaEnvironmentIds.Testing.Value),
                    composition,
                    CreateApiClient(),
                    authProvider);

            Assert.That(plan.Succeeded, Is.True, plan.Message);
            Assert.That(
                plan.EnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Testing));
        }

        [Test]
        public void ConfiguredLocalRemainsFirstClassLocal()
        {
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Local,
                SimultriaEnvironmentIds.Development);

            SimultriaViewerModelInitializationPlan plan = Coordinator()
                .Prepare(
                    Payload(SimultriaEnvironmentIds.Local.Value),
                    composition,
                    CreateApiClient(),
                    authProvider);

            Assert.That(plan.Succeeded, Is.True, plan.Message);
            Assert.That(plan.EnvironmentId, Is.EqualTo(
                SimultriaEnvironmentIds.Local));
            Assert.That(
                plan.Composition.GetEnvironmentStatus(plan.EnvironmentId)
                    .Stage,
                Is.EqualTo(ApiEnvironmentStage.Local));
            Assert.That(
                plan.Composition.GetEnvironmentStatus(plan.EnvironmentId)
                    .Stage,
                Is.Not.EqualTo(ApiEnvironmentStage.Custom));
        }

        [Test]
        public void UnconfiguredLocalNeverBorrowsDevelopment()
        {
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Development);

            SimultriaViewerModelInitializationPlan plan = Coordinator()
                .Prepare(
                    Payload(SimultriaEnvironmentIds.Local.Value),
                    composition,
                    CreateApiClient(),
                    authProvider);

            AssertFailure(plan, "environment_not_configured");
        }

        [Test]
        public void MissingPrimaryClientFailsClosed()
        {
            ApiServiceDefinition definition =
                SimultriaApiDefinitionDefaults.LoadServiceDefinition();
            ApiEnvironmentProfile environment = Own(
                ScriptableObject.CreateInstance<ApiEnvironmentProfile>());
            environment.EnvironmentId =
                SimultriaEnvironmentIds.Development.Value;
            environment.DisplayName = "Development";
            environment.Clients.Add(new ApiNamedClientDefinition
            {
                ClientId = "secondary",
                BaseUrl = "https://secondary.example.invalid"
            });
            var composition = new ApiComposition(
                new[] { environment },
                definition.EndpointCatalog);

            SimultriaViewerModelInitializationPlan plan = Coordinator()
                .Prepare(
                    Payload(SimultriaEnvironmentIds.Development.Value),
                    composition,
                    CreateApiClient(),
                    authProvider);

            AssertFailure(plan, "primary_client_unavailable");
        }

        [Test]
        public void MissingAuthenticationFailsBeforeResolverCreation()
        {
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Development);
            bool resolverCreated = false;
            var coordinator = new SimultriaViewerModelInitializationCoordinator(
                (config, auth) => CreateApiClient(),
                (client, composed, environment) =>
                {
                    resolverCreated = true;
                    return CreateSuccessfulResolver();
                });

            SimultriaViewerModelInitializationPlan plan = coordinator.Prepare(
                Payload(SimultriaEnvironmentIds.Development.Value),
                composition,
                CreateApiClient(),
                null);

            AssertFailure(plan, "authentication_unavailable");
            Assert.That(resolverCreated, Is.False);
        }

        [Test]
        public void ExistingCompositionAndClientKeepExactIdentity()
        {
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Development);
            IApiClient apiClient = CreateApiClient();

            SimultriaViewerModelInitializationPlan plan = Coordinator()
                .Prepare(
                    Payload(SimultriaEnvironmentIds.Development.Value),
                    composition,
                    apiClient,
                    authProvider);

            Assert.That(plan.Succeeded, Is.True, plan.Message);
            Assert.That(plan.Composition, Is.SameAs(composition));
            Assert.That(plan.ApiClient, Is.SameAs(apiClient));
            Assert.That(
                plan.PrimaryClient.EnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Development));
            Assert.That(
                plan.PrimaryClient.ClientId,
                Is.EqualTo(SimultriaClientIds.Primary));
        }

        [Test]
        public void SettingsOverloadUsesInjectedClientAndCompositionPolicy()
        {
            ApiConnectionSettings settings = CreateConnectionSettings(
                SimultriaEnvironmentIds.Testing);
            IApiClient expectedClient = CreateApiClient();
            var coordinator = new SimultriaViewerModelInitializationCoordinator(
                (config, auth) => expectedClient,
                (client, composition, environment) =>
                    CreateSuccessfulResolver());

            SimultriaViewerModelInitializationPlan plan = coordinator.Prepare(
                Payload(SimultriaEnvironmentIds.Testing.Value),
                settings,
                null,
                authProvider);

            Assert.That(plan.Succeeded, Is.True, plan.Message);
            Assert.That(plan.ApiClient, Is.SameAs(expectedClient));
            Assert.That(
                plan.EnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Testing));
        }

        [Test]
        public async Task PlanDelegatesToCanonicalResolver()
        {
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Development);
            IApiClient apiClient = CreateApiClient();
            IApiClient observedClient = null;
            ApiComposition observedComposition = null;
            ApiEnvironmentId observedEnvironment = default;
            SimultriaViewerModelInitializationResolver resolver =
                CreateSuccessfulResolver();
            var coordinator = new SimultriaViewerModelInitializationCoordinator(
                (config, auth) => apiClient,
                (client, composed, environment) =>
                {
                    observedClient = client;
                    observedComposition = composed;
                    observedEnvironment = environment;
                    return resolver;
                });

            SimultriaViewerModelInitializationPlan plan = coordinator.Prepare(
                Payload(SimultriaEnvironmentIds.Development.Value),
                composition,
                apiClient,
                authProvider);
            SimultriaViewerModelInitializationResolution resolved =
                await plan.ResolveAsync(CancellationToken.None);

            Assert.That(plan.Resolver, Is.SameAs(resolver));
            Assert.That(observedClient, Is.SameAs(apiClient));
            Assert.That(observedComposition, Is.SameAs(composition));
            Assert.That(
                observedEnvironment,
                Is.EqualTo(SimultriaEnvironmentIds.Development));
            Assert.That(resolved.Succeeded, Is.True, resolved.Message);
            Assert.That(resolved.ModelVersionId, Is.EqualTo(17));
        }

        [Test]
        public async Task PlanPinsValidatedPayloadBeforeCallerMutation()
        {
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Development);
            SimultriaViewerInitializationPayload payload = Payload(
                SimultriaEnvironmentIds.Development.Value);
            SimultriaViewerModelInitializationPlan plan = Coordinator()
                .Prepare(
                    payload,
                    composition,
                    CreateApiClient(),
                    authProvider);

            payload.EnvironmentId =
                SimultriaEnvironmentIds.Production.Value;
            payload.ProjectId = 999;
            payload.ModelId = 999;

            SimultriaViewerModelInitializationResolution resolved =
                await plan.ResolveAsync(CancellationToken.None);

            Assert.That(resolved.Succeeded, Is.True, resolved.Message);
            Assert.That(resolved.ProjectId, Is.EqualTo(1));
            Assert.That(resolved.ModelId, Is.EqualTo(2));
            Assert.That(
                plan.EnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Development));
        }

        [Test]
        public async Task ContextPlanFailsAfterOwningLeaseIsCleared()
        {
            Activate(SimultriaEnvironmentIds.Development);
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Development);
            Assert.That(
                composition.TryResolveClient(
                    SimultriaEnvironmentIds.Development,
                    SimultriaClientIds.Primary,
                    out ApiResolvedClient primary,
                    out string resolveError),
                Is.True,
                resolveError);
            IApiClient apiClient = CreateApiClient();
            Assert.That(
                SimultriaViewerRuntimeConnectionContext.TryActivate(
                    SimultriaEnvironmentIds.Development,
                    primary,
                    composition,
                    apiClient,
                    out SimultriaViewerRuntimeConnectionContext context,
                    out IDisposable registration,
                    out string activationError),
                Is.True,
                activationError);
            SimultriaViewerModelInitializationPlan plan = Coordinator()
                .Prepare(
                    Payload(SimultriaEnvironmentIds.Development.Value),
                    context,
                    authProvider);

            registration.Dispose();
            SimultriaViewerModelInitializationResolution resolved =
                await plan.ResolveAsync(CancellationToken.None);

            Assert.That(resolved.Succeeded, Is.False);
            Assert.That(
                resolved.ErrorCode,
                Is.EqualTo("runtime_connection_unavailable"));
        }

        [Test]
        public async Task ContextPlanFailsWhenLeaseClearsDuringResolution()
        {
            Activate(SimultriaEnvironmentIds.Development);
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Development);
            Assert.That(
                composition.TryResolveClient(
                    SimultriaEnvironmentIds.Development,
                    SimultriaClientIds.Primary,
                    out ApiResolvedClient primary,
                    out string resolveError),
                Is.True,
                resolveError);
            IApiClient apiClient = CreateApiClient();
            var resolverStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var resolverCompletion = new TaskCompletionSource<
                SimultriaViewerModelResolveResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var resolver = new SimultriaViewerModelInitializationResolver(
                (projectId, modelId, versionId, cancellationToken) =>
                {
                    resolverStarted.TrySetResult(true);
                    return resolverCompletion.Task;
                });
            var coordinator = new SimultriaViewerModelInitializationCoordinator(
                (config, auth) => apiClient,
                (client, composed, environment) => resolver);
            Assert.That(
                SimultriaViewerRuntimeConnectionContext.TryActivate(
                    SimultriaEnvironmentIds.Development,
                    primary,
                    composition,
                    apiClient,
                    out SimultriaViewerRuntimeConnectionContext context,
                    out IDisposable registration,
                    out string activationError),
                Is.True,
                activationError);
            try
            {
                SimultriaViewerModelInitializationPlan plan =
                    coordinator.Prepare(
                        Payload(SimultriaEnvironmentIds.Development.Value),
                        context,
                        authProvider);
                Task<SimultriaViewerModelInitializationResolution> pending =
                    plan.ResolveAsync(CancellationToken.None);

                await resolverStarted.Task;
                registration.Dispose();
                registration = null;
                resolverCompletion.TrySetResult(
                    CreateSuccessfulResolveResult());
                SimultriaViewerModelInitializationResolution resolved =
                    await pending;

                Assert.That(resolved.Succeeded, Is.False);
                Assert.That(
                    resolved.ErrorCode,
                    Is.EqualTo("runtime_connection_unavailable"));
            }
            finally
            {
                registration?.Dispose();
                resolverCompletion.TrySetCanceled();
            }
        }

        [Test]
        public void PlanPropagatesCancellation()
        {
            ApiComposition composition = CreateComposition(
                SimultriaEnvironmentIds.Development);
            var resolver = new SimultriaViewerModelInitializationResolver(
                (projectId, modelId, versionId, cancellationToken) =>
                    Task.FromCanceled<SimultriaViewerModelResolveResult>(
                        new CancellationToken(true)));
            var coordinator = new SimultriaViewerModelInitializationCoordinator(
                (config, auth) => CreateApiClient(),
                (client, composed, environment) => resolver);
            SimultriaViewerModelInitializationPlan plan = coordinator.Prepare(
                Payload(SimultriaEnvironmentIds.Development.Value),
                composition,
                CreateApiClient(),
                authProvider);

            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await plan.ResolveAsync(new CancellationToken(true)));
        }

        [Test]
        public async Task FactoryAndResolverFailuresRemainSanitized()
        {
            const string unsafeDetail =
                "https://should-not-escape.invalid/internal";
            ApiConnectionSettings settings = CreateConnectionSettings(
                SimultriaEnvironmentIds.Development);
            var clientFailure = new SimultriaViewerModelInitializationCoordinator(
                (config, auth) => throw new InvalidOperationException(
                    unsafeDetail),
                (client, composition, environment) =>
                    CreateSuccessfulResolver());
            SimultriaViewerModelInitializationPlan failedClient =
                clientFailure.Prepare(
                    Payload(SimultriaEnvironmentIds.Development.Value),
                    settings,
                    null,
                    authProvider);
            AssertFailure(failedClient, "api_client_creation_failed");
            Assert.That(failedClient.Message, Does.Not.Contain(unsafeDetail));

            var throwingResolver = new SimultriaViewerModelInitializationResolver(
                (projectId, modelId, versionId, cancellationToken) =>
                    throw new InvalidOperationException(unsafeDetail));
            var resolutionFailure = new SimultriaViewerModelInitializationCoordinator(
                (config, auth) => CreateApiClient(),
                (client, composition, environment) => throwingResolver);
            SimultriaViewerModelInitializationPlan plan =
                resolutionFailure.Prepare(
                    Payload(SimultriaEnvironmentIds.Development.Value),
                    CreateComposition(SimultriaEnvironmentIds.Development),
                    CreateApiClient(),
                    authProvider);
            SimultriaViewerModelInitializationResolution resolution =
                await plan.ResolveAsync(CancellationToken.None);

            Assert.That(resolution.Succeeded, Is.False);
            Assert.That(
                resolution.ErrorCode,
                Is.EqualTo("model_resolution_failed"));
            Assert.That(resolution.Message, Does.Not.Contain(unsafeDetail));
            Assert.That(resolution.Message, Does.Not.Contain("http"));
        }

        [Test]
        public async Task ExecutionRejectsStaleRevisionBeforeComposition()
        {
            bool clientCreated = false;
            bool applicationInvoked = false;
            var coordinator = new SimultriaViewerModelInitializationCoordinator(
                (config, auth) =>
                {
                    clientCreated = true;
                    return CreateApiClient();
                },
                (client, composition, environment) =>
                    CreateSuccessfulResolver());

            SimultriaViewerModelInitializationExecutionResult result =
                await coordinator.ExecuteAsync(
                    Payload(SimultriaEnvironmentIds.Development.Value),
                    1,
                    CreateConnectionSettings(
                        SimultriaEnvironmentIds.Development),
                    null,
                    authProvider,
                    true,
                    (context, cancellationToken) =>
                    {
                        applicationInvoked = true;
                        return Task.FromResult(
                            SimultriaViewerApplicationInitializationResult
                                .Success());
                    });

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("stale_revision"));
            Assert.That(clientCreated, Is.False);
            Assert.That(applicationInvoked, Is.False);
        }

        [Test]
        public async Task ExecutionNullPayloadPrecedesAllOtherPreconditions()
        {
            SimultriaViewerModelInitializationExecutionResult result =
                await Coordinator().ExecuteAsync(
                    null,
                    long.MaxValue,
                    null,
                    null,
                    null,
                    false,
                    null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("invalid_payload"));
        }

        [Test]
        public async Task ExecutionDefersFullValidationUntilRequestChecksPass()
        {
            SimultriaViewerInitializationPayload invalid =
                Payload(SimultriaEnvironmentIds.Development.Value);
            invalid.ModelId = 0;
            ApiConnectionSettings settings = CreateConnectionSettings(
                SimultriaEnvironmentIds.Development);
            Func<
                SimultriaViewerModelInitializationExecutionContext,
                CancellationToken,
                Task<SimultriaViewerApplicationInitializationResult>>
                application = (context, cancellationToken) =>
                    Task.FromResult(
                        SimultriaViewerApplicationInitializationResult
                            .Success());

            SimultriaViewerModelInitializationExecutionResult stale =
                await Coordinator().ExecuteAsync(
                    invalid,
                    invalid.Revision,
                    null,
                    null,
                    authProvider,
                    false,
                    null);
            Assert.That(stale.ErrorCode, Is.EqualTo("stale_revision"));

            SimultriaViewerModelInitializationExecutionResult noSettings =
                await Coordinator().ExecuteAsync(
                    invalid,
                    0,
                    null,
                    null,
                    authProvider,
                    false,
                    null);
            Assert.That(
                noSettings.ErrorCode,
                Is.EqualTo("connection_settings_missing"));

            SimultriaViewerModelInitializationExecutionResult noApplication =
                await Coordinator().ExecuteAsync(
                    invalid,
                    0,
                    settings,
                    null,
                    authProvider,
                    false,
                    null);
            Assert.That(
                noApplication.ErrorCode,
                Is.EqualTo("application_initialization_unavailable"));

            SimultriaViewerModelInitializationExecutionResult invalidPayload =
                await Coordinator().ExecuteAsync(
                    invalid,
                    0,
                    settings,
                    null,
                    authProvider,
                    true,
                    application);
            Assert.That(
                invalidPayload.ErrorCode,
                Is.EqualTo("invalid_payload"));
        }

        [Test]
        public async Task ExecutionRuntimeGuidancePrecedesFullValidation()
        {
            SimultriaViewerInitializationPayload invalid =
                Payload(SimultriaEnvironmentIds.Development.Value);
            invalid.ModelId = 0;
            ApiConnectionSettings settings = CreateConnectionSettings(
                SimultriaEnvironmentIds.Development);
            Func<
                SimultriaViewerModelInitializationExecutionContext,
                CancellationToken,
                Task<SimultriaViewerApplicationInitializationResult>>
                application = (context, cancellationToken) =>
                    Task.FromResult(
                        SimultriaViewerApplicationInitializationResult
                            .Success());

            SimultriaViewerModelInitializationExecutionResult unresolved =
                await Coordinator().ExecuteAsync(
                    invalid,
                    0,
                    settings,
                    null,
                    authProvider,
                    false,
                    application);
            Assert.That(
                unresolved.ErrorCode,
                Is.EqualTo("environment_unresolved"));

            Activate(SimultriaEnvironmentIds.Development);
            SimultriaViewerModelInitializationExecutionResult unavailable =
                await Coordinator().ExecuteAsync(
                    invalid,
                    0,
                    settings,
                    null,
                    authProvider,
                    true,
                    application);
            Assert.That(
                unavailable.ErrorCode,
                Is.EqualTo("runtime_connection_unavailable"));
        }

        [Test]
        public async Task ExecutionNeverFallsBackBehindActiveEnvironment()
        {
            Activate(SimultriaEnvironmentIds.Development);
            bool applicationInvoked = false;

            SimultriaViewerModelInitializationExecutionResult result =
                await Coordinator().ExecuteAsync(
                    Payload(SimultriaEnvironmentIds.Development.Value),
                    0,
                    CreateConnectionSettings(
                        SimultriaEnvironmentIds.Development),
                    null,
                    authProvider,
                    true,
                    (context, cancellationToken) =>
                    {
                        applicationInvoked = true;
                        return Task.FromResult(
                            SimultriaViewerApplicationInitializationResult
                                .Success());
                    });

            Assert.That(result.Succeeded, Is.False);
            Assert.That(
                result.ErrorCode,
                Is.EqualTo("runtime_connection_unavailable"));
            Assert.That(applicationInvoked, Is.False);
        }

        [Test]
        public async Task ExecutionUnleasedFallbackRequiresExplicitOptIn()
        {
            ApiConnectionSettings settings = CreateConnectionSettings(
                SimultriaEnvironmentIds.Local);
            bool applicationInvoked = false;
            Func<
                SimultriaViewerModelInitializationExecutionContext,
                CancellationToken,
                Task<SimultriaViewerApplicationInitializationResult>>
                application = (context, cancellationToken) =>
                {
                    applicationInvoked = true;
                    return Task.FromResult(
                        SimultriaViewerApplicationInitializationResult
                            .Success());
                };

            SimultriaViewerModelInitializationExecutionResult denied =
                await Coordinator().ExecuteAsync(
                    Payload(SimultriaEnvironmentIds.Local.Value),
                    0,
                    settings,
                    null,
                    authProvider,
                    false,
                    application);

            Assert.That(denied.Succeeded, Is.False);
            Assert.That(
                denied.ErrorCode,
                Is.EqualTo("environment_unresolved"));
            Assert.That(applicationInvoked, Is.False);

            SimultriaViewerModelInitializationExecutionResult allowed =
                await Coordinator().ExecuteAsync(
                    Payload(SimultriaEnvironmentIds.Local.Value),
                    0,
                    settings,
                    null,
                    authProvider,
                    true,
                    application);

            Assert.That(allowed.Succeeded, Is.True, allowed.Message);
            Assert.That(
                allowed.EnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Local));
            Assert.That(applicationInvoked, Is.True);
        }

        [Test]
        public async Task ExecutionReusesLeaseAndBuildsCanonicalSuccess()
        {
            Activate(SimultriaEnvironmentIds.Local);
            ApiConnectionSettings settings = CreateConnectionSettings(
                SimultriaEnvironmentIds.Local);
            Assert.That(
                SimultriaApiConnectionSettingsAdapter.TryCreateComposition(
                    settings,
                    out ApiComposition composition,
                    out string compositionError),
                Is.True,
                compositionError);
            Assert.That(
                composition.TryResolveClient(
                    SimultriaEnvironmentIds.Local,
                    SimultriaClientIds.Primary,
                    out ApiResolvedClient primaryClient,
                    out string resolveError),
                Is.True,
                resolveError);
            IApiClient apiClient = CreateApiClient();
            Assert.That(
                SimultriaViewerRuntimeConnectionContext.TryActivate(
                    SimultriaEnvironmentIds.Local,
                    primaryClient,
                    composition,
                    apiClient,
                    out _,
                    out IDisposable registration,
                    out string activationError),
                Is.True,
                activationError);
            var applicationPayload = new JObject
            {
                ["application"] = "ready"
            };
            SimultriaViewerModelInitializationExecutionContext observed = null;
            try
            {
                SimultriaViewerModelInitializationExecutionResult result =
                    await Coordinator().ExecuteAsync(
                        Payload(SimultriaEnvironmentIds.Local.Value),
                        0,
                        settings,
                        null,
                        authProvider,
                        false,
                        (context, cancellationToken) =>
                        {
                            observed = context;
                            return Task.FromResult(
                                SimultriaViewerApplicationInitializationResult
                                    .Success(applicationPayload));
                        });

                Assert.That(result.Succeeded, Is.True, result.Message);
                Assert.That(observed, Is.Not.Null);
                Assert.That(observed.Composition, Is.SameAs(composition));
                Assert.That(observed.ApiClient, Is.SameAs(apiClient));
                Assert.That(observed.PrimaryClient, Is.SameAs(primaryClient));
                Assert.That(
                    observed.EnvironmentId,
                    Is.EqualTo(SimultriaEnvironmentIds.Local));
                Assert.That(result.PrimaryClient, Is.SameAs(primaryClient));
                var response = (JObject)result.Payload;
                Assert.That(
                    response.Value<string>("application"),
                    Is.EqualTo("ready"));
                Assert.That(
                    response.Value<string>("environment_id"),
                    Is.EqualTo(SimultriaEnvironmentIds.Local.Value));
                Assert.That(response.Value<int>("project_id"), Is.EqualTo(1));
                Assert.That(response.Value<int>("model_id"), Is.EqualTo(2));
                Assert.That(
                    response.Value<int>("model_version_id"),
                    Is.EqualTo(17));
                Assert.That(
                    applicationPayload["environment_id"],
                    Is.Null);
            }
            finally
            {
                registration?.Dispose();
            }
        }

        [Test]
        public async Task ExecutionSkipsApplicationWhenLeaseClearsDuringResolve()
        {
            Activate(SimultriaEnvironmentIds.Development);
            ApiConnectionSettings settings = CreateConnectionSettings(
                SimultriaEnvironmentIds.Development);
            Assert.That(
                SimultriaApiConnectionSettingsAdapter.TryCreateComposition(
                    settings,
                    out ApiComposition composition,
                    out string compositionError),
                Is.True,
                compositionError);
            Assert.That(
                composition.TryResolveClient(
                    SimultriaEnvironmentIds.Development,
                    SimultriaClientIds.Primary,
                    out ApiResolvedClient primary,
                    out string resolveError),
                Is.True,
                resolveError);
            IApiClient apiClient = CreateApiClient();
            var resolverStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var resolverCompletion = new TaskCompletionSource<
                SimultriaViewerModelResolveResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var resolver = new SimultriaViewerModelInitializationResolver(
                (projectId, modelId, versionId, cancellationToken) =>
                {
                    resolverStarted.TrySetResult(true);
                    return resolverCompletion.Task;
                });
            var coordinator = new SimultriaViewerModelInitializationCoordinator(
                (config, auth) => apiClient,
                (client, composed, environment) => resolver);
            Assert.That(
                SimultriaViewerRuntimeConnectionContext.TryActivate(
                    SimultriaEnvironmentIds.Development,
                    primary,
                    composition,
                    apiClient,
                    out _,
                    out IDisposable registration,
                    out string activationError),
                Is.True,
                activationError);
            bool applicationInvoked = false;
            try
            {
                Task<SimultriaViewerModelInitializationExecutionResult>
                    pending = coordinator.ExecuteAsync(
                        Payload(SimultriaEnvironmentIds.Development.Value),
                        0,
                        settings,
                        null,
                        authProvider,
                        false,
                        (context, cancellationToken) =>
                        {
                            applicationInvoked = true;
                            return Task.FromResult(
                                SimultriaViewerApplicationInitializationResult
                                    .Success());
                        });

                await resolverStarted.Task;
                registration.Dispose();
                registration = null;
                resolverCompletion.TrySetResult(
                    CreateSuccessfulResolveResult());
                SimultriaViewerModelInitializationExecutionResult result =
                    await pending;

                Assert.That(result.Succeeded, Is.False);
                Assert.That(
                    result.ErrorCode,
                    Is.EqualTo("runtime_connection_unavailable"));
                Assert.That(applicationInvoked, Is.False);
            }
            finally
            {
                registration?.Dispose();
                resolverCompletion.TrySetCanceled();
            }
        }

        [Test]
        public async Task ExecutionRejectsSuccessWhenLeaseClearsDuringApplication()
        {
            Activate(SimultriaEnvironmentIds.Development);
            ApiConnectionSettings settings = CreateConnectionSettings(
                SimultriaEnvironmentIds.Development);
            Assert.That(
                SimultriaApiConnectionSettingsAdapter.TryCreateComposition(
                    settings,
                    out ApiComposition composition,
                    out string compositionError),
                Is.True,
                compositionError);
            Assert.That(
                composition.TryResolveClient(
                    SimultriaEnvironmentIds.Development,
                    SimultriaClientIds.Primary,
                    out ApiResolvedClient primary,
                    out string resolveError),
                Is.True,
                resolveError);
            IApiClient apiClient = CreateApiClient();
            Assert.That(
                SimultriaViewerRuntimeConnectionContext.TryActivate(
                    SimultriaEnvironmentIds.Development,
                    primary,
                    composition,
                    apiClient,
                    out _,
                    out IDisposable registration,
                    out string activationError),
                Is.True,
                activationError);
            var applicationStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var applicationCompletion = new TaskCompletionSource<
                SimultriaViewerApplicationInitializationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                Task<SimultriaViewerModelInitializationExecutionResult>
                    pending = Coordinator().ExecuteAsync(
                        Payload(SimultriaEnvironmentIds.Development.Value),
                        0,
                        settings,
                        null,
                        authProvider,
                        false,
                        (context, cancellationToken) =>
                        {
                            applicationStarted.TrySetResult(true);
                            return applicationCompletion.Task;
                        });

                await applicationStarted.Task;
                registration.Dispose();
                registration = null;
                applicationCompletion.TrySetResult(
                    SimultriaViewerApplicationInitializationResult.Success(
                        new JObject { ["stale"] = true }));
                SimultriaViewerModelInitializationExecutionResult result =
                    await pending;

                Assert.That(result.Succeeded, Is.False);
                Assert.That(
                    result.ErrorCode,
                    Is.EqualTo("runtime_connection_unavailable"));
                Assert.That(result.Payload, Is.Null);
            }
            finally
            {
                registration?.Dispose();
                applicationCompletion.TrySetCanceled();
            }
        }

        [Test]
        public async Task ExecutionPreservesApplicationFailureWireValues()
        {
            var failurePayload = new JObject { ["detail"] = "safe" };
            SimultriaViewerModelInitializationExecutionResult result =
                await Coordinator().ExecuteAsync(
                    Payload(SimultriaEnvironmentIds.Development.Value),
                    0,
                    CreateConnectionSettings(
                        SimultriaEnvironmentIds.Development),
                    null,
                    authProvider,
                    true,
                    (context, cancellationToken) => Task.FromResult(
                        SimultriaViewerApplicationInitializationResult.Failure(
                            "viewer_failed",
                            "Viewer failure.",
                            failurePayload)));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("viewer_failed"));
            Assert.That(result.Message, Is.EqualTo("Viewer failure."));
            Assert.That(
                result.Payload["detail"].Value<string>(),
                Is.EqualTo("safe"));
        }

        [Test]
        public async Task ExecutionSanitizesApplicationExceptions()
        {
            const string unsafeDetail =
                "https://redacted.invalid/?token=must-not-escape";
            SimultriaViewerModelInitializationExecutionResult result =
                await Coordinator().ExecuteAsync(
                    Payload(SimultriaEnvironmentIds.Development.Value),
                    0,
                    CreateConnectionSettings(
                        SimultriaEnvironmentIds.Development),
                    null,
                    authProvider,
                    true,
                    (context, cancellationToken) =>
                        Task.FromException<
                            SimultriaViewerApplicationInitializationResult>(
                            new InvalidOperationException(unsafeDetail)));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(
                result.ErrorCode,
                Is.EqualTo("application_initialization_failed"));
            Assert.That(result.Message, Does.Not.Contain(unsafeDetail));
            Assert.That(result.Message, Does.Not.Contain("token"));
            Assert.That(result.Message, Does.Not.Contain("http"));
        }

        private SimultriaViewerModelInitializationCoordinator Coordinator() =>
            new SimultriaViewerModelInitializationCoordinator(
                (config, auth) => CreateApiClient(),
                (client, composition, environment) =>
                    CreateSuccessfulResolver());

        private IApiClient CreateApiClient()
        {
            ApiClientConfig config = Own(
                ApiClientConfig.CreateRuntimeDefault());
            return ApiClientFactory.Create(config, authProvider);
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

        private ApiConnectionSettings CreateConnectionSettings(
            params ApiEnvironmentId[] configuredIds)
        {
            ApiServiceDefinition definition =
                SimultriaApiDefinitionDefaults.LoadServiceDefinition();
            Assert.That(definition, Is.Not.Null);
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
                environment.Clients.Add(new ApiNamedClientDefinition
                {
                    ClientId = SimultriaClientIds.Primary.Value,
                    BaseUrl = configured.Contains(descriptor.EnvironmentId)
                        ? "https://viewer-coordinator.example.invalid"
                        : string.Empty
                });
                environments.Add(environment);
            }

            return Own(ApiConnectionSettings.CreateTransient(
                environments,
                definition));
        }

        private static SimultriaViewerInitializationPayload Payload(
            string environmentId) =>
            new SimultriaViewerInitializationPayload
            {
                Revision = 1,
                EnvironmentId = environmentId,
                ProjectId = 1,
                ModelId = 2,
                ModelVersionId = 17
            };

        private static SimultriaViewerModelInitializationResolver
            CreateSuccessfulResolver() =>
            new SimultriaViewerModelInitializationResolver(
                (projectId, modelId, versionId, cancellationToken) =>
                    Task.FromResult(CreateSuccessfulResolveResult(
                        projectId,
                        modelId,
                        versionId)));

        private static SimultriaViewerModelResolveResult
            CreateSuccessfulResolveResult(
                int projectId = 1,
                int modelId = 2,
                int? versionId = 17) =>
            SimultriaViewerModelResolver.ResolveFromProjects(
                projectId,
                modelId,
                versionId,
                new[]
                {
                    new SimultriaProjectDto
                    {
                        Id = 1,
                        Models = new List<SimultriaModelDto>
                        {
                            new SimultriaModelDto
                            {
                                Id = 2,
                                Versions = new List<
                                    SimultriaModelVersionDto>
                                {
                                    new SimultriaModelVersionDto
                                    {
                                        Id = 17,
                                        DownloadUrl =
                                            "https://models.example.invalid/resolved"
                                    }
                                }
                            }
                        }
                    }
                });

        private static void Activate(ApiEnvironmentId environmentId)
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

        private static void AssertFailure(
            SimultriaViewerModelInitializationPlan plan,
            string errorCode)
        {
            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.Succeeded, Is.False);
            Assert.That(plan.ErrorCode, Is.EqualTo(errorCode));
            Assert.That(plan.Message, Is.Not.Empty);
            Assert.That(plan.Composition, Is.Null);
            Assert.That(plan.ApiClient, Is.Null);
            Assert.That(plan.Resolver, Is.Null);
        }

        private T Own<T>(T instance) where T : UnityEngine.Object
        {
            ownedObjects.Add(instance);
            return instance;
        }

        private sealed class PresentAuthProvider : IApiAuthProvider
        {
            public Task<string> GetAccessTokenAsync(
                CancellationToken cancellationToken) =>
                Task.FromResult(string.Empty);
        }
    }
}
