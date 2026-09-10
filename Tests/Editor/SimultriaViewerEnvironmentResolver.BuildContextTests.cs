using System.Threading;
using System.Threading.Tasks;
using Deucarian.API.Models;
using Deucarian.CommandRouting;
using Deucarian.Simultria.API.Configuration;
using Deucarian.SimultriaViewerIntegration.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed partial class SimultriaViewerEnvironmentResolverTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void BuildContextOmitsManualOrAutomaticEditorEnvironment(bool automatic)
        {
            profile.EnvironmentId = SimultriaEnvironmentIds.Local;
            profile.EnvironmentResolutionMode = automatic
                ? SimultriaViewerEnvironmentResolutionMode.AutomaticFromUnityBuildVersion
                : SimultriaViewerEnvironmentResolutionMode.Manual;
            // Local is intentionally unconfigured. A compiled context does
            // not use this dropdown, Editor product, or Editor version override.
            profile.BuildProduct = "editor-only-product";
            profile.BuildVersionOverride = "editor-only-version";
            profile.LookupEnvironment = SimultriaUnityBuildLookupEnvironment.Development;

            Assert.That(SimultriaViewerWebGlDevelopmentExporter.TryCreateBuildCommand(
                profile, out var command, out string error), Is.True, error);
            string json = SimultriaViewerInitializationCommand.Serialize(command);

            Assert.That(json, Does.Not.Contain("environment_id"));
            Assert.That(json, Does.Not.Contain("simultria.local"));
            Assert.That(json, Does.Not.Contain("editor-only"));
            Assert.That(json, Does.Not.Contain("lookupEnvironment"));
            Assert.That(json, Does.Not.Contain("lookup_environment"));
            Assert.That(command.TryReadPayload(out SimultriaViewerInitializationPayload payload,
                out _), Is.True);
            Assert.That(payload.ProjectId, Is.EqualTo(profile.ProjectId));
            Assert.That(payload.ModelId, Is.EqualTo(profile.ModelId));
            Assert.That(SimultriaViewerBuildContextValidator.TryValidateJson(json, out error),
                Is.True, error);
            Assert.That(profile.EnvironmentId, Is.EqualTo(SimultriaEnvironmentIds.Local));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ExportedBuildContextUsesImmutableRecordOrProfileEnvironment(bool hasRecord)
        {
            profile.EnvironmentId = SimultriaEnvironmentIds.Local;
            var client = hasRecord
                ? BuildDirectoryClient.Success("compiled-1.0", "activity_viewer", "development")
                : ClassifiedFailure(404, MissingRecordBody);
            var resolution = await BuildResolver(client, SimultriaEnvironmentIds.Production)
                .ResolveForCurrentRuntimeAsync(FallbackConfiguration());
            Assert.That(resolution.Succeeded, Is.True, resolution.Message);
            Assert.That(resolution.UsedBuildProfileFallback, Is.EqualTo(!hasRecord));
            Assert.That(SimultriaViewerRuntimeEnvironment.TryActivate(resolution, out string error),
                Is.True, error);
            Assert.That(SimultriaViewerWebGlDevelopmentExporter.TryCreateBuildCommand(
                profile, out var generated, out error), Is.True, error);
            string json = SimultriaViewerInitializationCommand.Serialize(generated);
            Assert.That(new JsonCommandProtocolCodec().TryDecode(json,
                out CommandEnvelope decoded, out _), Is.True);

            SimultriaViewerInitializationPayload applied = null;
            var handler = new SimultriaViewerInitializationCommandHandler<object>(
                (application, payload, metadata, cancellation) =>
                {
                    applied = payload;
                    return Task.FromResult(CommandResult.Success());
                });
            CommandResult result = await handler.HandleAsync(
                new CommandExecutionContext<object>(new object(), decoded, decoded.CommandName),
                CancellationToken.None);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(applied.EnvironmentId, Is.EqualTo(resolution.EnvironmentId.Value));
            Assert.That(applied.EnvironmentId, Is.Not.EqualTo(SimultriaEnvironmentIds.Local.Value));
            Assert.That(applied.ProjectId, Is.EqualTo(profile.ProjectId));
        }

        [Test]
        public async Task ExplicitEditorExportRetainsEnvironmentAndMismatchRejection()
        {
            profile.ConnectionSettingsReference = CreateConnectionSettings(SimultriaEnvironmentIds.Local);
            profile.EnvironmentId = SimultriaEnvironmentIds.Local;
            Assert.That(SimultriaViewerDevelopmentCommandService.TryCreateCommand(profile,
                out var command, out string error), Is.True, error);
            string json = SimultriaViewerInitializationCommand.Serialize(command);
            Assert.That(json, Does.Contain("simultria.local"));

            var resolution = await BuildResolver(ClassifiedFailure(404, MissingRecordBody),
                    SimultriaEnvironmentIds.Production)
                .ResolveForCurrentRuntimeAsync(FallbackConfiguration());
            Assert.That(SimultriaViewerRuntimeEnvironment.TryActivate(resolution, out error), Is.True, error);
            bool applied = false;
            var handler = new SimultriaViewerInitializationCommandHandler<object>(
                (application, payload, metadata, cancellation) =>
                {
                    applied = true;
                    return Task.FromResult(CommandResult.Success());
                });
            CommandResult result = await handler.HandleAsync(
                new CommandExecutionContext<object>(new object(), command, command.CommandName),
                CancellationToken.None);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("environment_mismatch"));
            Assert.That(applied, Is.False);
        }

        [TestCase("simultria.local")]
        [TestCase("simultria.production")]
        [TestCase("")]
        [TestCase(null)]
        public void CompiledArtifactValidatorRejectsAnySerializedEnvironmentOverride(string environment)
        {
            JObject command = JObject.Parse(SimultriaViewerBuildTestFactory.CreateBuildContextJson());
            ((JObject)command["payload"])["environment_id"] = environment;

            Assert.That(SimultriaViewerBuildContextValidator.TryValidateJson(command.ToString(),
                out string error), Is.False);
            Assert.That(error, Does.Contain("defer"));
        }

        [Test]
        public void BuildContextStillRejectsInvalidModelSelection()
        {
            profile.ModelId = 0;
            Assert.That(SimultriaViewerWebGlDevelopmentExporter.TryCreateBuildCommand(profile,
                out _, out string error), Is.False);
            Assert.That(error, Does.Contain("Model ID"));
        }

        [Test]
        public void ExplicitEditorPayloadStillRequiresResolvedEnvironment()
        {
            Assert.That(profile.TryCreatePayload(1, default(ApiEnvironmentId), out _, out string error),
                Is.False);
            Assert.That(error, Does.Contain("effective Simultria environment"));
        }
    }
}
