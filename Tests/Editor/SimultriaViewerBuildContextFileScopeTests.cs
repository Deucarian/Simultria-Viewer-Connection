using System;
using System.Collections.Generic;
using System.IO;
using Deucarian.API.Configuration;
using Deucarian.API.Models;
using Deucarian.BuildPipeline;
using Deucarian.Simultria.API.Configuration;
using Deucarian.SimultriaViewerIntegration.Editor;
using NUnit.Framework;
using UnityEditor.Build;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class SimultriaViewerBuildContextFileScopeTests
    {
        private readonly List<UnityEngine.Object> ownedObjects =
            new List<UnityEngine.Object>();
        private string testRoot;
        private string current;
        private string currentMeta;
        private string legacy;
        private string legacyMeta;

        [SetUp]
        public void SetUp()
        {
            testRoot = Path.Combine(
                SimultriaViewerProjectFileBoundary.ProjectRoot,
                "Temp",
                "SimultriaViewerContextTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot);
            current = Path.Combine(testRoot, "simultria-viewer-context.json");
            currentMeta = current + ".meta";
            legacy = Path.Combine(testRoot, "dev-viewer-context.json");
            legacyMeta = legacy + ".meta";
        }

        [TearDown]
        public void TearDown()
        {
            SimultriaViewerBuildTestFactory.DestroyAll(ownedObjects);
            if (!string.IsNullOrWhiteSpace(testRoot) &&
                Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }

        [Test]
        public void ScopeRestoresExactBytesAndReassertsAfterRefresh()
        {
            byte[] originalCurrent = { 1, 2, 3, 4 };
            byte[] originalMeta = { 5, 6, 7 };
            byte[] originalLegacy = { 8, 9 };
            File.WriteAllBytes(current, originalCurrent);
            File.WriteAllBytes(currentMeta, originalMeta);
            File.WriteAllBytes(legacy, originalLegacy);
            int refreshCount = 0;
            var scope = CreateScope(() =>
            {
                refreshCount++;
                if (refreshCount == 2)
                {
                    File.WriteAllText(currentMeta, "regenerated");
                    File.WriteAllText(legacyMeta, "unexpected");
                }
            });

            scope.RemoveAll();
            File.WriteAllText(current, "temporary");
            File.WriteAllText(currentMeta, "temporary-meta");
            File.WriteAllText(legacy, "temporary-legacy");
            File.WriteAllText(legacyMeta, "temporary-legacy-meta");
            scope.Dispose();

            Assert.That(File.ReadAllBytes(current), Is.EqualTo(originalCurrent));
            Assert.That(File.ReadAllBytes(currentMeta), Is.EqualTo(originalMeta));
            Assert.That(File.ReadAllBytes(legacy), Is.EqualTo(originalLegacy));
            Assert.That(File.Exists(legacyMeta), Is.False);
        }

        [Test]
        public void ScopeRemovesInitiallyAbsentContextDirectoryAndMeta()
        {
            string streamingAssets = Path.Combine(
                testRoot,
                "StreamingAssets");
            current = Path.Combine(
                streamingAssets,
                "simultria-viewer-context.json");
            currentMeta = current + ".meta";
            legacy = Path.Combine(
                streamingAssets,
                "dev-viewer-context.json");
            legacyMeta = legacy + ".meta";
            var scope = CreateScope(() => { });

            scope.RemoveAll();
            Directory.CreateDirectory(streamingAssets);
            File.WriteAllText(current, "temporary");
            File.WriteAllText(currentMeta, "temporary-meta");
            File.WriteAllText(streamingAssets + ".meta", "directory-meta");
            scope.Dispose();

            Assert.That(Directory.Exists(streamingAssets), Is.False);
            Assert.That(File.Exists(streamingAssets + ".meta"), Is.False);
        }

        [Test]
        public void ScopeNeverRecursivelyDeletesUnexpectedDirectoryContent()
        {
            string streamingAssets = Path.Combine(
                testRoot,
                "StreamingAssets");
            current = Path.Combine(
                streamingAssets,
                "simultria-viewer-context.json");
            currentMeta = current + ".meta";
            legacy = Path.Combine(
                streamingAssets,
                "dev-viewer-context.json");
            legacyMeta = legacy + ".meta";
            var scope = CreateScope(() => { });

            Directory.CreateDirectory(streamingAssets);
            string unrelated = Path.Combine(streamingAssets, "unrelated.txt");
            File.WriteAllText(unrelated, "keep");
            File.WriteAllText(current, "temporary");

            Assert.Throws<InvalidOperationException>(() => scope.Dispose());
            Assert.That(File.ReadAllText(unrelated), Is.EqualTo("keep"));
            Assert.That(Directory.Exists(streamingAssets), Is.True);
        }

        [Test]
        public void PreparationFailureRestoresPartialExport()
        {
            File.WriteAllText(current, "original");
            File.WriteAllText(currentMeta, "original-meta");
            ApiConnectionSettings connection =
                SimultriaViewerBuildTestFactory.CreateConnection(ownedObjects);
            SimultriaViewerDevelopmentContext context =
                SimultriaViewerBuildTestFactory.CreateContext(
                    ownedObjects,
                    connection,
                    SimultriaEnvironmentIds.Local);
            var preparation = new SimultriaViewerBuildContextPreparation(
                () => CreateScope(() => { }),
                (SimultriaViewerDevelopmentContext profile,
                 ApiEnvironmentId environment,
                 out string message) =>
                {
                    File.WriteAllText(current, "partial");
                    File.WriteAllText(currentMeta, "partial-meta");
                    message = "failed";
                    return false;
                });

            Assert.Throws<BuildFailedException>(() =>
                preparation.Prepare(
                    DeucarianBuildEnvironment.Development,
                    context,
                    SimultriaEnvironmentIds.Local));
            Assert.That(File.ReadAllText(current), Is.EqualTo("original"));
            Assert.That(
                File.ReadAllText(currentMeta),
                Is.EqualTo("original-meta"));
            Assert.That(File.Exists(legacy), Is.False);
            Assert.That(File.Exists(legacyMeta), Is.False);
        }

        [Test]
        public void SuccessfulDevelopmentPreparationExportsAndRestores()
        {
            File.WriteAllText(legacy, "legacy-original");
            ApiConnectionSettings connection =
                SimultriaViewerBuildTestFactory.CreateConnection(ownedObjects);
            SimultriaViewerDevelopmentContext context =
                SimultriaViewerBuildTestFactory.CreateContext(
                    ownedObjects,
                    connection,
                    SimultriaEnvironmentIds.Local);
            var preparation = new SimultriaViewerBuildContextPreparation(
                () => CreateScope(() => { }),
                (SimultriaViewerDevelopmentContext profile,
                 ApiEnvironmentId environment,
                 out string message) =>
                {
                    File.WriteAllText(
                        current,
                        SimultriaViewerBuildTestFactory.CreateSafeContextJson(
                            environment));
                    message = string.Empty;
                    return true;
                });

            IDisposable scope = preparation.Prepare(
                DeucarianBuildEnvironment.Development,
                context,
                SimultriaEnvironmentIds.Local);
            Assert.That(File.Exists(current), Is.True);
            Assert.That(
                SimultriaViewerBuildContextValidator.TryValidateFile(
                    current,
                    out string issue),
                Is.True,
                issue);
            Assert.That(File.Exists(legacy), Is.False);

            scope.Dispose();
            Assert.That(File.Exists(current), Is.False);
            Assert.That(
                File.ReadAllText(legacy),
                Is.EqualTo("legacy-original"));
        }

        [Test]
        public void ProductionPreparationExportsNothingAndRestoresEverything()
        {
            File.WriteAllText(current, "current-original");
            File.WriteAllText(currentMeta, "current-meta-original");
            File.WriteAllText(legacy, "legacy-original");
            File.WriteAllText(legacyMeta, "legacy-meta-original");
            bool exporterCalled = false;
            var preparation = new SimultriaViewerBuildContextPreparation(
                () => CreateScope(() => { }),
                (SimultriaViewerDevelopmentContext profile,
                 ApiEnvironmentId environment,
                 out string message) =>
                {
                    exporterCalled = true;
                    message = string.Empty;
                    return true;
                });

            IDisposable scope = preparation.Prepare(
                DeucarianBuildEnvironment.Production,
                null,
                default);

            Assert.That(exporterCalled, Is.False);
            Assert.That(File.Exists(current), Is.False);
            Assert.That(File.Exists(currentMeta), Is.False);
            Assert.That(File.Exists(legacy), Is.False);
            Assert.That(File.Exists(legacyMeta), Is.False);

            scope.Dispose();
            Assert.That(
                File.ReadAllText(current),
                Is.EqualTo("current-original"));
            Assert.That(
                File.ReadAllText(currentMeta),
                Is.EqualTo("current-meta-original"));
            Assert.That(
                File.ReadAllText(legacy),
                Is.EqualTo("legacy-original"));
            Assert.That(
                File.ReadAllText(legacyMeta),
                Is.EqualTo("legacy-meta-original"));
        }

        [Test]
        public void ScopeRejectsBlankContainingRoot()
        {
            Assert.Throws<ArgumentException>(() =>
                new SimultriaViewerBuildContextFileScope(
                    current,
                    currentMeta,
                    legacy,
                    legacyMeta,
                    () => { },
                    " "));
        }

        [Test]
        public void BoundaryRejectsPathOutsideProject()
        {
            string outside = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N"),
                "simultria-viewer-context.json");

            Assert.That(
                SimultriaViewerProjectFileBoundary.TryNormalize(
                    outside,
                    SimultriaViewerProjectFileBoundary.ProjectRoot,
                    out _,
                    out string issue),
                Is.False);
            Assert.That(issue, Does.Contain("safe project"));
        }

        private SimultriaViewerBuildContextFileScope CreateScope(
            Action refresh)
        {
            return new SimultriaViewerBuildContextFileScope(
                current,
                currentMeta,
                legacy,
                legacyMeta,
                refresh,
                SimultriaViewerProjectFileBoundary.ProjectRoot);
        }
    }
}
