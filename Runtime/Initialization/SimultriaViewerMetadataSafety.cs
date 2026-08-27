using System;
using Newtonsoft.Json.Linq;

namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>Rejects secret-bearing development metadata before dispatch/export.</summary>
    public static class SimultriaViewerMetadataSafety
    {
        private static readonly string[] SensitiveFragments =
        {
            "token",
            "password",
            "secret",
            "authorization",
            "cookie",
            "api_key",
            "apikey",
            "credential"
        };

        public static bool IsSafe(JToken metadata, out string error)
        {
            if (metadata == null || metadata.Type == JTokenType.Null)
            {
                error = null;
                return true;
            }

            if (metadata is JObject objectValue)
            {
                foreach (JProperty property in objectValue.Properties())
                {
                    if (IsSensitiveName(property.Name))
                    {
                        error = "Metadata contains a secret-like field and cannot be dispatched or exported.";
                        return false;
                    }

                    if (!IsSafe(property.Value, out error))
                    {
                        return false;
                    }
                }
            }
            else if (metadata is JArray arrayValue)
            {
                foreach (JToken item in arrayValue)
                {
                    if (!IsSafe(item, out error))
                    {
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        private static bool IsSensitiveName(string name)
        {
            string normalized = (name ?? string.Empty).Trim().ToLowerInvariant();
            for (int i = 0; i < SensitiveFragments.Length; i++)
            {
                if (normalized.Contains(SensitiveFragments[i]))
                {
                    return true;
                }
            }

            return false;
        }

    }
}
