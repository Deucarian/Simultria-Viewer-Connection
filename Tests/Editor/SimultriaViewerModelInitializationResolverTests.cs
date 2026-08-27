using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.Simultria.API.Models;
using Deucarian.Simultria.API.Services;
using NUnit.Framework;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class SimultriaViewerModelInitializationResolverTests
    {
        [Test]
        public async Task ResolvesIdsAndIgnoresHostProvidedModelUrl()
        {
            var resolver = CreateResolver(
                Version(17, "https://models.example/resolved"));
            SimultriaViewerInitializationPayload payload = Payload(17);
            payload.ModelUrl = "https://host-provided.invalid/untrusted";

            SimultriaViewerModelInitializationResolution result =
                await resolver.ResolveAsync(payload, CancellationToken.None);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ProjectId, Is.EqualTo(1));
            Assert.That(result.ModelId, Is.EqualTo(2));
            Assert.That(result.ModelVersionId, Is.EqualTo(17));
            Assert.That(
                result.ModelUrl,
                Is.EqualTo("https://models.example/resolved"));
            Assert.That(result.UsedRequestedVersion, Is.True);
        }

        [Test]
        public async Task MissingVersionUsesCanonicalLatestFallback()
        {
            var resolver = CreateResolver(
                Version(17, "https://models.example/older", "1"),
                Version(19, "https://models.example/latest", "2"));

            SimultriaViewerModelInitializationResolution result =
                await resolver.ResolveAsync(Payload(null));

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ModelVersionId, Is.EqualTo(19));
            Assert.That(result.UsedRequestedVersion, Is.False);
        }

        [Test]
        public async Task ResolvedBearerQueryFailsClosed()
        {
            var resolver = CreateResolver(
                Version(
                    17,
                    "https://models.example/model?access_token=secret"));

            SimultriaViewerModelInitializationResolution result =
                await resolver.ResolveAsync(Payload(17));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("unsafe_model_source"));
            Assert.That(result.ModelUrl, Is.Empty);
        }

        private static SimultriaViewerModelInitializationResolver
            CreateResolver(params SimultriaModelVersionDto[] versions) =>
            new SimultriaViewerModelInitializationResolver(
                (projectId, modelId, versionId, cancellationToken) =>
                    Task.FromResult(
                        SimultriaViewerModelResolver.ResolveFromProjects(
                            projectId,
                            modelId,
                            versionId,
                            Projects(versions))));

        private static SimultriaViewerInitializationPayload Payload(
            int? versionId) =>
            new SimultriaViewerInitializationPayload
            {
                Revision = 1,
                EnvironmentId = "simultria.development",
                ProjectId = 1,
                ModelId = 2,
                ModelVersionId = versionId
            };

        private static IEnumerable<SimultriaProjectDto> Projects(
            IEnumerable<SimultriaModelVersionDto> versions) =>
            new[]
            {
                new SimultriaProjectDto
                {
                    Id = 1,
                    Name = "Project",
                    Models = new List<SimultriaModelDto>
                    {
                        new SimultriaModelDto
                        {
                            Id = 2,
                            Name = "Model",
                            Versions = new List<SimultriaModelVersionDto>(
                                versions)
                        }
                    }
                }
            };

        private static SimultriaModelVersionDto Version(
            int id,
            string url,
            string order = null) =>
            new SimultriaModelVersionDto
            {
                Id = id,
                Name = "Version " + id,
                Version = "1.0." + id,
                Order = order,
                DownloadUrl = url
            };
    }
}
