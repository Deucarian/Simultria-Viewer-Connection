using System;
using Deucarian.CommandRouting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Deucarian.SimultriaViewerConnection
{
    /// <summary>Creates the canonical Command Routing initialization envelope.</summary>
    public static class SimultriaViewerInitializationCommand
    {
        public const string CommandName = "initialize_viewer";
        public const string DevelopmentSource = "simultria-development-profile";
        public const string DevelopmentTransport = "editor-local";
        public const string DevelopmentRemoteEndpoint = "development-profile";

        public static CommandEnvelope Create(
            SimultriaViewerInitializationPayload payload,
            string commandId = null)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (!payload.IsValid(out string error))
            {
                throw new ArgumentException(error, nameof(payload));
            }

            return new CommandEnvelope(
                CommandName,
                JObject.FromObject(payload),
                string.IsNullOrWhiteSpace(commandId)
                    ? "simultria-development-" + payload.Revision
                    : commandId.Trim(),
                1,
                new CommandMetadata(
                    DevelopmentSource,
                    DevelopmentTransport,
                    DevelopmentRemoteEndpoint));
        }

        /// <summary>Serializes a canonical, credential-free command for preview/export.</summary>
        public static string Serialize(CommandEnvelope command, bool indented = true)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            var json = new JObject
            {
                ["protocol_version"] = command.ProtocolVersion,
                ["command_id"] = command.CommandId,
                ["command"] = command.CommandName,
                ["payload"] = command.Payload.DeepClone(),
                ["metadata"] = JObject.FromObject(command.Metadata)
            };
            return json.ToString(indented ? Formatting.Indented : Formatting.None);
        }
    }
}
