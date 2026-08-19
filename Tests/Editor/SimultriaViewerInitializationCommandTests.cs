using System;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.CommandRouting;
using NUnit.Framework;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection.Tests
{
    public sealed class SimultriaViewerInitializationCommandTests
    {
        [Test]
        public void SerializesCanonicalInitializeViewerEnvelopeWithoutSecretsOrUrls()
        {
            CommandEnvelope command = SimultriaViewerInitializationCommand.Create(
                CreatePayload(25));

            string json = SimultriaViewerInitializationCommand.Serialize(command);
            Assert.That(json, Does.Contain("\"command\": \"initialize_viewer\""));
            Assert.That(json, Does.Contain("\"project_id\": 8"));
            Assert.That(json, Does.Contain(
                "\"source\": \"" +
                SimultriaViewerInitializationCommand.DevelopmentSource +
                "\""));
            Assert.That(json, Does.Not.Contain("access_token"));
            Assert.That(json, Does.Not.Contain("base_url"));
            Assert.That(json, Does.Not.Contain("http://"));
            Assert.That(json, Does.Not.Contain("https://"));
        }

        [Test]
        public async Task HandlerUsesCommandRoutingAndTypedPayload()
        {
            var application = new RecordingApplication();
            var handler = new SimultriaViewerInitializationCommandHandler<RecordingApplication>(
                (app, payload, metadata, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    app.Payload = payload;
                    app.Source = metadata.Source;
                    return Task.FromResult(CommandResult.Success());
                });
            GameObject owner = new GameObject("Command route");
            try
            {
                using (var runtime = new CommandRoutingRuntime<RecordingApplication>(
                           application,
                           new[] { handler }))
                {
                    CommandRoutePortBehaviour port =
                        owner.AddComponent<CommandRoutePortBehaviour>();
                    port.Initialize(runtime);

                    CommandEnvelope command =
                        SimultriaViewerInitializationCommand.Create(CreatePayload(31));
                    CommandRouteOutcome outcome = await port.RouteMessageAsync(
                        SimultriaViewerInitializationCommand.Serialize(command, false),
                        SimultriaViewerInitializationCommand.DevelopmentTransport,
                        SimultriaViewerInitializationCommand.DevelopmentRemoteEndpoint,
                        CancellationToken.None);

                    Assert.That(outcome.Result.Succeeded, Is.True);
                    Assert.That(application.Payload.ProjectId, Is.EqualTo(8));
                    Assert.That(application.Source,
                        Is.EqualTo(SimultriaViewerInitializationCommand.DevelopmentSource));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static SimultriaViewerInitializationPayload CreatePayload(long revision)
        {
            return new SimultriaViewerInitializationPayload
            {
                Revision = revision,
                EnvironmentId = "development",
                ProjectId = 8,
                ModelId = 13,
                Placement = new SimultriaViewerPlacementAlignment(),
                Metadata = null
            };
        }

        private sealed class RecordingApplication
        {
            public SimultriaViewerInitializationPayload Payload { get; set; }
            public string Source { get; set; }
        }

    }
}
