using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Deucarian.API.Authentication;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
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

        [SetUp]
        public void SetUp()
        {
            hostSuspension =
                SimultriaViewerEditorAuthenticationHost.SuspendForTests();
        }

        [TearDown]
        public void TearDown()
        {
            hostSuspension?.Dispose();
            hostSuspension = null;
        }

        [Test]
        public void ExplicitEnvironmentRegistrationUsesStableSingleViewerIdentity()
        {
            SimultriaApiProfile apiProfile =
                SimultriaApiProfileDefaults.Load();
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
        public void RuntimeProviderCreatesOneStableSharedSessionAndApiClient()
        {
            var provider = new SimultriaViewerRuntimeConnectionProvider(
                SimultriaApiProfileDefaults.Load(),
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
            SimultriaApiProfile apiProfile =
                SimultriaApiProfileDefaults.Load();
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
                SimultriaApiProfileDefaults.Load();
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
            SimultriaApiProfile apiProfile =
                SimultriaApiProfileDefaults.Load();
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
            SimultriaApiProfile apiProfile =
                SimultriaApiProfileDefaults.Load();
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
        public void FailedEditorRegistrationRetriesBeforeRebindingOwner()
        {
            SimultriaApiProfile apiProfile =
                SimultriaApiProfileDefaults.Load();
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
            await Task.Yield();

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
    }
}
