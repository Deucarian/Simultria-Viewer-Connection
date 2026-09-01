using System.Linq;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Deucarian.SimultriaViewerIntegration.Editor;
using NUnit.Framework;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class SimultriaViewerDevelopmentWindowTests
    {
        [Test]
        public void WindowUsesCompactMinimumSize()
        {
            Assert.That(
                SimultriaViewerDevelopmentWindow.CompactMinimumSize,
                Is.EqualTo(new UnityEngine.Vector2(420f, 340f)));
        }

        [Test]
        public void ReadinessStopsAtMissingContext()
        {
            SimultriaViewerDevelopmentWindow.DevelopmentReadiness readiness =
                SimultriaViewerDevelopmentWindow.BuildReadiness(
                    false,
                    false,
                    false,
                    false,
                    false,
                    "Choose the project context.");

            Assert.That(
                readiness.Level,
                Is.EqualTo(
                    SimultriaViewerDevelopmentWindow
                        .DevelopmentReadinessLevel.NeedsAction));
            Assert.That(readiness.Message, Is.EqualTo("Choose the project context."));
        }

        [Test]
        public void ReadinessRequestsAuthenticationAfterEnvironmentResolution()
        {
            SimultriaViewerDevelopmentWindow.DevelopmentReadiness readiness =
                SimultriaViewerDevelopmentWindow.BuildReadiness(
                    true,
                    true,
                    false,
                    false,
                    false);

            Assert.That(
                readiness.Level,
                Is.EqualTo(
                    SimultriaViewerDevelopmentWindow
                        .DevelopmentReadinessLevel.NeedsAction));
            Assert.That(
                readiness.Message,
                Is.EqualTo("Sign in with Viewer Authentication."));
        }

        [Test]
        public void ReadinessWaitsForRouteOnlyDuringPlayMode()
        {
            SimultriaViewerDevelopmentWindow.DevelopmentReadiness editMode =
                SimultriaViewerDevelopmentWindow.BuildReadiness(
                    true,
                    true,
                    true,
                    false,
                    false);
            SimultriaViewerDevelopmentWindow.DevelopmentReadiness playMode =
                SimultriaViewerDevelopmentWindow.BuildReadiness(
                    true,
                    true,
                    true,
                    true,
                    false);

            Assert.That(
                editMode.Level,
                Is.EqualTo(
                    SimultriaViewerDevelopmentWindow
                        .DevelopmentReadinessLevel.Ready));
            Assert.That(
                playMode.Level,
                Is.EqualTo(
                    SimultriaViewerDevelopmentWindow
                        .DevelopmentReadinessLevel.Waiting));
            Assert.That(
                playMode.Message,
                Is.EqualTo("Waiting for the running viewer."));
        }

        [Test]
        public void BuildEnvironmentOptionsUsesCanonicalEnvironmentOrder()
        {
            ApiEnvironmentId emptyEnvironmentId = default;

            SimultriaViewerDevelopmentWindow.BuildEnvironmentOptions(
                emptyEnvironmentId,
                out string[] options,
                out ApiEnvironmentId[] values,
                out int selectedIndex);

            ApiEnvironmentId[] expectedEnvironmentIds =
            {
                SimultriaEnvironmentIds.Local,
                SimultriaEnvironmentIds.Development,
                SimultriaEnvironmentIds.Testing,
                SimultriaEnvironmentIds.Acceptance,
                SimultriaEnvironmentIds.Production
            };
            string[] expectedEnvironmentLabels =
                SimultriaEnvironmentDescriptors.All
                    .Select(descriptor => descriptor.DisplayName)
                    .ToArray();

            Assert.That(options, Is.Not.Null);
            Assert.That(values, Is.Not.Null);
            Assert.That(
                SimultriaEnvironmentDescriptors.All
                    .Select(descriptor => descriptor.EnvironmentId)
                    .ToArray(),
                Is.EqualTo(expectedEnvironmentIds));
            Assert.That(options, Is.EqualTo(expectedEnvironmentLabels));
            Assert.That(values, Is.EqualTo(expectedEnvironmentIds));
            Assert.That(selectedIndex, Is.EqualTo(1));
            Assert.That(
                values[selectedIndex],
                Is.EqualTo(SimultriaEnvironmentIds.Development));
            Assert.That(options, Has.Length.EqualTo(5));
            Assert.That(options[0], Is.EqualTo("Local"));
            Assert.That(values[0], Is.EqualTo(SimultriaEnvironmentIds.Local));
        }

        [Test]
        public void BuildEnvironmentOptionsKeepsEmptyLegacyValueOnDevelopment()
        {
            SimultriaViewerDevelopmentWindow.BuildEnvironmentOptions(
                default(ApiEnvironmentId),
                out string[] options,
                out ApiEnvironmentId[] values,
                out int selectedIndex);

            Assert.That(options[selectedIndex], Is.EqualTo("Development"));
            Assert.That(
                values[selectedIndex],
                Is.EqualTo(SimultriaEnvironmentIds.Development));
        }

        [Test]
        public void BuildEnvironmentOptionsSelectsTesting()
        {
            SimultriaViewerDevelopmentWindow.BuildEnvironmentOptions(
                SimultriaEnvironmentIds.Testing,
                out _,
                out ApiEnvironmentId[] values,
                out int selectedIndex);

            Assert.That(selectedIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(values[selectedIndex], Is.EqualTo(SimultriaEnvironmentIds.Testing));
            Assert.That(SimultriaEnvironmentIds.Testing.Value, Is.EqualTo("simultria.testing"));
            Assert.That(values[selectedIndex].Value, Is.EqualTo("simultria.testing"));
        }

        [Test]
        public void BuildEnvironmentOptionsTreatsLocalAsBuiltInAfterRoundTrip()
        {
            ApiEnvironmentId current = SimultriaEnvironmentIds.Local;
            AssertBuiltInEnvironmentSelection(
                current,
                "Local");

            SimultriaViewerDevelopmentWindow.BuildEnvironmentOptions(
                current,
                out _,
                out ApiEnvironmentId[] localValues,
                out _);
            current = localValues.Single(
                value => value.Equals(SimultriaEnvironmentIds.Development));
            AssertBuiltInEnvironmentSelection(
                current,
                "Development");

            SimultriaViewerDevelopmentWindow.BuildEnvironmentOptions(
                current,
                out _,
                out ApiEnvironmentId[] developmentValues,
                out _);
            current = developmentValues.Single(
                value => value.Equals(SimultriaEnvironmentIds.Local));
            AssertBuiltInEnvironmentSelection(
                current,
                "Local");
        }

        [Test]
        public void BuildEnvironmentOptionsPreservesCustomId()
        {
            var custom = new ApiEnvironmentId("simultria.custom");
            SimultriaViewerDevelopmentWindow.BuildEnvironmentOptions(
                custom,
                out string[] options,
                out ApiEnvironmentId[] values,
                out int selectedIndex);

            Assert.That(options, Is.Not.Empty);
            Assert.That(values, Is.Not.Empty);
            Assert.That(selectedIndex, Is.GreaterThan(0));
            Assert.That(
                options[selectedIndex],
                Is.EqualTo($"Custom ({custom.Value})"));
            Assert.That(values[selectedIndex], Is.EqualTo(custom));
        }

        [Test]
        public void BuildDirectoryOptionsRequireAnExplicitHostEnvironment()
        {
            SimultriaViewerDevelopmentWindow.BuildDirectoryEnvironmentOptions(
                default(ApiEnvironmentId),
                out string[] options,
                out ApiEnvironmentId[] values,
                out int selectedIndex);

            Assert.That(selectedIndex, Is.Zero);
            Assert.That(values[0].IsEmpty, Is.True);
            Assert.That(options[0], Does.StartWith("Choose"));
            Assert.That(
                values.Skip(1).ToArray(),
                Is.EqualTo(
                    SimultriaEnvironmentDescriptors.All
                        .Select(descriptor => descriptor.EnvironmentId)
                        .ToArray()));
        }

        [Test]
        public void BuildDirectoryOptionsSelectLocalAsBuiltIn()
        {
            SimultriaViewerDevelopmentWindow.BuildDirectoryEnvironmentOptions(
                SimultriaEnvironmentIds.Local,
                out string[] options,
                out ApiEnvironmentId[] values,
                out int selectedIndex);

            Assert.That(selectedIndex, Is.EqualTo(1));
            Assert.That(options[selectedIndex], Is.EqualTo("Local"));
            Assert.That(
                values[selectedIndex],
                Is.EqualTo(SimultriaEnvironmentIds.Local));
            Assert.That(options[selectedIndex], Does.Not.StartWith("Custom"));
        }

        [Test]
        public void BuildDirectoryOptionsPreserveUnknownCustomId()
        {
            var custom = new ApiEnvironmentId("simultria.private-preview");

            SimultriaViewerDevelopmentWindow.BuildDirectoryEnvironmentOptions(
                custom,
                out string[] options,
                out ApiEnvironmentId[] values,
                out int selectedIndex);

            Assert.That(
                options[selectedIndex],
                Is.EqualTo($"Custom ({custom.Value})"));
            Assert.That(values[selectedIndex], Is.EqualTo(custom));
        }

        private static void AssertBuiltInEnvironmentSelection(
            ApiEnvironmentId environmentId,
            string expectedLabel)
        {
            SimultriaViewerDevelopmentWindow.BuildEnvironmentOptions(
                environmentId,
                out string[] options,
                out ApiEnvironmentId[] values,
                out int selectedIndex);

            Assert.That(selectedIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(options[selectedIndex], Is.EqualTo(expectedLabel));
            Assert.That(values[selectedIndex], Is.EqualTo(environmentId));
            Assert.That(options[selectedIndex], Does.Not.StartWith("Custom"));
        }
    }
}
