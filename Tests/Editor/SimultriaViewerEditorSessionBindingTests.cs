using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.API.Configuration;
using Deucarian.API.Models;
using Deucarian.Authentication;
using Deucarian.Authentication.Editor;
using Deucarian.Simultria.API.Configuration;
using Deucarian.SimultriaViewerIntegration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class SimultriaViewerEditorSessionBindingTests
    {
        private readonly List<string> capturedBindings = new List<string>();
        private IDisposable hostSuspension;
        private string assetPath;

        [SetUp]
        public void SetUp()
        {
            hostSuspension =
                SimultriaViewerEditorAuthenticationHost.SuspendForTests();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < capturedBindings.Count; i++)
            {
                AuthenticationEditorSessionHandoff.Clear(capturedBindings[i]);
            }

            capturedBindings.Clear();
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
                assetPath = null;
            }

            SimultriaViewerEditorRuntimeSessionBridge.Install();
            hostSuspension?.Dispose();
            hostSuspension = null;
        }

        [Test]
        public void SameAssetHostChangeCreatesANewCredentialFreeBinding()
        {
            PersistentSettings fixture = CreatePersistentSettings();
            string original = CreateBinding(
                fixture,
                SimultriaEnvironmentIds.Development);

            ChangeHost(
                fixture,
                SimultriaEnvironmentIds.Development,
                "https://alternate-viewer.invalid");
            string changed = CreateBinding(
                fixture,
                SimultriaEnvironmentIds.Development);

            Assert.That(changed, Is.Not.EqualTo(original));
            Assert.That(original, Does.Contain("|sha256:"));
            Assert.That(original, Does.Not.Contain("viewer.invalid"));
            Assert.That(changed, Does.Not.Contain("alternate-viewer.invalid"));
        }

        [Test]
        public void SameAssetCatalogRouteChangeCreatesANewBinding()
        {
            PersistentSettings fixture = CreatePersistentSettings();
            string original = CreateBinding(
                fixture,
                SimultriaEnvironmentIds.Development);
            string originalRoute = fixture.Catalog.Endpoints[0].RouteTemplate;

            ChangeFirstRoute(fixture);
            string changed = CreateBinding(
                fixture,
                SimultriaEnvironmentIds.Development);

            Assert.That(changed, Is.Not.EqualTo(original));
            Assert.That(original, Does.Not.Contain(originalRoute));
            Assert.That(
                changed,
                Does.Not.Contain(fixture.Catalog.Endpoints[0].RouteTemplate));
        }

        [Test]
        public async Task SecondaryClientHostChangeInvalidatesRegisteredTarget()
        {
            PersistentSettings fixture = CreatePersistentSettings();
            AuthenticationSession session =
                AuthenticationSession.CreateTransient();
            IDisposable registration = null;
            try
            {
                Assert.That(
                    SimultriaViewerConnectionAuthentication.TryRegister(
                        fixture.Settings,
                        SimultriaEnvironmentIds.Development,
                        session,
                        out registration,
                        out _,
                        out string registrationError),
                    Is.True,
                    registrationError);
                Assert.That(
                    AuthenticationTargetRegistry.TryGet(
                        SimultriaViewerConnectionAuthentication.DefaultTargetId,
                        out AuthenticationTarget target),
                    Is.True);
                Assert.That(
                    SimultriaViewerConnectionAuthentication.TryValidateTarget(
                        target,
                        fixture.Settings,
                        SimultriaEnvironmentIds.Development,
                        out string initialError),
                    Is.True,
                    initialError);

                ChangeSecondaryHost(
                    fixture,
                    SimultriaEnvironmentIds.Development,
                    "https://alternate-model-content.invalid");

                Assert.That(
                    SimultriaViewerConnectionAuthentication.TryValidateTarget(
                        target,
                        fixture.Settings,
                        SimultriaEnvironmentIds.Development,
                        out string changedError),
                    Is.False);
                Assert.That(changedError, Does.Contain("does not match"));
            }
            finally
            {
                registration?.Dispose();
                await session.ClearAsync(CancellationToken.None);
            }
        }

        [Test]
        public async Task LeaseDropsBearerWhenSameAssetBackendChanges()
        {
            PersistentSettings fixture = CreatePersistentSettings();
            AuthenticationSession source =
                AuthenticationSession.CreateTransient();
            var createdSessions = new List<AuthenticationSession>();
            SimultriaViewerEditorAuthenticationLease lease = null;
            await source.ReplaceAccessTokenAsync(Guid.NewGuid().ToString("N"));
            string originalBinding = Capture(
                fixture,
                SimultriaEnvironmentIds.Development,
                source);
            string targetId = "simultria-binding-lease-" +
                              Guid.NewGuid().ToString("N");
            try
            {
                lease = new SimultriaViewerEditorAuthenticationLease(
                    () => new SimultriaViewerEditorAuthenticationConfiguration(
                        fixture.Settings,
                        SimultriaEnvironmentIds.Development),
                    registerTarget: (_, session) =>
                    {
                        createdSessions.Add(session);
                        return AuthenticationTargetRegistry.Register(
                            targetId,
                            "Binding lease test",
                            session);
                    });

                lease.Reconcile(suspendForPlayMode: false);
                Assert.That(createdSessions, Has.Count.EqualTo(1));
                Assert.That(
                    createdSessions[0].Status.HasAccessToken,
                    Is.True);

                ChangeHost(
                    fixture,
                    SimultriaEnvironmentIds.Development,
                    "https://changed-during-lease.invalid");
                lease.Reconcile(suspendForPlayMode: false);

                Assert.That(createdSessions, Has.Count.EqualTo(2));
                Assert.That(
                    createdSessions[1].Status.HasAccessToken,
                    Is.False);
                bool restored =
                    AuthenticationEditorSessionHandoff.TryCreateSession(
                        originalBinding,
                        out AuthenticationSession stale);
                try
                {
                    Assert.That(restored, Is.False);
                }
                finally
                {
                    await stale.ClearAsync(CancellationToken.None);
                }
            }
            finally
            {
                lease?.Dispose();
                await source.ClearAsync(CancellationToken.None);
                for (int i = 0; i < createdSessions.Count; i++)
                {
                    await createdSessions[i].ClearAsync(
                        CancellationToken.None);
                }
            }
        }

        [Test]
        public async Task ProductionBridgeRestoresOnlyMatchingBackendAndEnvironment()
        {
            PersistentSettings fixture = CreatePersistentSettings();
            AuthenticationSession source =
                AuthenticationSession.CreateTransient();
            await source.ReplaceAccessTokenAsync(Guid.NewGuid().ToString("N"));
            Capture(
                fixture,
                SimultriaEnvironmentIds.Development,
                source);

            SimultriaViewerEditorRuntimeSessionBridge.Install();
            SimultriaViewerEditorRuntimeSessionBridge.Install();

            ViewerRuntimeConnection matching = null;
            ViewerRuntimeConnection otherEnvironment = null;
            try
            {
                matching = CreateRuntimeConnection(
                    fixture,
                    SimultriaEnvironmentIds.Development);
                Assert.That(matching.Session.Status.HasAccessToken, Is.True);
                matching.Dispose();
                matching = null;

                otherEnvironment = CreateRuntimeConnection(
                    fixture,
                    SimultriaEnvironmentIds.Testing);
                Assert.That(
                    otherEnvironment.Session.Status.HasAccessToken,
                    Is.False);
            }
            finally
            {
                matching?.Dispose();
                otherEnvironment?.Dispose();
                await source.ClearAsync(CancellationToken.None);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ProductionBridgeRejectsSameAssetBackendMutation(
            bool changeHost)
        {
            PersistentSettings fixture = CreatePersistentSettings();
            AuthenticationSession source =
                AuthenticationSession.CreateTransient();
            await source.ReplaceAccessTokenAsync(Guid.NewGuid().ToString("N"));
            Capture(
                fixture,
                SimultriaEnvironmentIds.Development,
                source);

            if (changeHost)
            {
                ChangeHost(
                    fixture,
                    SimultriaEnvironmentIds.Development,
                    "https://bridge-backend-change.invalid");
            }
            else
            {
                ChangeFirstRoute(fixture);
            }

            SimultriaViewerEditorRuntimeSessionBridge.Install();
            ViewerRuntimeConnection connection = null;
            try
            {
                connection = CreateRuntimeConnection(
                    fixture,
                    SimultriaEnvironmentIds.Development);
                Assert.That(connection.Session.Status.HasAccessToken, Is.False);
            }
            finally
            {
                connection?.Dispose();
                await source.ClearAsync(CancellationToken.None);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task FactoryDropsSessionWhenBackendChangesBeforeTryCreate(
            bool changeHost)
        {
            PersistentSettings fixture = CreatePersistentSettings();
            AuthenticationSession source =
                AuthenticationSession.CreateTransient();
            await source.ReplaceAccessTokenAsync(Guid.NewGuid().ToString("N"));
            Capture(
                fixture,
                SimultriaEnvironmentIds.Development,
                source);

            SimultriaViewerEditorRuntimeSessionBridge.Install();
            SimultriaViewerRuntimeConnectionProvider provider =
                SimultriaViewerRuntimeConnectionProviderFactory.Create(
                    fixture.Settings,
                    SimultriaEnvironmentIds.Development);
            if (changeHost)
            {
                ChangeHost(
                    fixture,
                    SimultriaEnvironmentIds.Development,
                    "https://factory-backend-change.invalid");
            }
            else
            {
                ChangeFirstRoute(fixture);
            }

            ViewerRuntimeConnection connection = null;
            try
            {
                Assert.That(
                    provider.TryCreate(out connection, out string error),
                    Is.True,
                    error);
                Assert.That(connection.Session.Status.HasAccessToken, Is.False);
            }
            finally
            {
                connection?.Dispose();
                await source.ClearAsync(CancellationToken.None);
            }
        }

        [Test]
        public async Task FactoryRejectsSessionWhenSourceMutatesBeforeReturn()
        {
            PersistentSettings fixture = CreatePersistentSettings();
            AuthenticationSession restored =
                AuthenticationSession.CreateTransient();
            await restored.ReplaceAccessTokenAsync(Guid.NewGuid().ToString("N"));

            SimultriaViewerRuntimeConnectionProvider provider;
            using (SimultriaViewerRuntimeConnectionProviderFactory
                .OverrideInitialSessionFactoryForTests(
                    (candidateSettings, candidateEnvironment) =>
                    {
                        SimultriaViewerInitialSession captured =
                            SimultriaViewerInitialSession.Capture(
                                candidateSettings,
                                candidateEnvironment,
                                restored);
                        ChangeFirstRoute(fixture);
                        return captured;
                    }))
            {
                provider = SimultriaViewerRuntimeConnectionProviderFactory.Create(
                    fixture.Settings,
                    SimultriaEnvironmentIds.Development);
            }

            ViewerRuntimeConnection connection = null;
            try
            {
                Assert.That(
                    provider.TryCreate(out connection, out string error),
                    Is.True,
                    error);
                Assert.That(connection.Session.Status.HasAccessToken, Is.False);
            }
            finally
            {
                connection?.Dispose();
                await restored.ClearAsync(CancellationToken.None);
            }
        }

        [Test]
        public async Task RegistrationCallbackMutationFailsConnectionClosed()
        {
            PersistentSettings fixture = CreatePersistentSettings();
            AuthenticationSession source =
                AuthenticationSession.CreateTransient();
            await source.ReplaceAccessTokenAsync(Guid.NewGuid().ToString("N"));
            Capture(
                fixture,
                SimultriaEnvironmentIds.Development,
                source);

            SimultriaViewerEditorRuntimeSessionBridge.Install();
            SimultriaViewerRuntimeConnectionProvider provider =
                SimultriaViewerRuntimeConnectionProviderFactory.Create(
                    fixture.Settings,
                    SimultriaEnvironmentIds.Development);
            bool mutated = false;
            Action mutateDuringRegistration = () =>
            {
                if (mutated ||
                    !AuthenticationTargetRegistry.TryGet(
                        SimultriaViewerConnectionAuthentication.DefaultTargetId,
                        out _))
                {
                    return;
                }

                mutated = true;
                ChangeFirstRoute(fixture);
            };

            ViewerRuntimeConnection connection = null;
            AuthenticationTargetRegistry.TargetsChanged +=
                mutateDuringRegistration;
            try
            {
                Assert.That(
                    provider.TryCreate(out connection, out string error),
                    Is.False);
                Assert.That(connection, Is.Null);
                Assert.That(mutated, Is.True);
                Assert.That(error, Does.Contain("configuration changed"));
                Assert.That(
                    AuthenticationTargetRegistry.TryGet(
                        SimultriaViewerConnectionAuthentication.DefaultTargetId,
                        out _),
                    Is.False);
            }
            finally
            {
                AuthenticationTargetRegistry.TargetsChanged -=
                    mutateDuringRegistration;
                connection?.Dispose();
                await source.ClearAsync(CancellationToken.None);
            }
        }

        [Test]
        public async Task RegisteredTargetStopsMatchingBeforeLeasePollWhenRouteChanges()
        {
            PersistentSettings fixture = CreatePersistentSettings();
            AuthenticationSession session =
                AuthenticationSession.CreateTransient();
            IDisposable registration = null;
            try
            {
                Assert.That(
                    SimultriaViewerConnectionAuthentication.TryRegister(
                        fixture.Settings,
                        SimultriaEnvironmentIds.Development,
                        session,
                        out registration,
                        out _,
                        out string registrationError),
                    Is.True,
                    registrationError);
                Assert.That(
                    AuthenticationTargetRegistry.TryGet(
                        SimultriaViewerConnectionAuthentication.DefaultTargetId,
                        out AuthenticationTarget target),
                    Is.True);
                Assert.That(
                    SimultriaViewerConnectionCompositionFingerprint.TryCreate(
                        fixture.Settings,
                        SimultriaEnvironmentIds.Development,
                        out string registeredFingerprint),
                    Is.True);
                Assert.That(
                    target.PersistenceIdentity.ConfigurationFingerprint,
                    Is.EqualTo(registeredFingerprint));
                Assert.That(
                    SimultriaViewerConnectionAuthentication.TryValidateTarget(
                        target,
                        fixture.Settings,
                        SimultriaEnvironmentIds.Development,
                        out string initialError),
                    Is.True,
                    initialError);

                ChangeFirstRoute(fixture);

                Assert.That(
                    SimultriaViewerConnectionAuthentication.TryValidateTarget(
                        target,
                        fixture.Settings,
                        SimultriaEnvironmentIds.Development,
                        out string changedError),
                    Is.False);
                Assert.That(changedError, Does.Contain("does not match"));
            }
            finally
            {
                registration?.Dispose();
                await session.ClearAsync(CancellationToken.None);
            }
        }

        private PersistentSettings CreatePersistentSettings()
        {
            ApiServiceDefinition source =
                SimultriaApiDefinitionDefaults.LoadServiceDefinition();
            Assert.That(source, Is.Not.Null);
            Assert.That(
                source.TryGetEnvironmentDescriptors(
                    out IReadOnlyList<ApiEnvironmentDescriptor> descriptors,
                    out string error),
                Is.True,
                error);

            ApiEndpointCatalog catalog = UnityEngine.Object.Instantiate(
                source.EndpointCatalog);
            ApiServiceDefinition definition = UnityEngine.Object.Instantiate(
                source);
            definition.EndpointCatalog = catalog;
            var environments = new List<ApiEnvironmentProfile>();
            for (int i = 0; i < descriptors.Count; i++)
            {
                ApiEnvironmentDescriptor descriptor = descriptors[i];
                ApiEnvironmentProfile environment =
                    ScriptableObject.CreateInstance<ApiEnvironmentProfile>();
                environment.EnvironmentId = descriptor.EnvironmentId.Value;
                environment.DisplayName = descriptor.DisplayName;
                environment.Clients.Add(new ApiNamedClientDefinition
                {
                    ClientId = SimultriaClientIds.Primary.Value,
                    BaseUrl = InitialHost(descriptor.EnvironmentId)
                });
                environment.Clients.Add(new ApiNamedClientDefinition
                {
                    ClientId = SimultriaViewerAuthenticatedOriginResolver
                        .ModelContentClientIdValue,
                    BaseUrl = InitialModelContentHost(
                        descriptor.EnvironmentId)
                });
                environments.Add(environment);
            }

            ApiConnectionSettings settings =
                ApiConnectionSettings.CreateTransient(
                    environments,
                    definition);
            assetPath = "Assets/SimultriaViewerSessionBinding-" +
                        Guid.NewGuid().ToString("N") + ".asset";
            AssetDatabase.CreateAsset(settings, assetPath);
            AssetDatabase.AddObjectToAsset(definition, settings);
            AssetDatabase.AddObjectToAsset(catalog, settings);
            for (int i = 0; i < environments.Count; i++)
            {
                AssetDatabase.AddObjectToAsset(environments[i], settings);
            }

            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(definition);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            settings = AssetDatabase.LoadAssetAtPath<ApiConnectionSettings>(
                assetPath);
            Assert.That(settings, Is.Not.Null);
            return new PersistentSettings(settings);
        }

        private string CreateBinding(
            PersistentSettings fixture,
            ApiEnvironmentId environmentId)
        {
            string binding = SimultriaViewerEditorAuthenticationBinding.Create(
                fixture.Settings,
                environmentId);
            Assert.That(binding, Is.Not.Empty);
            return binding;
        }

        private string Capture(
            PersistentSettings fixture,
            ApiEnvironmentId environmentId,
            AuthenticationSession source)
        {
            string binding = CreateBinding(fixture, environmentId);
            capturedBindings.Add(binding);
            AuthenticationEditorSessionHandoff.Capture(binding, source);
            return binding;
        }

        private static ViewerRuntimeConnection CreateRuntimeConnection(
            PersistentSettings fixture,
            ApiEnvironmentId environmentId)
        {
            SimultriaViewerRuntimeConnectionProvider provider =
                SimultriaViewerRuntimeConnectionProviderFactory.Create(
                    fixture.Settings,
                    environmentId);
            Assert.That(
                provider.TryCreate(
                    out ViewerRuntimeConnection connection,
                    out string error),
                Is.True,
                error);
            return connection;
        }

        private static void ChangeHost(
            PersistentSettings fixture,
            ApiEnvironmentId environmentId,
            string host)
        {
            ApiEnvironmentProfile environment =
                fixture.GetEnvironment(environmentId);
            Assert.That(environment.Clients, Has.Count.GreaterThan(0));
            environment.Clients[0].BaseUrl = host;
            EditorUtility.SetDirty(environment);
            AssetDatabase.SaveAssets();
        }

        private static void ChangeFirstRoute(PersistentSettings fixture)
        {
            Assert.That(fixture.Catalog.Endpoints, Has.Count.GreaterThan(0));
            ApiEndpointCatalogEntry endpoint = fixture.Catalog.Endpoints[0];
            endpoint.RouteTemplate = endpoint.RouteTemplate.TrimEnd('/') +
                                     "/binding-revision";
            EditorUtility.SetDirty(fixture.Catalog);
            AssetDatabase.SaveAssets();
        }

        private static void ChangeSecondaryHost(
            PersistentSettings fixture,
            ApiEnvironmentId environmentId,
            string host)
        {
            ApiEnvironmentProfile environment =
                fixture.GetEnvironment(environmentId);
            for (int i = 0; i < environment.Clients.Count; i++)
            {
                ApiNamedClientDefinition client = environment.Clients[i];
                if (client != null && string.Equals(
                        client.ClientId,
                        SimultriaViewerAuthenticatedOriginResolver
                            .ModelContentClientIdValue,
                        StringComparison.Ordinal))
                {
                    client.BaseUrl = host;
                    EditorUtility.SetDirty(environment);
                    AssetDatabase.SaveAssets();
                    return;
                }
            }

            Assert.Fail("The model-content client is missing.");
        }

        private static string InitialHost(ApiEnvironmentId environmentId)
        {
            if (environmentId == SimultriaEnvironmentIds.Development)
            {
                return "https://development-viewer.invalid";
            }

            return environmentId == SimultriaEnvironmentIds.Testing
                ? "https://testing-viewer.invalid"
                : string.Empty;
        }

        private static string InitialModelContentHost(
            ApiEnvironmentId environmentId)
        {
            if (environmentId == SimultriaEnvironmentIds.Development)
            {
                return "https://development-content.invalid";
            }

            return environmentId == SimultriaEnvironmentIds.Testing
                ? "https://testing-content.invalid"
                : string.Empty;
        }

        private sealed class PersistentSettings
        {
            internal PersistentSettings(ApiConnectionSettings settings)
            {
                Settings = settings;
                Catalog = settings.ServiceDefinition.EndpointCatalog;
            }

            internal ApiConnectionSettings Settings { get; }
            internal ApiEndpointCatalog Catalog { get; }

            internal ApiEnvironmentProfile GetEnvironment(
                ApiEnvironmentId environmentId)
            {
                IReadOnlyList<ApiEnvironmentProfile> environments =
                    Settings.Environments;
                for (int i = 0; i < environments.Count; i++)
                {
                    ApiEnvironmentProfile environment = environments[i];
                    if (environment != null &&
                        environment.TryGetId(out ApiEnvironmentId id) &&
                        id == environmentId)
                    {
                        return environment;
                    }
                }

                Assert.Fail("The requested environment is missing.");
                return null;
            }
        }
    }
}
