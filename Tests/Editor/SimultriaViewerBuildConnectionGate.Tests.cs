using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Deucarian.API.Models;
using Deucarian.Authentication;
using Deucarian.Simultria.API.Configuration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed partial class SimultriaViewerEnvironmentResolverTests
    {
        [Test]
        public async Task GateReportsResolvingThenRoutedAndAllowsLateReplay()
        {
            using (var fixture = CreateGate())
            {
                var states = new List<SimultriaViewerBuildStartupSnapshot>();
                bool heldWhileRouted = false;
                bool activatedWhileRouted = false;
                Assert.That(fixture.Gate.StartupStatus.Phase,
                    Is.EqualTo(SimultriaViewerBuildStartupPhase.NotStarted));
                fixture.Gate.StartupStatusChanged += states.Add;
                fixture.Gate.StartupStatusChanged += status =>
                {
                    if (status.Phase == SimultriaViewerBuildStartupPhase.Routed)
                    {
                        heldWhileRouted = !fixture.Startup.enabled;
                        activatedWhileRouted = ReferenceEquals(
                            SimultriaViewerRuntimeEnvironment.Current,
                            fixture.Gate.Resolution);
                    }
                };
                fixture.Gate.BeginStartup();
                await fixture.Gate.PendingResolution;
                Assert.That(states.ConvertAll(status => status.Phase), Is.EqualTo(new[]
                {
                    SimultriaViewerBuildStartupPhase.Resolving,
                    SimultriaViewerBuildStartupPhase.Routed
                }));
                Assert.That(states[0].EnvironmentId.IsEmpty, Is.True);
                Assert.That(fixture.Gate.StartupStatus.EnvironmentId,
                    Is.EqualTo(SimultriaEnvironmentIds.Local));
                Assert.That(fixture.Startup.enabled, Is.True);
                Assert.That(heldWhileRouted, Is.True);
                Assert.That(activatedWhileRouted, Is.True);
                var lateStates = new List<SimultriaViewerBuildStartupSnapshot>();
                fixture.Gate.StartupStatusChanged += lateStates.Add;
                lateStates.Add(fixture.Gate.StartupStatus);
                Assert.That(lateStates[0], Is.SameAs(states[1]));
                fixture.Gate.BeginStartup();
                Assert.That(states.Count, Is.EqualTo(2));
                fixture.Gate.DisposeStartup();
                fixture.Gate.DisposeStartup();
                Assert.That(lateStates.Count, Is.EqualTo(2));
                Assert.That(lateStates[1].Phase,
                    Is.EqualTo(SimultriaViewerBuildStartupPhase.Disposed));
                Assert.That(fixture.Startup.enabled, Is.False);
                Assert.That(fixture.Gate.StartupStatus.EnvironmentId.IsEmpty, Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GateReportsOnlyTypedMissingRecordAsBuildProfileFallback(bool development)
        {
            var stamp = development ? SimultriaEnvironmentIds.Development
                : SimultriaEnvironmentIds.Production;
            using (var fixture = CreateGate(ClassifiedFailure(404, MissingRecordBody), stamp))
            {
                fixture.Gate.BeginStartup();
                await fixture.Gate.PendingResolution;
                Assert.That(fixture.Gate.StartupStatus.Phase,
                    Is.EqualTo(SimultriaViewerBuildStartupPhase.Fallback));
                Assert.That(fixture.Gate.StartupStatus.EnvironmentId, Is.EqualTo(stamp));
                Assert.That(fixture.Gate.Resolution.UsedBuildProfileFallback, Is.True);
                Assert.That(fixture.Startup.enabled, Is.True);
            }
        }

        [TestCase(404)]
        [TestCase(401)]
        [TestCase(503)]
        public async Task GateLookupFailureIsTerminalAndNotFallback(int status)
        {
            using (var fixture = CreateGate(BuildDirectoryClient.Failure(status)))
            {
                ExpectGateFailure(SimultriaViewerBuildStartupFailureCode.EnvironmentResolutionFailed);
                fixture.Gate.BeginStartup();
                await fixture.Gate.PendingResolution;
                AssertGateFailure(fixture,
                    SimultriaViewerBuildStartupFailureCode.EnvironmentResolutionFailed);
                fixture.Gate.BeginStartup();
                Assert.That(fixture.Gate.StartupStatus.Phase,
                    Is.EqualTo(SimultriaViewerBuildStartupPhase.Failed));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GateCatchesResolverFactoryExceptionOrNull(bool returnNull)
        {
            using (var fixture = CreateGate())
            {
                fixture.Gate.ConfigureForTests(fixture.Configuration,
                    new Behaviour[] { fixture.Startup }, null,
                    testResolverFactory: () => returnNull ? null
                        : throw new InvalidOperationException("token=never-publish"));
                ExpectGateFailure(SimultriaViewerBuildStartupFailureCode.EnvironmentResolutionFailed);
                fixture.Gate.BeginStartup();
                await fixture.Gate.PendingResolution;
                AssertGateFailure(fixture,
                    SimultriaViewerBuildStartupFailureCode.EnvironmentResolutionFailed);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GateCatchesProviderFactoryExceptionOrNull(bool returnNull)
        {
            using (var fixture = CreateGate())
            {
                fixture.Gate.ConfigureForTests(fixture.Configuration,
                    new Behaviour[] { fixture.Startup }, fixture.Resolver,
                    (settings, environment) => returnNull ? null
                        : throw new InvalidOperationException("https://secret.invalid/token"));
                ExpectGateFailure(SimultriaViewerBuildStartupFailureCode.ProviderCreationFailed);
                fixture.Gate.BeginStartup();
                await fixture.Gate.PendingResolution;
                AssertGateFailure(fixture,
                    SimultriaViewerBuildStartupFailureCode.ProviderCreationFailed);
                Assert.That(SimultriaViewerRuntimeEnvironment.Current, Is.Null);
            }
        }

        [Test]
        public async Task GateHandlesConnectionRemovedWhileLookupWasPending()
        {
            var client = new DeferredGateClient();
            using (var fixture = CreateGate(client))
            {
                fixture.Gate.BeginStartup();
                fixture.Configuration.ConnectionSettings = null;
                ExpectGateFailure(SimultriaViewerBuildStartupFailureCode.ProviderCreationFailed);
                client.Completion.SetResult(true);
                await fixture.Gate.PendingResolution;
                AssertGateFailure(fixture,
                    SimultriaViewerBuildStartupFailureCode.ProviderCreationFailed);
            }
        }

        [Test]
        public async Task GateRegistrationConflictDoesNotActivateOrOpenStartup()
        {
            using (ViewerRuntimeConnectionProviderRegistry.Register(new GateProvider()))
            using (var fixture = CreateGate())
            {
                ExpectGateFailure(SimultriaViewerBuildStartupFailureCode.ProviderRegistrationFailed);
                fixture.Gate.BeginStartup();
                await fixture.Gate.PendingResolution;
                AssertGateFailure(fixture,
                    SimultriaViewerBuildStartupFailureCode.ProviderRegistrationFailed);
                Assert.That(SimultriaViewerRuntimeEnvironment.Current, Is.Null);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GateActivationRejectionOrExceptionFailsClosedAndReleasesProvider(bool throws)
        {
            using (var fixture = CreateGate())
            {
                Action<SimultriaViewerEnvironmentResolution> observer = value =>
                    throw new InvalidOperationException("token=never-publish");
                if (throws)
                    SimultriaViewerRuntimeEnvironment.Changed += observer;
                else
                    Assert.That(SimultriaViewerRuntimeEnvironment.TryActivate(
                        GateResolution(), out _), Is.True);
                try
                {
                    ExpectGateFailure(SimultriaViewerBuildStartupFailureCode.EnvironmentActivationFailed);
                    fixture.Gate.BeginStartup();
                    await fixture.Gate.PendingResolution;
                    AssertGateFailure(fixture,
                        SimultriaViewerBuildStartupFailureCode.EnvironmentActivationFailed);
                    using (ViewerRuntimeConnectionProviderRegistry.Register(new GateProvider())) { }
                }
                finally
                {
                    if (throws)
                        SimultriaViewerRuntimeEnvironment.Changed -= observer;
                }
            }
        }

        [Test]
        public async Task GateCancellationHasSafeTerminalStatus()
        {
            var client = new DeferredGateClient();
            using (var fixture = CreateGate(client))
            {
                fixture.Gate.BeginStartup();
                ExpectGateFailure(SimultriaViewerBuildStartupFailureCode.StartupCancelled);
                client.Completion.SetCanceled();
                await fixture.Gate.PendingResolution;
                AssertGateFailure(fixture,
                    SimultriaViewerBuildStartupFailureCode.StartupCancelled);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GateDisposalRejectsLateLookupSuccessAndException(bool throws)
        {
            var client = new DeferredGateClient();
            using (var fixture = CreateGate(client))
            {
                var phases = new List<SimultriaViewerBuildStartupPhase>();
                fixture.Gate.StartupStatusChanged += status => phases.Add(status.Phase);
                fixture.Gate.BeginStartup();
                fixture.Gate.DisposeStartup();
                if (throws)
                    client.Completion.SetException(new InvalidOperationException("secret"));
                else
                    client.Completion.SetResult(true);
                await fixture.Gate.PendingResolution;
                fixture.Gate.BeginStartup();
                Assert.That(phases, Is.EqualTo(new[]
                {
                    SimultriaViewerBuildStartupPhase.Resolving,
                    SimultriaViewerBuildStartupPhase.Disposed
                }));
                Assert.That(fixture.Gate.Resolution, Is.Null);
                Assert.That(fixture.Startup.enabled, Is.False);
                Assert.That(SimultriaViewerRuntimeEnvironment.Current, Is.Null);
            }
        }

        [Test]
        public async Task DisposalFromEnvironmentActivationObserverDoesNotOpenStartup()
        {
            using (var fixture = CreateGate())
            {
                Action<SimultriaViewerEnvironmentResolution> observer = value =>
                    fixture.Gate.DisposeStartup();
                SimultriaViewerRuntimeEnvironment.Changed += observer;
                try
                {
                    fixture.Gate.BeginStartup();
                    await fixture.Gate.PendingResolution;
                    Assert.That(fixture.Gate.StartupStatus.Phase,
                        Is.EqualTo(SimultriaViewerBuildStartupPhase.Disposed));
                    Assert.That(fixture.Startup.enabled, Is.False);
                    using (ViewerRuntimeConnectionProviderRegistry.Register(new GateProvider())) { }
                    // Status cleanup does not erase or replace the immutable
                    // routing decision which activation already published.
                    Assert.That(SimultriaViewerRuntimeEnvironment.Current,
                        Is.SameAs(fixture.Gate.Resolution));
                }
                finally
                {
                    SimultriaViewerRuntimeEnvironment.Changed -= observer;
                }
            }
        }

        [Test]
        public async Task GateStatusObserverExceptionCannotBreakStartupOrOtherObservers()
        {
            using (var fixture = CreateGate())
            {
                var states = new List<SimultriaViewerBuildStartupSnapshot>();
                fixture.Gate.StartupStatusChanged += status =>
                    throw new InvalidOperationException("token=never-publish");
                fixture.Gate.StartupStatusChanged += states.Add;
                fixture.Gate.BeginStartup();
                await fixture.Gate.PendingResolution;
                Assert.That(fixture.Startup.enabled, Is.True);
                fixture.Gate.DisposeStartup();
                Assert.That(states.Count, Is.EqualTo(3));
                Assert.That(states[2].Phase,
                    Is.EqualTo(SimultriaViewerBuildStartupPhase.Disposed));
            }
        }

        [TestCase(SimultriaViewerBuildStartupPhase.Resolving)]
        [TestCase(SimultriaViewerBuildStartupPhase.Routed)]
        public async Task GateObserverDisposalDoesNotReplaySupersededStateOrOpenStartup(
            SimultriaViewerBuildStartupPhase disposeAt)
        {
            using (var fixture = CreateGate())
            {
                var phases = new List<SimultriaViewerBuildStartupPhase>();
                fixture.Gate.StartupStatusChanged += status =>
                {
                    if (status.Phase == disposeAt)
                        fixture.Gate.DisposeStartup();
                };
                fixture.Gate.StartupStatusChanged += status => phases.Add(status.Phase);
                fixture.Gate.BeginStartup();
                await fixture.Gate.PendingResolution;
                Assert.That(phases, Has.No.Member(disposeAt));
                Assert.That(phases[phases.Count - 1],
                    Is.EqualTo(SimultriaViewerBuildStartupPhase.Disposed));
                Assert.That(fixture.Startup.enabled, Is.False);
                using (ViewerRuntimeConnectionProviderRegistry.Register(new GateProvider())) { }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GateDisposalInsideProviderCreationOrRegistrationCannotLeakRegistration(bool registration)
        {
            using (var fixture = CreateGate())
            {
                fixture.Gate.ConfigureForTests(fixture.Configuration,
                    new Behaviour[] { fixture.Startup }, fixture.Resolver, (settings, id) =>
                    {
                        if (!registration)
                            fixture.Gate.DisposeStartup();
                        return new GateProvider(registration
                            ? (Action)fixture.Gate.DisposeStartup : null);
                    });
                fixture.Gate.BeginStartup();
                await fixture.Gate.PendingResolution;
                Assert.That(fixture.Gate.StartupStatus.Phase,
                    Is.EqualTo(SimultriaViewerBuildStartupPhase.Disposed));
                Assert.That(fixture.Startup.enabled, Is.False);
                Assert.That(SimultriaViewerRuntimeEnvironment.Current, Is.Null);
                using (ViewerRuntimeConnectionProviderRegistry.Register(new GateProvider())) { }
            }
        }

        [Test]
        public void GateOwnershipCheckRequiresExplicitNonSelfBehaviour()
        {
            using (var fixture = CreateGate())
            {
                Assert.That(fixture.Gate.ContainsStartupBehaviour(fixture.Startup), Is.True);
                Assert.That(fixture.Gate.ContainsStartupBehaviour(fixture.Gate), Is.False);
                Assert.That(fixture.Gate.ContainsStartupBehaviour(null), Is.False);
                var unrelated = fixture.Gate.gameObject.AddComponent<AudioListener>();
                Assert.That(fixture.Gate.ContainsStartupBehaviour(unrelated), Is.False);
            }
        }

        private static void ExpectGateFailure(SimultriaViewerBuildStartupFailureCode code) =>
            LogAssert.Expect(LogType.Error, new Regex("Viewer startup stopped: " + code + "\\."));

        private static void AssertGateFailure(GateHarness fixture,
            SimultriaViewerBuildStartupFailureCode code)
        {
            Assert.That(fixture.Gate.StartupStatus.Phase,
                Is.EqualTo(SimultriaViewerBuildStartupPhase.Failed));
            Assert.That(fixture.Gate.StartupStatus.FailureCode, Is.EqualTo(code));
            Assert.That(fixture.Gate.StartupStatus.EnvironmentId.IsEmpty, Is.True);
            Assert.That(fixture.Startup.enabled, Is.False);
        }
    }
}
