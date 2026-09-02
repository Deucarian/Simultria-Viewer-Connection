using System;
using System.IO;
using Deucarian.API.Models;
using Deucarian.CommandRouting;
using Newtonsoft.Json.Linq;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    internal static class SimultriaViewerBuildContextValidator
    {
        private const long MaximumContextBytes = 1024L * 1024L;
        private static readonly string[] CredentialAssignments =
        {
            "token=",
            "token:",
            "access_token=",
            "access_token:",
            "bearer=",
            "bearer:",
            "password=",
            "password:",
            "secret=",
            "secret:",
            "authorization=",
            "authorization:",
            "cookie=",
            "cookie:",
            "api_key=",
            "api_key:",
            "api-key=",
            "api-key:",
            "apikey=",
            "apikey:",
            "credential=",
            "credential:"
        };

        internal static bool TryValidateFile(
            string fullPath,
            out string issue)
        {
            issue = string.Empty;
            try
            {
                var file = new FileInfo(fullPath);
                if (!file.Exists || file.Length <= 0 ||
                    file.Length > MaximumContextBytes)
                {
                    issue = "The development context file is missing or invalid.";
                    return false;
                }

                return TryValidateJson(File.ReadAllText(fullPath), out issue);
            }
            catch (Exception)
            {
                issue = "The development context file could not be validated.";
                return false;
            }
        }

        internal static bool TryValidateJson(
            string json,
            out string issue)
        {
            issue = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                issue = "The development context is empty.";
                return false;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception)
            {
                issue = "The development context is not valid JSON.";
                return false;
            }

            if (!SimultriaViewerMetadataSafety.IsSafe(root, out _) ||
                ContainsCredentialLikeValue(root))
            {
                issue = "The development context contains credential-like data.";
                return false;
            }

            var codec = new JsonCommandProtocolCodec();
            if (!codec.TryDecode(
                    json,
                    out CommandEnvelope command,
                    out CommandResult _))
            {
                issue = "The development context command is malformed.";
                return false;
            }

            if (command.ProtocolVersion != 1 ||
                string.IsNullOrWhiteSpace(command.CommandId) ||
                !string.Equals(
                    command.CommandName,
                    SimultriaViewerInitializationCommand.CommandName,
                    StringComparison.Ordinal) ||
                !HasExpectedMetadata(command.Metadata))
            {
                issue = "The development context is not a canonical " +
                        "Simultria viewer command.";
                return false;
            }

            if (!command.TryReadPayload(
                    out SimultriaViewerInitializationPayload payload,
                    out _) ||
                payload == null ||
                !payload.IsValid(out _) ||
                !ApiEnvironmentId.TryParse(
                    payload.EnvironmentId,
                    out ApiEnvironmentId _) ||
                !string.IsNullOrWhiteSpace(payload.ModelUrl) ||
                !string.IsNullOrWhiteSpace(payload.ModelVersion))
            {
                issue = "The development context payload is invalid or unsafe.";
                return false;
            }

            return true;
        }

        private static bool HasExpectedMetadata(CommandMetadata metadata)
        {
            return metadata != null &&
                   string.Equals(
                       metadata.Source,
                       SimultriaViewerInitializationCommand.DevelopmentSource,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       metadata.Transport,
                       SimultriaViewerInitializationCommand
                           .DevelopmentTransport,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       metadata.RemoteEndpoint,
                       SimultriaViewerInitializationCommand
                           .DevelopmentRemoteEndpoint,
                       StringComparison.Ordinal);
        }

        private static bool ContainsCredentialLikeValue(JToken token)
        {
            if (token is JContainer container)
            {
                foreach (JToken child in container.Children())
                {
                    if (ContainsCredentialLikeValue(child))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (token.Type != JTokenType.String)
            {
                return false;
            }

            string value = token.Value<string>()?.Trim() ?? string.Empty;
            if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) ||
                ContainsCredentialAssignment(value))
            {
                return true;
            }

            return Uri.TryCreate(value, UriKind.Absolute, out Uri uri) &&
                   !string.IsNullOrEmpty(uri.UserInfo);
        }

        private static bool ContainsCredentialAssignment(string value)
        {
            for (int index = 0;
                 index < CredentialAssignments.Length;
                 index++)
            {
                if (value.IndexOf(
                        CredentialAssignments[index],
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
