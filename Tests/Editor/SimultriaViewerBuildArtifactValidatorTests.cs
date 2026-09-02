using System;
using System.IO;
using Deucarian.BuildPipeline;
using Deucarian.Simultria.API.Configuration;
using Deucarian.SimultriaViewerIntegration.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class SimultriaViewerBuildArtifactValidatorTests
    {
        private string outputRoot;
        private string unsafeOutputRoot;

        [SetUp]
        public void SetUp()
        {
            outputRoot = Path.Combine(
                SimultriaViewerProjectFileBoundary.ProjectRoot,
                "Builds",
                "SimultriaViewerArtifactTests",
                Guid.NewGuid().ToString("N"));
            unsafeOutputRoot = Path.Combine(
                SimultriaViewerProjectFileBoundary.ProjectRoot,
                "Temp",
                "SimultriaViewerUnsafeArtifactTests",
                Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(outputRoot) &&
                Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, true);
            }

            if (!string.IsNullOrWhiteSpace(unsafeOutputRoot) &&
                Directory.Exists(unsafeOutputRoot))
            {
                Directory.Delete(unsafeOutputRoot, true);
            }
        }

        [TestCase("https://example.invalid/?token=value")]
        [TestCase("access_token=value")]
        [TestCase("password=value")]
        [TestCase("secret=value")]
        [TestCase("authorization=value")]
        [TestCase("cookie=value")]
        [TestCase("api_key=value")]
        [TestCase("apikey=value")]
        [TestCase("credential=value")]
        [TestCase("Authorization: Bearer opaque-value")]
        [TestCase("token: opaque-value")]
        [TestCase("x-api-key=opaque-value")]
        [TestCase("Bearer opaque-value")]
        [TestCase("Basic dXNlcjpwYXNz")]
        public void ContextValidatorRejectsCredentialLikeMetadataValue(
            string unsafeValue)
        {
            JObject command = JObject.Parse(
                SimultriaViewerBuildTestFactory.CreateSafeContextJson(
                    SimultriaEnvironmentIds.Development));
            ((JObject)command["payload"])["metadata"] = new JObject
            {
                ["note"] = unsafeValue
            };

            Assert.That(
                SimultriaViewerBuildContextValidator.TryValidateJson(
                    command.ToString(),
                    out string issue),
                Is.False);
            Assert.That(issue, Does.Contain("credential-like"));
        }

        [Test]
        public void DevelopmentRequiresOneSafeCurrentContext()
        {
            string relative =
                "StreamingAssets/simultria-viewer-context.json";
            string fullPath = Path.Combine(
                outputRoot,
                relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(
                fullPath,
                SimultriaViewerBuildTestFactory.CreateSafeContextJson(
                    SimultriaEnvironmentIds.Local));
            DeucarianBuildArtifactManifest manifest = Manifest(relative);
            var request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Development,
                OutputPath = outputRoot
            };

            DeucarianBuildValidationResult result =
                new SimultriaViewerBuildArtifactValidator().Validate(
                    request,
                    manifest);

            Assert.That(result.IsValid, Is.True, result.Format("artifacts"));
        }

        [Test]
        public void DevelopmentRejectsLegacyOrEscapingContext()
        {
            var request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Development,
                OutputPath = outputRoot
            };
            DeucarianBuildArtifactManifest legacy = Manifest(
                "StreamingAssets/dev-viewer-context.json");
            DeucarianBuildValidationResult legacyResult =
                new SimultriaViewerBuildArtifactValidator().Validate(
                    request,
                    legacy);
            Assert.That(legacyResult.IsValid, Is.False);

            DeucarianBuildArtifactManifest escaping = Manifest(
                "../simultria-viewer-context.json");
            DeucarianBuildValidationResult escapingResult =
                new SimultriaViewerBuildArtifactValidator().Validate(
                    request,
                    escaping);
            Assert.That(escapingResult.IsValid, Is.False);
        }

        [Test]
        public void DevelopmentRejectsCurrentContextOutsideLoadablePath()
        {
            const string relative =
                "Other/simultria-viewer-context.json";
            string fullPath = Path.Combine(
                outputRoot,
                relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(
                fullPath,
                SimultriaViewerBuildTestFactory.CreateSafeContextJson(
                    SimultriaEnvironmentIds.Local));
            var request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Development,
                OutputPath = outputRoot
            };

            DeucarianBuildValidationResult result =
                new SimultriaViewerBuildArtifactValidator().Validate(
                    request,
                    Manifest(relative));

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Issues, Has.Some.Contains("StreamingAssets"));
        }

        [TestCase("StreamingAssets/simultria-viewer-context.json")]
        [TestCase("StreamingAssets/dev-viewer-context.json")]
        public void ProductionRejectsAndRemovesContaminatedOutput(
            string relative)
        {
            string fullPath = Path.Combine(
                outputRoot,
                relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, "contaminated");
            var request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Production,
                OutputPath = outputRoot
            };

            DeucarianBuildValidationResult result =
                new SimultriaViewerBuildArtifactValidator().Validate(
                    request,
                    Manifest(relative));

            Assert.That(result.IsValid, Is.False);
            Assert.That(Directory.Exists(outputRoot), Is.False);
            Assert.That(result.Issues, Has.Some.Contains("removed"));
        }

        [Test]
        public void ProductionFailsClosedWhenOutputCannotBeRemovedSafely()
        {
            Directory.CreateDirectory(unsafeOutputRoot);
            File.WriteAllText(
                Path.Combine(unsafeOutputRoot, "unowned.txt"),
                "not a Deucarian build output");
            var request = new DeucarianBuildRequest
            {
                Environment = DeucarianBuildEnvironment.Production,
                OutputPath = unsafeOutputRoot
            };

            DeucarianBuildValidationResult result =
                new SimultriaViewerBuildArtifactValidator().Validate(
                    request,
                    Manifest(
                        "StreamingAssets/" +
                        "simultria-viewer-context.json"));

            Assert.That(result.IsValid, Is.False);
            Assert.That(Directory.Exists(unsafeOutputRoot), Is.True);
            Assert.That(
                result.Issues,
                Has.Some.Contains("could not be removed safely"));
        }

        private static DeucarianBuildArtifactManifest Manifest(
            params string[] relativePaths)
        {
            var manifest = new DeucarianBuildArtifactManifest();
            for (int index = 0; index < relativePaths.Length; index++)
            {
                manifest.artifacts.Add(new DeucarianBuildArtifact
                {
                    relativePath = relativePaths[index]
                });
            }

            return manifest;
        }
    }
}
