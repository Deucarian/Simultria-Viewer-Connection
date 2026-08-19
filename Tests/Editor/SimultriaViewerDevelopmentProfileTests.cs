using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Deucarian.API.Configuration;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
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
        public void ResolvesAssignedGenericConnectionProfile()
        {
            SimultriaApiProfile legacyProfile =
                SimultriaApiProfileDefaults.Load();
            Assert.That(legacyProfile, Is.Not.Null);
            ApiEnvironmentProfile configuredDevelopment = null;
            var environments = new List<ApiEnvironmentProfile>();
            foreach (ApiEnvironmentProfile source in legacyProfile.Environments)
            {
                if (source == null ||
                    !source.TryGetId(out var environmentId) ||
                    environmentId != SimultriaEnvironmentIds.Development)
                {
                    environments.Add(source);
                    continue;
                }

                configuredDevelopment = Object.Instantiate(source);
                Assert.That(
                    configuredDevelopment.TryGetClient(
                        SimultriaClientIds.Primary,
                        out ApiNamedClientDefinition client),
                    Is.True);
                client.BaseUrl = "https://simultria-viewer.invalid";
                environments.Add(configuredDevelopment);
            }

            ApiConnectionProfile connectionProfile =
                ApiConnectionProfile.CreateTransient(
                    environments,
                    legacyProfile.EndpointCatalog,
                    SimultriaEnvironmentDescriptors.Standard);
            try
            {
                profile.ConnectionProfileReference = connectionProfile;

                Assert.That(
                    profile.TryResolveEnvironment(
                        out var status,
                        out string error),
                    Is.True,
                    error);
                Assert.That(status.IsResolved, Is.True, status.Message);
                Assert.That(
                    profile.EffectiveProfileReference,
                    Is.SameAs(connectionProfile));
            }
            finally
            {
                Object.DestroyImmediate(connectionProfile);
                Object.DestroyImmediate(configuredDevelopment);
            }
        }

        [Test]
        public void SerializedProfileKeepsOnlySafeConnectionCompatibility()
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
            Assert.That(
                fieldNames,
                Does.Contain("apiconnectionprofilereference"));
            Assert.That(
                fieldNames,
                Does.Contain("apiprofilereference"),
                "The legacy serialized reference must remain readable.");
        }
    }
}
