using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class SimultriaViewerDevelopmentContextTests
    {
        private SimultriaViewerDevelopmentContext profile;

        [SetUp]
        public void SetUp()
        {
            profile = ScriptableObject.CreateInstance<SimultriaViewerDevelopmentContext>();
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
        public void ZeroVersionSelectsActiveVersionByOmittingExactPin()
        {
            profile.ModelVersionId = 0;

            bool created =
                profile.TryCreatePayload(12, out var payload, out string error);

            Assert.That(created, Is.True, error);
            Assert.That(payload.ModelVersionId, Is.Null);
        }

        [Test]
        public void NegativeVersionIsRejectedInsteadOfSelectingActiveVersion()
        {
            profile.ModelVersionId = -1;

            bool created = profile.TryCreatePayload(12, out _, out string error);

            Assert.That(created, Is.False);
            Assert.That(error, Does.Contain("zero for the active version"));
        }

        [Test]
        public void LegacyBlankManualEnvironmentNormalizesToDevelopment()
        {
            profile.EnvironmentResolutionMode =
                SimultriaViewerEnvironmentResolutionMode.Manual;
            profile.EnvironmentId = default(ApiEnvironmentId);

            bool created = profile.TryCreatePayload(
                12,
                out SimultriaViewerInitializationPayload payload,
                out string error);

            Assert.That(created, Is.True, error);
            Assert.That(
                profile.EnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Development));
            Assert.That(
                payload.EnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Development.Value));

            profile.EnvironmentId = SimultriaEnvironmentIds.Local;
            Assert.That(
                profile.EnvironmentId,
                Is.EqualTo(SimultriaEnvironmentIds.Local));
        }

        [Test]
        public void AutomaticProfileIgnoresItsUnusedManualEnvironmentField()
        {
            profile.EnvironmentResolutionMode =
                SimultriaViewerEnvironmentResolutionMode
                    .AutomaticFromUnityBuildVersion;
            profile.EnvironmentId = default(ApiEnvironmentId);

            bool hasError = SimultriaViewerIntegration.Editor
                .SimultriaViewerProjectChecks.TryGetManualEnvironmentError(
                    profile,
                    out string error);

            Assert.That(hasError, Is.False);
            Assert.That(error, Is.Null);
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
        public void ResolvesAssignedGenericConnectionSettings()
        {
            ApiServiceDefinition definition =
                SimultriaApiDefinitionDefaults.LoadServiceDefinition();
            Assert.That(definition, Is.Not.Null);
            Assert.That(
                definition.TryGetEnvironmentDescriptors(
                    out IReadOnlyList<ApiEnvironmentDescriptor> descriptors,
                    out string definitionError),
                Is.True,
                definitionError);
            var environments = new List<ApiEnvironmentProfile>();
            foreach (ApiEnvironmentDescriptor descriptor in descriptors)
            {
                ApiEnvironmentProfile environment =
                    ScriptableObject.CreateInstance<ApiEnvironmentProfile>();
                environment.EnvironmentId = descriptor.EnvironmentId.Value;
                environment.DisplayName = descriptor.DisplayName;
                environment.Clients.Add(new ApiNamedClientDefinition
                {
                    ClientId = SimultriaClientIds.Primary.Value,
                    BaseUrl = descriptor.EnvironmentId ==
                        SimultriaEnvironmentIds.Development
                        ? "https://simultria-viewer.invalid"
                        : string.Empty
                });
                environments.Add(environment);
            }

            ApiConnectionSettings connectionSettings =
                ApiConnectionSettings.CreateTransient(
                    environments,
                    definition);
            try
            {
                profile.ConnectionSettingsReference = connectionSettings;

                Assert.That(
                    profile.TryResolveEnvironment(
                        out var status,
                        out string error),
                    Is.True,
                    error);
                Assert.That(status.IsResolved, Is.True, status.Message);
                Assert.That(
                    profile.EffectiveProfileReference,
                    Is.SameAs(connectionSettings));
            }
            finally
            {
                Object.DestroyImmediate(connectionSettings);
                foreach (ApiEnvironmentProfile environment in environments)
                {
                    Object.DestroyImmediate(environment);
                }
            }
        }

        [Test]
        public void BlankLocalSelectionRemainsLocalAndNeverUsesDevelopmentHost()
        {
            ApiServiceDefinition definition =
                SimultriaApiDefinitionDefaults.LoadServiceDefinition();
            Assert.That(definition, Is.Not.Null);
            Assert.That(
                definition.TryGetEnvironmentDescriptors(
                    out IReadOnlyList<ApiEnvironmentDescriptor> descriptors,
                    out string definitionError),
                Is.True,
                definitionError);
            var environments = new List<ApiEnvironmentProfile>();
            foreach (ApiEnvironmentDescriptor descriptor in descriptors)
            {
                ApiEnvironmentProfile environment =
                    ScriptableObject.CreateInstance<ApiEnvironmentProfile>();
                environment.EnvironmentId = descriptor.EnvironmentId.Value;
                environment.DisplayName = descriptor.DisplayName;
                environment.Clients.Add(new ApiNamedClientDefinition
                {
                    ClientId = SimultriaClientIds.Primary.Value,
                    BaseUrl = descriptor.EnvironmentId ==
                        SimultriaEnvironmentIds.Development
                        ? "https://development-only.invalid"
                        : string.Empty
                });
                environments.Add(environment);
            }

            ApiConnectionSettings connectionSettings =
                ApiConnectionSettings.CreateTransient(
                    environments,
                    definition);
            try
            {
                profile.EnvironmentId = SimultriaEnvironmentIds.Local;
                profile.ConnectionSettingsReference = connectionSettings;

                Assert.That(
                    profile.TryResolveEnvironment(
                        out ApiEnvironmentStatus local,
                        out string error),
                    Is.False);
                Assert.That(local, Is.Not.Null);
                Assert.That(
                    local.EnvironmentId,
                    Is.EqualTo(SimultriaEnvironmentIds.Local));
                Assert.That(local.DisplayName, Is.EqualTo("Local"));
                Assert.That(
                    local.Stage,
                    Is.EqualTo(ApiEnvironmentStage.Local));
                Assert.That(
                    local.Stage,
                    Is.Not.EqualTo(ApiEnvironmentStage.Custom));
                Assert.That(
                    local.Availability,
                    Is.EqualTo(ApiEnvironmentAvailability.Unconfigured));
                Assert.That(error, Does.Contain("known but not configured"));

                Assert.That(
                    connectionSettings.TryCreateComposition(
                        out var composition,
                        out string compositionError),
                    Is.True,
                    compositionError);
                Assert.That(
                    composition.GetEnvironmentStatus(
                        SimultriaEnvironmentIds.Development).Availability,
                    Is.EqualTo(ApiEnvironmentAvailability.Configured));
                Assert.That(
                    SimultriaViewerIntegration.Editor
                        .SimultriaViewerDevelopmentCommandService.TryCreateCommand(
                            profile,
                            out var command,
                            out string commandError),
                    Is.False);
                Assert.That(command, Is.Null);
                Assert.That(
                    commandError,
                    Does.Contain("known but not configured"));
                Assert.That(
                    profile.EnvironmentId,
                    Is.EqualTo(SimultriaEnvironmentIds.Local));
            }
            finally
            {
                Object.DestroyImmediate(connectionSettings);
                foreach (ApiEnvironmentProfile environment in environments)
                {
                    Object.DestroyImmediate(environment);
                }
            }
        }

        [Test]
        public void SerializedProfileKeepsOnlySafeConnectionCompatibility()
        {
            string[] fieldNames = typeof(SimultriaViewerDevelopmentContext)
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
                Does.Contain("apiconnectionsettingsreference"));
            Assert.That(
                fieldNames.Any(name => name.Contains("apiprofile")),
                Is.False);
        }
    }
}
