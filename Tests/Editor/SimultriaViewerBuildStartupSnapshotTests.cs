using System;
using Deucarian.API.Models;
using NUnit.Framework;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class SimultriaViewerBuildStartupSnapshotTests
    {
        [TestCase("simultria.local")]
        [TestCase("simultria.development")]
        [TestCase("simultria.testing")]
        [TestCase("simultria.acceptance")]
        [TestCase("simultria.production")]
        public void RoutedStatusRetainsOnlyBuiltInEnvironment(string id)
        {
            var status = new SimultriaViewerBuildStartupSnapshot(
                SimultriaViewerBuildStartupPhase.Routed,
                environmentId: new ApiEnvironmentId(id));
            Assert.That(status.EnvironmentId.Value, Is.EqualTo(id));
            Assert.That(status.FailureCode,
                Is.EqualTo(SimultriaViewerBuildStartupFailureCode.None));
        }

        [TestCase(SimultriaViewerBuildStartupPhase.Routed)]
        [TestCase(SimultriaViewerBuildStartupPhase.Fallback)]
        public void CustomIdentifierIsNeverExposed(SimultriaViewerBuildStartupPhase phase)
        {
            var status = new SimultriaViewerBuildStartupSnapshot(phase,
                environmentId: new ApiEnvironmentId("secret-like-customer-value"));
            Assert.That(status.EnvironmentId.IsEmpty, Is.True);
        }

        [TestCase(SimultriaViewerBuildStartupPhase.NotStarted)]
        [TestCase(SimultriaViewerBuildStartupPhase.Resolving)]
        [TestCase(SimultriaViewerBuildStartupPhase.Disposed)]
        public void NonRoutedStatusOmitsEvenKnownEnvironment(SimultriaViewerBuildStartupPhase phase)
        {
            var status = new SimultriaViewerBuildStartupSnapshot(phase,
                environmentId: new ApiEnvironmentId("simultria.production"));
            Assert.That(status.EnvironmentId.IsEmpty, Is.True);
        }

        [Test]
        public void FailureHasOnlySafeCodeAndNoEnvironment()
        {
            var status = new SimultriaViewerBuildStartupSnapshot(
                SimultriaViewerBuildStartupPhase.Failed,
                SimultriaViewerBuildStartupFailureCode.ProviderCreationFailed,
                new ApiEnvironmentId("simultria.production"));
            Assert.That(status.EnvironmentId.IsEmpty, Is.True);
            Assert.That(status.FailureCode,
                Is.EqualTo(SimultriaViewerBuildStartupFailureCode.ProviderCreationFailed));
            var properties = typeof(SimultriaViewerBuildStartupSnapshot).GetProperties();
            Assert.That(properties.Length, Is.EqualTo(3));
            foreach (var property in properties)
            {
                Assert.That(property.CanWrite, Is.False);
                Assert.That(property.PropertyType == typeof(string), Is.False);
            }
        }

        [TestCase(-1, 0)]
        [TestCase(6, 0)]
        [TestCase(4, 0)]
        [TestCase(4, -1)]
        [TestCase(4, 6)]
        [TestCase(2, 1)]
        public void InvalidPhaseOrFailureCombinationCannotBeConstructed(int phase, int code)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SimultriaViewerBuildStartupSnapshot(
                    (SimultriaViewerBuildStartupPhase)phase,
                    (SimultriaViewerBuildStartupFailureCode)code));
        }
    }
}
