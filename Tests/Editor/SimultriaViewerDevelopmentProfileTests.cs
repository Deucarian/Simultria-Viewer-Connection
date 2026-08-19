using System.Linq;
using System.Reflection;
using Deucarian.API.Models;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection.Tests
{
    public sealed class SimultriaViewerDevelopmentProfileTests
    {
        private SimultriaViewerDevelopmentProfile profile;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<SimultriaViewerDevelopmentProfile>();
            profile.EnvironmentId = new ApiEnvironmentId("simultria.development");
            profile.ProjectId = 832;
            profile.ModelId = 41;
            profile.ModelVersionId = 7;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(profile);
        }

        [Test]
        public void CreatesTypedCredentialFreePayload()
        {
            profile.PlacementPosition = new Vector3(1f, 2f, 3f);
            profile.MetadataJson = "{\"source\":\"edit-mode\"}";

            bool created = profile.TryCreatePayload(12, out var payload, out string error);

            Assert.That(created, Is.True, error);
            Assert.That(payload.EnvironmentId, Is.EqualTo("simultria.development"));
            Assert.That(payload.ProjectId, Is.EqualTo(832));
            Assert.That(payload.ModelId, Is.EqualTo(41));
            Assert.That(payload.ModelVersionId, Is.EqualTo(7));
            Assert.That(payload.Placement.Position.X, Is.EqualTo(1f));
            Assert.That(payload.Metadata["source"].ToString(), Is.EqualTo("edit-mode"));
        }

        [Test]
        public void RejectsSecretLikeMetadataAtAnyDepth()
        {
            profile.MetadataJson = "{\"nested\":{\"access_token\":\"never\"}}";

            bool created = profile.TryCreatePayload(12, out _, out string error);

            Assert.That(created, Is.False);
            Assert.That(error, Does.Contain("secret-like"));
        }

        [Test]
        public void SerializedProfileHasNoLegacyOrSensitiveFields()
        {
            string[] fieldNames = typeof(SimultriaViewerDevelopmentProfile)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Select(field => field.Name.ToLowerInvariant())
                .ToArray();

            Assert.That(fieldNames, Does.Not.Contain("baseurl"));
            Assert.That(fieldNames, Does.Not.Contain("base_url"));
            Assert.That(fieldNames, Does.Not.Contain("accesstoken"));
            Assert.That(fieldNames, Does.Not.Contain("access_token"));
            Assert.That(fieldNames, Does.Not.Contain("is_default_context"));
            Assert.That(fieldNames.Any(name => name.Contains("login")), Is.False);
            Assert.That(fieldNames.Any(name => name.Contains("report")), Is.False);
            Assert.That(fieldNames.Any(name => name.Contains("media")), Is.False);
        }
    }
}
