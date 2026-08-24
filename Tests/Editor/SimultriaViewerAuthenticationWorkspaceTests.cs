using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Deucarian.API.Authentication;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Authentication;
using Deucarian.Simultria.API.Configuration;
using Deucarian.SimultriaViewerConnection.Editor;
using Deucarian.ViewerAuthentication;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection.Tests
{
    public sealed class SimultriaViewerAuthenticationWorkspaceTests
    {
        private IDisposable hostSuspension;
        private SimultriaApiProfile configuredLegacyProfile;
        private ApiEnvironmentProfile configuredDevelopmentEnvironment;

        [SetUp]
        public void SetUp()
        {
            hostSuspension =
                SimultriaViewerEditorAuthenticationHost.SuspendForTests();
            configuredLegacyProfile = CreateConfiguredLegacyProfile();
        }

        [TearDown]
        public void TearDown()
        {
            hostSuspension?.Dispose();
            hostSuspension = null;
            UnityEngine.Object.DestroyImmediate(configuredLegacyProfile);
            UnityEngine.Object.DestroyImmediate(
                configuredDevelopmentEnvironment);
            configuredLegacyProfile = null;
            configuredDevelopmentEnvironment = null;
        }

        [Test]
        public void ExplicitEnvironmentRegistrationUsesStableSingleViewerIdentity()
        {
            SimultriaApiProfile apiProfile = configuredLegacyProfile;
            ViewerAuthenticationSession session =
                ViewerAuthenticationSession.CreateTransient();
            IDisposable registration = null;
            try
            {
                bool registered =
                    SimultriaViewerConnectionAuthentication.TryRegister(
                        apiProfile,
                        SimultriaEnvironmentIds.Development,
                        session,
                        out registration,
                        out ApiEnvironmentStatus environment,
                        out string error);

                Assert.That(registered, Is.True, error);
                Assert.That(environment.IsResolved, Is.True);
                Assert.That(
                    ViewerAuthenticationTargetRegistry.TryGet(
                        SimultriaViewerConnectionAuthentication
                            .DefaultTargetId,
                        out ViewerAuthenticationTarget target),
                    Is.True);
                Assert.That(target.Session, Is.SameAs(session));
                Assert.That(target.AcquisitionProvider,
                    Is.TypeOf<SimultriaViewerAuthenticationProvider>());
                Assert.That(target.ValidationProvider,
                    Is.SameAs(target.AcquisitionProvider));
            }
            finally
            {
                registration?.Dispose();
            }
        }

        [Test]
        public void GenericConnectionProfileUsesTheSameStableRegistration()
        {
            ApiConnectionProfile connectionProfile =
                CreateGenericConnectionProfile();
            ViewerAuthenticationSession session =
                ViewerAuthenticationSession.CreateTransient();
            IDisposable registration = null;
            try
            {
                bool registered =
                    SimultriaViewerConnectionAuthentication.TryRegister(
                        connectionProfile,
                        SimultriaEnvironmentIds.Development,
                        session,
                        out registration,
                        out ApiEnvironmentStatus environment,
                        out string error);

                Assert.That(registered, Is.True, error);
                Assert.That(environment.IsResolved, Is.True);
                Assert.That(
                    ViewerAuthenticationTargetRegistry.TryGet(
                        SimultriaViewerConnectionAuthentication
                            .DefaultTargetId,
                        out ViewerAuthenticationTarget target),
                    Is.True);
                Assert.That(target.Session, Is.SameAs(session));
                Assert.That(
                    SimultriaViewerConnectionAuthentication.TryValidateTarget(
                        target,
                        connectionProfile,
                        SimultriaEnvironmentIds.Development,
                        out string validationError),
                    Is.True,
                    validationError);
            }
            finally
            {
                registration?.Dispose();
                UnityEngine.Object.DestroyImmediate(connectionProfile);
            }
        }

        [Test]
        public void DevelopmentProfilePrefersAssignedGenericConnection()
        {
            ApiConnectionProfile connectionProfile =
                CreateGenericConnectionProfile();
            var developmentProfile =
                ScriptableObject.CreateInstance<
                    SimultriaViewerDevelopmentProfile>();
            developmentProfile.ConnectionProfileReference = connectionProfile;
            developmentProfile.ApiProfileReference =
                configuredLegacyProfile;
            developmentProfile.EnvironmentId =
                SimultriaEnvironmentIds.Development;
            ViewerAuthenticationSession session =
                ViewerAuthenticationSession.CreateTransient();
            IDisposable registration = null;
            try
            {
                Assert.That(
                    developmentProfile.EffectiveProfileReference,
                    Is.SameAs(connectionProfile));
                Assert.That(
                    developmentProfile.EffectiveApiProfile,
                    Is.Null,
                    "Legacy callers must not silently ignore the generic profile.");
                Assert.That(
                    SimultriaViewerConnectionAuthentication.TryRegister(
                        developmentProfile,
                        session,
                        out registration,
                        out _,
                        out string registrationError),
                    Is.True,
                    registrationError);
                Assert.That(
                    ViewerAuthenticationTargetRegistry.TryGet(
                        SimultriaViewerConnectionAuthentication
                            .DefaultTargetId,
                        out ViewerAuthenticationTarget target),
                    Is.True);
                Assert.That(
                    SimultriaViewerConnectionAuthentication.TryValidateTarget(
                        target,
                        developmentProfile,
                        SimultriaEnvironmentIds.Development,
                        out string validationError),
                    Is.True,
                    validationError);
            }
            finally
            {
                registration?.Dispose();
                UnityEngine.Object.DestroyImmediate(developmentProfile);
                UnityEngine.Object.DestroyImmediate(connectionProfile);
            }
        }

        [Test]
        public void RuntimeProviderCreatesOneStableSharedSessionAndApiClient()
        {
            var provider = new SimultriaViewerRuntimeConnectionProvider(
                configuredLegacyProfile,
                SimultriaEnvironmentIds.Development);
            ViewerRuntimeConnection connection = null;
            try
            {
                bool created = provider.TryCreate(
                    out connection,
                    out string error);

                Assert.That(created, Is.True, error);
                Assert.That(
                    connection.TargetId,
                    Is.EqualTo(
                        SimultriaViewerConnectionAuthentication
                            .DefaultTargetId));
                Assert.That(connection.Session, Is.Not.Null);
                Assert.That(connection.ApiClient, Is.Not.Null);
                Assert.That(
                    Uri.TryCreate(
                        connection.ApiBaseUrl,
                        UriKind.Absolute,
                        out Uri baseUri),
                    Is.True);
                Assert.That(
                    connection.AuthenticatedOrigins,
                    Does.Contain(baseUri.GetLeftPart(UriPartial.Authority)));
                Assert.That(
                    ViewerAuthenticationTargetRegistry.TryGet(
                        connection.TargetId,
                        out ViewerAuthenticationTarget target),
                    Is.True);
                Assert.That(target.Session, Is.SameAs(connection.Session));

                Assert.That(
                    provider.TryCreate(out _, out _),
                    Is.False,
                    "A provider lease must be authoritative and singular.");
            }
            finally
            {
                connection?.Dispose();
            }
        }

        [Test]
        public void RuntimeProviderAcceptsExplicitGenericConnectionProfile()
        {
            ApiConnectionProfile connectionProfile =
                CreateGenericConnectionProfile();
            var provider = new SimultriaViewerRuntimeConnectionProvider(
                connectionProfile,
                SimultriaEnvironmentIds.Development);
            ViewerRuntimeConnection connection = null;
            try
            {
                bool created = provider.TryCreate(
                    out connection,
                    out string error);

                Assert.That(created, Is.True, error);
                Assert.That(connection, Is.Not.Null);
                Assert.That(
                    connection.ApiBaseUrl,
                    Is.EqualTo("https://simultria-viewer.invalid"));
                Assert.That(
                    ViewerAuthenticationTargetRegistry.TryGet(
                        connection.TargetId,
                        out ViewerAuthenticationTarget target),
                    Is.True);
                Assert.That(target.Session, Is.SameAs(connection.Session));
            }
            finally
            {
                connection?.Dispose();
                UnityEngine.Object.DestroyImmediate(connectionProfile);
            }
        }

        [Test]
        public void BlankPackageDefaultLeavesRuntimeProviderRegistryEmpty()
        {
            Assert.That(
                SimultriaViewerRuntimeConnectionBootstrap
                    .TryRegisterDefaultProvider(),
                Is.False,
                "A package default without a host must not claim the " +
                "fail-closed runtime registry.");

            ViewerRuntimeConnectionResolution resolution =
                ViewerRuntimeConnectionProviderRegistry.Resolve();
            Assert.That(
                resolution.Status,
                Is.EqualTo(ViewerRuntimeConnectionResolutionStatus.None));
            Assert.That(resolution.Connection, Is.Null);
        }

        [Test]
        public void RuntimeProviderTypedNullProfilesFailCleanly()
        {
            ApiConnectionProfile connectionProfile = null;
            SimultriaApiProfile legacyProfile = null;
            var genericProvider =
                new SimultriaViewerRuntimeConnectionProvider(
                    connectionProfile,
                    SimultriaEnvironmentIds.Development);
            var legacyProvider =
                new SimultriaViewerRuntimeConnectionProvider(
                    legacyProfile,
                    SimultriaEnvironmentIds.Development);

            Assert.That(
                genericProvider.TryCreate(out _, out string genericError),
                Is.False);
            Assert.That(
                genericError,
                Does.Contain("connection profile"));
            Assert.That(
                legacyProvider.TryCreate(out _, out string legacyError),
                Is.False);
            Assert.That(
                legacyError,
                Does.Contain("connection profile"));
        }

        [Test]
        public void ArbitraryStableIdTargetCannotSupplySimultriaBearer()
        {
            IDisposable registration =
                ViewerAuthenticationTargetRegistry.Register(
                    SimultriaViewerConnectionAuthentication.DefaultTargetId,
                    "Untrusted Viewer",
                    ViewerAuthenticationSession.CreateTransient());
            try
            {
                Assert.That(
                    SimultriaViewerConnectionStatus
                        .TryResolveAuthenticationTarget(out _),
                    Is.False);
            }
            finally
            {
                registration.Dispose();
            }
        }

        [Test]
        public void RuntimeDevelopmentBindingRejectsDifferentSelectedEnvironment()
        {
            SimultriaApiProfile apiProfile = configuredLegacyProfile;
            ViewerAuthenticationSession session =
                ViewerAuthenticationSession.CreateTransient();
            var selectedProfile =
                ScriptableObject.CreateInstance<
                    SimultriaViewerDevelopmentProfile>();
            selectedProfile.EnvironmentId =
                SimultriaEnvironmentIds.Acceptance;
            IDisposable registration = null;
            try
            {
                Assert.That(
                    SimultriaViewerConnectionAuthentication.TryRegister(
                        apiProfile,
                        SimultriaEnvironmentIds.Development,
                        session,
                        out registration,
                        out _,
                        out string registrationError),
                    Is.True,
                    registrationError);

                Assert.That(
                    SimultriaViewerConnectionStatus
                        .TryResolveAuthenticationTarget(
                            selectedProfile,
                            out _,
                            out string error),
                    Is.False);
                Assert.That(error, Does.Contain("does not match"));
            }
            finally
            {
                registration?.Dispose();
                UnityEngine.Object.DestroyImmediate(selectedProfile);
            }
        }

        [Test]
        public void RegistrationRejectsStructurallyEqualButDifferentSelectedProfile()
        {
            SimultriaApiProfile registeredProfile =
                configuredLegacyProfile;
            SimultriaApiProfile selectedApiProfile =
                SimultriaApiProfile.CreateTransient(
                    registeredProfile.Environments,
                    registeredProfile.EndpointCatalog);
            var selectedProfile =
                ScriptableObject.CreateInstance<
                    SimultriaViewerDevelopmentProfile>();
            selectedProfile.ApiProfileReference = selectedApiProfile;
            selectedProfile.EnvironmentId =
                SimultriaEnvironmentIds.Development;
            ViewerAuthenticationSession session =
                ViewerAuthenticationSession.CreateTransient();
            IDisposable registration = null;
            try
            {
                Assert.That(
                    SimultriaViewerConnectionAuthentication.TryRegister(
                        registeredProfile,
                        SimultriaEnvironmentIds.Development,
                        session,
                        out registration,
                        out _,
                        out string registrationError),
                    Is.True,
                    registrationError);

                Assert.That(
                    SimultriaViewerConnectionStatus
                        .TryResolveAuthenticationTarget(
                            selectedProfile,
                            out _,
                            out _),
                    Is.False);
            }
            finally
            {
                registration?.Dispose();
                UnityEngine.Object.DestroyImmediate(selectedProfile);
                UnityEngine.Object.DestroyImmediate(selectedApiProfile);
            }
        }

        [Test]
        public void DefaultApiClientCompositionUsesTheSameSessionAuthProvider()
        {
            ViewerAuthenticationSession session =
                ViewerAuthenticationSession.CreateTransient();
            IApiAuthProvider capturedProvider = null;
            ApiClientConfig capturedConfig = null;

            IApiClient client =
                SimultriaViewerConnectionAuthentication
                    .CreateSessionApiClient(
                        session,
                        "https://api.example.test/v2",
                        (config, provider) =>
                        {
                            capturedConfig = config;
                            capturedProvider = provider;
                            return ApiClientFactory.CreateDefault();
                        });

            Assert.That(client, Is.Not.Null);
            Assert.That(capturedProvider, Is.SameAs(session.ApiAuthProvider));
            Assert.That(
                capturedConfig.BaseUrl,
                Is.EqualTo("https://api.example.test/v2"));
            UnityEngine.Object.DestroyImmediate(capturedConfig);
        }

        [Test]
        public void EditorLeaseYieldsToRuntimeTargetAndRestoresAfterItLeaves()
        {
            SimultriaApiProfile apiProfile = configuredLegacyProfile;
            var ownerRebinds = new List<string>();
            var lease = new SimultriaViewerEditorAuthenticationLease(
                () => new SimultriaViewerEditorAuthenticationConfiguration(
                    apiProfile,
                    SimultriaEnvironmentIds.Development),
                (current, replacement) =>
                    ownerRebinds.Add(current + "->" + replacement));
            IDisposable runtimeRegistration = null;
            try
            {
                lease.Reconcile(suspendForPlayMode: false);
                Assert.That(lease.IsRegistered, Is.True);
                Assert.That(
                    ViewerAuthenticationTargetRegistry.Targets.Count,
                    Is.EqualTo(1));
                Assert.That(
                    ViewerAuthenticationTargetRegistry.Targets[0].Id,
                    Is.EqualTo(
                        SimultriaViewerConnectionAuthentication
                            .DefaultTargetId));
                Assert.That(
                    ownerRebinds,
                    Is.EqualTo(new[]
                    {
                        "report-viewer->simultria-viewer"
                    }));

                runtimeRegistration =
                    ViewerAuthenticationTargetRegistry.Register(
                        "runtime-viewer-test",
                        "Runtime Viewer",
                        ViewerAuthenticationSession.CreateTransient());
                lease.Reconcile(suspendForPlayMode: false);

                Assert.That(lease.IsRegistered, Is.False);
                Assert.That(
                    ViewerAuthenticationTargetRegistry.Targets.Count,
                    Is.EqualTo(1));
                Assert.That(
                    ViewerAuthenticationTargetRegistry.Targets[0].Id,
                    Is.EqualTo("runtime-viewer-test"));

                runtimeRegistration.Dispose();
                runtimeRegistration = null;
                lease.Reconcile(suspendForPlayMode: false);
                Assert.That(lease.IsRegistered, Is.True);
                Assert.That(
                    ViewerAuthenticationTargetRegistry.Targets.Count,
                    Is.EqualTo(1));
                Assert.That(
                    ownerRebinds,
                    Has.All.EqualTo("report-viewer->simultria-viewer"));

                lease.Reconcile(suspendForPlayMode: true);
                Assert.That(lease.IsRegistered, Is.False);
                Assert.That(
                    ViewerAuthenticationTargetRegistry.Targets,
                    Is.Empty);
            }
            finally
            {
                runtimeRegistration?.Dispose();
                lease.Dispose();
            }
        }

        [Test]
        public void EditorLeaseDoesNotRequireProjectOrModelContext()
        {
            SimultriaApiProfile apiProfile = configuredLegacyProfile;
            var lease = new SimultriaViewerEditorAuthenticationLease(
                () => new SimultriaViewerEditorAuthenticationConfiguration(
                    apiProfile,
                    SimultriaEnvironmentIds.Development));
            try
            {
                lease.Reconcile(suspendForPlayMode: false);

                Assert.That(lease.IsRegistered, Is.True);
                Assert.That(
                    ViewerAuthenticationTargetRegistry.Targets.Count,
                    Is.EqualTo(1));
            }
            finally
            {
                lease.Dispose();
            }
        }

        [Test]
        public void EditorLeaseAcceptsGenericConnectionConfiguration()
        {
            ApiConnectionProfile connectionProfile =
                CreateGenericConnectionProfile();
            var lease = new SimultriaViewerEditorAuthenticationLease(
                () => new SimultriaViewerEditorAuthenticationConfiguration(
                    connectionProfile,
                    SimultriaEnvironmentIds.Development));
            try
            {
                lease.Reconcile(suspendForPlayMode: false);

                Assert.That(lease.IsRegistered, Is.True);
                Assert.That(
                    ViewerAuthenticationTargetRegistry.Targets.Count,
                    Is.EqualTo(1));
            }
            finally
            {
                lease.Dispose();
                UnityEngine.Object.DestroyImmediate(connectionProfile);
            }
        }

        [Test]
        public void FailedEditorRegistrationRetriesBeforeRebindingOwner()
        {
            SimultriaApiProfile apiProfile = configuredLegacyProfile;
            int attempts = 0;
            var ownerRebinds = new List<string>();
            var lease = new SimultriaViewerEditorAuthenticationLease(
                () => new SimultriaViewerEditorAuthenticationConfiguration(
                    apiProfile,
                    SimultriaEnvironmentIds.Development),
                (current, replacement) =>
                    ownerRebinds.Add(current + "->" + replacement),
                (configuration, session) =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        return null;
                    }

                    return SimultriaViewerConnectionAuthentication.TryRegister(
                            configuration.ApiProfile,
                            configuration.EnvironmentId,
                            session,
                            out IDisposable registration,
                            out _,
                            out _)
                        ? registration
                        : null;
                });
            try
            {
                lease.Reconcile(suspendForPlayMode: false);
                Assert.That(lease.IsRegistered, Is.False);
                Assert.That(ownerRebinds, Is.Empty);

                lease.Reconcile(suspendForPlayMode: false);
                Assert.That(attempts, Is.EqualTo(2));
                Assert.That(lease.IsRegistered, Is.True);
                Assert.That(
                    ownerRebinds,
                    Is.EqualTo(new[]
                    {
                        "report-viewer->simultria-viewer"
                    }));
            }
            finally
            {
                lease.Dispose();
            }
        }

        [Test]
        public void EditorLeaseRebindsWhenEffectiveEnvironmentChanges()
        {
            SimultriaApiProfile apiProfile = configuredLegacyProfile;
            ApiEnvironmentId selectedEnvironment =
                SimultriaEnvironmentIds.Development;
            var attemptedEnvironments = new List<ApiEnvironmentId>();
            var registrations = new List<RecordingDisposable>();
            var lease = new SimultriaViewerEditorAuthenticationLease(
                () => new SimultriaViewerEditorAuthenticationConfiguration(
                    apiProfile,
                    selectedEnvironment),
                registerTarget: (configuration, _) =>
                {
                    attemptedEnvironments.Add(configuration.EnvironmentId);
                    var registration = new RecordingDisposable();
                    registrations.Add(registration);
                    return registration;
                });
            try
            {
                lease.Reconcile(suspendForPlayMode: false);
                selectedEnvironment = SimultriaEnvironmentIds.Testing;
                lease.Invalidate();
                lease.Reconcile(suspendForPlayMode: false);

                Assert.That(
                    attemptedEnvironments,
                    Is.EqualTo(new[]
                    {
                        SimultriaEnvironmentIds.Development,
                        SimultriaEnvironmentIds.Testing
                    }));
                Assert.That(registrations[0].Disposed, Is.True);
                Assert.That(registrations[1].Disposed, Is.False);
            }
            finally
            {
                lease.Dispose();
            }

            Assert.That(registrations[1].Disposed, Is.True);
        }

        [Test]
        public async Task RuntimeLifetimeClearsAndReleasesWhenRegistrationThrows()
        {
            ViewerAuthenticationSession session =
                ViewerAuthenticationSession.CreateTransient();
            await session.ReplaceAccessTokenAsync("temporary-token");
            bool released = false;
            var lifetime = new SimultriaViewerRuntimeConnectionLifetime(
                new ThrowingDisposable(),
                session,
                () => released = true);

            Assert.Throws<InvalidOperationException>(() => lifetime.Dispose());

            Assert.That(session.Status.HasAccessToken, Is.False);
            Assert.That(released, Is.True);
            Assert.DoesNotThrow(() => lifetime.Dispose());
        }

        private sealed class ThrowingDisposable : IDisposable
        {
            public void Dispose()
            {
                throw new InvalidOperationException("Subscriber failed.");
            }
        }

        private sealed class RecordingDisposable : IDisposable
        {
            internal bool Disposed { get; private set; }

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private ApiConnectionProfile CreateGenericConnectionProfile()
        {
            return ApiConnectionProfile.CreateTransient(
                configuredLegacyProfile.Environments,
                configuredLegacyProfile.EndpointCatalog,
                SimultriaEnvironmentDescriptors.Standard);
        }

        private SimultriaApiProfile CreateConfiguredLegacyProfile()
        {
            SimultriaApiProfile packageProfile =
                SimultriaApiProfileDefaults.Load();
            Assert.That(packageProfile, Is.Not.Null);
            var environments = new List<ApiEnvironmentProfile>();
            foreach (ApiEnvironmentProfile source in
                     packageProfile.Environments)
            {
                if (source == null ||
                    !source.TryGetId(out var environmentId) ||
                    environmentId != SimultriaEnvironmentIds.Development)
                {
                    environments.Add(source);
                    continue;
                }

                configuredDevelopmentEnvironment =
                    UnityEngine.Object.Instantiate(source);
                Assert.That(
                    configuredDevelopmentEnvironment.TryGetClient(
                        SimultriaClientIds.Primary,
                        out ApiNamedClientDefinition client),
                    Is.True);
                client.BaseUrl = "https://simultria-viewer.invalid";
                environments.Add(configuredDevelopmentEnvironment);
            }

            return SimultriaApiProfile.CreateTransient(
                environments,
                packageProfile.EndpointCatalog);
        }
    }
}
