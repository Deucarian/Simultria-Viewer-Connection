using System;
using System.Collections.Generic;
using Deucarian.API.Configuration;
using Deucarian.API.Models;
using Deucarian.BuildPipeline;
using Deucarian.Simultria.API.Configuration;
using Deucarian.SimultriaViewerIntegration.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class SimultriaViewerBuildLifecycleContributorTests
    {
        private readonly List<UnityEngine.Object> ownedObjects =
            new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            SimultriaViewerBuildTestFactory.DestroyAll(ownedObjects);
        }

        [Test]
        public void DisabledSceneEntriesDoNotInvalidateSoleEnabledScene()
        {
            var scenes = new[]
            {
                new EditorBuildSettingsScene(
                    "Assets/Viewer.unity",
                    true),
                new EditorBuildSettingsScene(
                    "Assets/Disabled.unity",
                    false)
            };

            Assert.That(
                SimultriaViewerBuildSceneInspector.TryGetEnabledScenePaths(
                    scenes,
                    out IReadOnlyList<string> paths,
                    out string issue),
                Is.True,
                issue);
            Assert.That(paths, Is.EqualTo(new[] { "Assets/Viewer.unity" }));
            Assert.That(issue, Is.Empty);
        }

        [Test]
        public void MultipleEnabledGateScenesApplyAndFailValidation()
        {
            Assert.That(
                SimultriaViewerBuildSceneInspector.TryGetEnabledScenePaths(
                    new[]
                    {
                        new EditorBuildSettingsScene(
                            "Assets/ViewerA.unity",
                            true),
                        new EditorBuildSettingsScene(
                            "Assets/ViewerB.unity",
                            true)
                    },
                    out IReadOnlyList<string> paths,
                    out string selectionIssue),
                Is.True);
            Assert.That(paths.Count, Is.EqualTo(2));
            Assert.That(selectionIssue, Does.Contain("exactly one"));

            ApiConnectionSettings connection = CreateConnection();
            SimultriaViewerBuildConfiguration configuration =
                SimultriaViewerBuildTestFactory.CreateConfiguration(
                    ownedObjects,
                    connection);
            var snapshot = new SimultriaViewerBuildSceneSnapshot(
                string.Empty,
                1,
                configuration,
                new[] { connection },
                selectionIssue);
            SimultriaViewerBuildLifecycleContributor contributor =
                CreateContributor(snapshot, null);
            var request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Production
            };

            Assert.That(contributor.AppliesTo(request), Is.True);
            DeucarianBuildValidationResult result =
                contributor.ValidateBeforeBuild(request);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Some.Contains("exactly one"));
        }

        [Test]
        public void SceneInspectionFailureDoesNotClaimUnrelatedBuild()
        {
            const string inspectionIssue =
                "The selected viewer scene could not be inspected.";
            SimultriaViewerBuildLifecycleContributor contributor =
                CreateContributor(
                    new FailingInspector(inspectionIssue),
                    null);
            var request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Production
            };

            Assert.That(contributor.AppliesTo(request), Is.False);
        }

        [Test]
        public void ZeroSceneSelectionDoesNotClaimUnrelatedBuild()
        {
            SimultriaViewerBuildLifecycleContributor contributor =
                CreateContributor(
                    new FailingInspector(
                        "The Build Profile must select one enabled scene."),
                    null);

            Assert.That(
                contributor.AppliesTo(new DeucarianBuildRequest
                {
                    Environment = DeucarianBuildEnvironment.Production
                }),
                Is.False);
        }

        [Test]
        public void SuccessfulInspectionWithoutViewerMarkersDoesNotApply()
        {
            SimultriaViewerBuildLifecycleContributor contributor =
                CreateContributor(
                    new SimultriaViewerBuildSceneSnapshot(
                        "Assets/Unrelated.unity",
                        0,
                        null,
                        Array.Empty<ApiConnectionSettings>()),
                    null);

            Assert.That(
                contributor.AppliesTo(new DeucarianBuildRequest
                {
                    Environment = DeucarianBuildEnvironment.Production
                }),
                Is.False);
        }

        [Test]
        public void ConnectionSourceWithoutGateAppliesAndFailsGateCount()
        {
            ApiConnectionSettings connection = CreateConnection();
            SimultriaViewerBuildLifecycleContributor contributor =
                CreateContributor(
                    new SimultriaViewerBuildSceneSnapshot(
                        "Assets/Viewer.unity",
                        0,
                        null,
                        new[] { connection }),
                    null);
            var request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Production
            };

            Assert.That(contributor.AppliesTo(request), Is.True);
            DeucarianBuildValidationResult result =
                contributor.ValidateBeforeBuild(request);
            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Issues,
                Is.EqualTo(new[]
                {
                    "The selected viewer scene must contain exactly one " +
                    "Simultria viewer build connection gate."
                }));
        }

        [Test]
        public void DetectedGateWithInspectionIssueAppliesAndFailsClosed()
        {
            const string inspectionIssue =
                "A selected gated viewer scene could not be inspected safely.";
            var snapshot = new SimultriaViewerBuildSceneSnapshot(
                "Assets/Viewer.unity",
                1,
                null,
                Array.Empty<ApiConnectionSettings>(),
                inspectionIssue);
            SimultriaViewerBuildLifecycleContributor contributor =
                CreateContributor(snapshot, null);
            var request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Production
            };

            Assert.That(contributor.AppliesTo(request), Is.True);
            DeucarianBuildValidationResult result =
                contributor.ValidateBeforeBuild(request);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Some.EqualTo(inspectionIssue));
        }

        [Test]
        public void ConfigurationRequiresMatchingSources()
        {
            ApiConnectionSettings connection = CreateConnection();
            ApiConnectionSettings other = CreateConnection();
            SimultriaViewerBuildConfiguration configuration =
                SimultriaViewerBuildTestFactory.CreateConfiguration(
                    ownedObjects,
                    connection);
            var request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Production
            };

            SimultriaViewerBuildLifecycleContributor mismatchContributor =
                CreateContributor(
                    new SimultriaViewerBuildSceneSnapshot(
                        "Assets/Viewer.unity",
                        1,
                        configuration,
                        new[] { other }),
                    null);
            Assert.That(mismatchContributor.AppliesTo(request), Is.True);
            DeucarianBuildValidationResult mismatch =
                mismatchContributor.ValidateBeforeBuild(request);
            Assert.That(mismatch.IsValid, Is.False);
            Assert.That(
                mismatch.Issues,
                Has.Some.Contains("must reference the build configuration"));
        }

        [Test]
        public void ConfigurationRequiresProductDirectoryPromotionsAndSource()
        {
            ApiConnectionSettings connection =
                SimultriaViewerBuildTestFactory.CreateConnection(
                    ownedObjects,
                    SimultriaEnvironmentIds.Acceptance);
            SimultriaViewerBuildConfiguration configuration =
                SimultriaViewerBuildTestFactory.CreateConfiguration(
                    ownedObjects,
                    connection);
            configuration.Product = " ";
            configuration.BuildDirectoryEnvironmentId = default;
            var snapshot = new SimultriaViewerBuildSceneSnapshot(
                "Assets/Viewer.unity",
                1,
                configuration,
                Array.Empty<ApiConnectionSettings>());

            DeucarianBuildValidationResult result = CreateContributor(
                    snapshot,
                    null)
                .ValidateBeforeBuild(new DeucarianBuildRequest
                {
                    Environment = DeucarianBuildEnvironment.Production
                });

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Some.Contains("product identifier"));
            Assert.That(
                result.Issues,
                Has.Some.Contains("at least one product"));
            Assert.That(
                result.Issues,
                Has.Some.Contains("build-directory environment"));
            Assert.That(
                result.Issues,
                Has.Some.Contains("Every promotable"));
        }

        [Test]
        public void DevelopmentAcceptsConfiguredLocalAndRejectsAutomatic()
        {
            ApiConnectionSettings connection = CreateConnection();
            SimultriaViewerBuildConfiguration configuration =
                SimultriaViewerBuildTestFactory.CreateConfiguration(
                    ownedObjects,
                    connection);
            SimultriaViewerDevelopmentContext context =
                SimultriaViewerBuildTestFactory.CreateContext(
                    ownedObjects,
                    connection,
                    SimultriaEnvironmentIds.Local);
            var snapshot = new SimultriaViewerBuildSceneSnapshot(
                "Assets/Viewer.unity",
                1,
                configuration,
                new[] { connection });
            SimultriaViewerBuildLifecycleContributor contributor =
                CreateContributor(snapshot, context);
            var request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Development
            };

            Assert.That(
                contributor.ValidateBeforeBuild(request).IsValid,
                Is.True);

            context.EnvironmentResolutionMode =
                SimultriaViewerEnvironmentResolutionMode
                    .AutomaticFromUnityBuildVersion;
            DeucarianBuildValidationResult automatic =
                contributor.ValidateBeforeBuild(request);
            Assert.That(automatic.IsValid, Is.False);
            Assert.That(automatic.Issues, Has.Some.Contains("Manual"));
        }

        [Test]
        public void DevelopmentRejectsBlankOrUnconfiguredLocal()
        {
            ApiConnectionSettings configured = CreateConnection();
            SimultriaViewerBuildConfiguration configuredBuild =
                SimultriaViewerBuildTestFactory.CreateConfiguration(
                    ownedObjects,
                    configured);
            SimultriaViewerDevelopmentContext blank =
                SimultriaViewerBuildTestFactory.CreateContext(
                    ownedObjects,
                    configured,
                    SimultriaEnvironmentIds.Local);
            blank.EnvironmentId = default;
            var request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Development
            };

            Assert.That(
                CreateContributor(
                        Snapshot(configuredBuild, configured),
                        blank)
                    .ValidateBeforeBuild(request).IsValid,
                Is.False);

            ApiConnectionSettings withoutLocal =
                SimultriaViewerBuildTestFactory.CreateConnection(
                    ownedObjects,
                    SimultriaEnvironmentIds.Local);
            SimultriaViewerBuildConfiguration unconfiguredBuild =
                SimultriaViewerBuildTestFactory.CreateConfiguration(
                    ownedObjects,
                    withoutLocal);
            SimultriaViewerDevelopmentContext unconfigured =
                SimultriaViewerBuildTestFactory.CreateContext(
                    ownedObjects,
                    withoutLocal,
                    SimultriaEnvironmentIds.Local);

            DeucarianBuildValidationResult result = CreateContributor(
                    Snapshot(unconfiguredBuild, withoutLocal),
                    unconfigured)
                .ValidateBeforeBuild(request);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Some.Contains("resolved"));
        }

        private ApiConnectionSettings CreateConnection() =>
            SimultriaViewerBuildTestFactory.CreateConnection(ownedObjects);

        private static SimultriaViewerBuildSceneSnapshot Snapshot(
            SimultriaViewerBuildConfiguration configuration,
            ApiConnectionSettings connection) =>
            new SimultriaViewerBuildSceneSnapshot(
                "Assets/Viewer.unity",
                1,
                configuration,
                new[] { connection });

        private static SimultriaViewerBuildLifecycleContributor
            CreateContributor(
                SimultriaViewerBuildSceneSnapshot snapshot,
                SimultriaViewerDevelopmentContext context)
        {
            return CreateContributor(new FixedInspector(snapshot), context);
        }

        private static SimultriaViewerBuildLifecycleContributor
            CreateContributor(
                ISimultriaViewerBuildSceneInspector inspector,
                SimultriaViewerDevelopmentContext context)
        {
            return new SimultriaViewerBuildLifecycleContributor(
                inspector,
                (out SimultriaViewerDevelopmentContext selected,
                 out string source,
                 out string error) =>
                {
                    selected = context;
                    source = "test";
                    error = context == null ? "missing" : string.Empty;
                    return context != null;
                },
                new NoOpPreparation(),
                new AcceptingArtifactValidator());
        }

        private sealed class FailingInspector :
            ISimultriaViewerBuildSceneInspector
        {
            private readonly string inspectionIssue;

            internal FailingInspector(string issue)
            {
                inspectionIssue = issue;
            }

            public bool TryInspect(
                DeucarianBuildRequest request,
                out SimultriaViewerBuildSceneSnapshot result,
                out string issue)
            {
                result = null;
                issue = inspectionIssue;
                return false;
            }
        }

        private sealed class FixedInspector :
            ISimultriaViewerBuildSceneInspector
        {
            private readonly SimultriaViewerBuildSceneSnapshot snapshot;

            internal FixedInspector(
                SimultriaViewerBuildSceneSnapshot value)
            {
                snapshot = value;
            }

            public bool TryInspect(
                DeucarianBuildRequest request,
                out SimultriaViewerBuildSceneSnapshot result,
                out string issue)
            {
                result = snapshot;
                issue = string.Empty;
                return snapshot != null;
            }
        }

        private sealed class NoOpPreparation :
            ISimultriaViewerBuildContextPreparation
        {
            public IDisposable Prepare(
                DeucarianBuildEnvironment environment,
                SimultriaViewerDevelopmentContext profile,
                ApiEnvironmentId effectiveEnvironmentId) =>
                new EmptyScope();
        }

        private sealed class AcceptingArtifactValidator :
            ISimultriaViewerBuildArtifactValidator
        {
            public DeucarianBuildValidationResult Validate(
                DeucarianBuildRequest request,
                DeucarianBuildArtifactManifest manifest) =>
                new DeucarianBuildValidationResult();
        }

        private sealed class EmptyScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
