using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;

namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>
    /// Produces a credential-free identity for one resolved API backend.
    /// The digest is safe to retain; raw hosts, routes, headers, and tokens are
    /// never exposed by the result.
    /// </summary>
    internal static class SimultriaViewerConnectionCompositionFingerprint
    {
        internal static bool TryCreate(
            ApiConnectionSettings settings,
            ApiEnvironmentId environmentId,
            out string fingerprint)
        {
            fingerprint = null;
            return settings != null &&
                   SimultriaApiConnectionSettingsAdapter.TryCreateComposition(
                       settings,
                       out ApiComposition composition,
                       out _) &&
                   TryCreate(
                       settings,
                       composition,
                       environmentId,
                       out fingerprint);
        }

        internal static bool TryCreate(
            ApiConnectionSettings settings,
            ApiComposition composition,
            ApiEnvironmentId environmentId,
            out string fingerprint)
        {
            fingerprint = null;
            ApiServiceDefinition service = settings?.ServiceDefinition;
            if (composition == null || environmentId.IsEmpty ||
                service == null ||
                composition.GetEnvironmentStatus(environmentId)?.IsResolved !=
                    true)
            {
                return false;
            }

            var canonical = new StringBuilder();
            AppendValue(canonical, "simultria-viewer-backend-v1");
            AppendValue(canonical, environmentId.Value);
            AppendValue(canonical, service.ServiceId);
            AppendValue(canonical, service.SourceVersion);
            AppendValue(canonical, service.SourceFingerprint);
            AppendValue(canonical, composition.CatalogId.Value);

            ApiEnvironmentProfile environment = FindEnvironment(
                settings,
                environmentId);
            if (environment == null ||
                !AppendClients(
                    canonical,
                    composition,
                    environmentId,
                    environment) ||
                !AppendEndpoints(
                    canonical,
                    composition,
                    environmentId,
                    service.EndpointCatalog))
            {
                return false;
            }

            byte[] input = Encoding.UTF8.GetBytes(canonical.ToString());
            byte[] digest;
            using (SHA256 sha256 = SHA256.Create())
            {
                digest = sha256.ComputeHash(input);
            }

            var encoded = new StringBuilder(digest.Length * 2);
            for (int i = 0; i < digest.Length; i++)
            {
                encoded.Append(digest[i].ToString(
                    "x2",
                    CultureInfo.InvariantCulture));
            }

            fingerprint = encoded.ToString();
            return true;
        }

        private static ApiEnvironmentProfile FindEnvironment(
            ApiConnectionSettings settings,
            ApiEnvironmentId environmentId)
        {
            IReadOnlyList<ApiEnvironmentProfile> environments =
                settings.Environments;
            for (int i = 0; i < environments.Count; i++)
            {
                ApiEnvironmentProfile candidate = environments[i];
                if (candidate != null &&
                    candidate.TryGetId(out ApiEnvironmentId candidateId) &&
                    candidateId == environmentId)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool AppendClients(
            StringBuilder canonical,
            ApiComposition composition,
            ApiEnvironmentId environmentId,
            ApiEnvironmentProfile environment)
        {
            var clients = new List<ApiNamedClientDefinition>(
                environment.Clients);
            clients.Sort((left, right) => string.Compare(
                left?.ClientId,
                right?.ClientId,
                StringComparison.Ordinal));
            AppendValue(canonical, clients.Count.ToString(
                CultureInfo.InvariantCulture));
            for (int i = 0; i < clients.Count; i++)
            {
                ApiNamedClientDefinition definition = clients[i];
                if (definition == null ||
                    !ApiClientId.TryParse(
                        definition.ClientId,
                        out ApiClientId clientId) ||
                    !composition.TryResolveClient(
                        environmentId,
                        clientId,
                        out ApiResolvedClient client,
                        out _))
                {
                    return false;
                }

                AppendValue(canonical, client.ClientId.Value);
                AppendValue(canonical, client.BaseUrl);
                AppendPairs(canonical, client.DefaultHeaders);
                AppendPolicy(canonical, client.RequestPolicy);
            }

            return true;
        }

        private static bool AppendEndpoints(
            StringBuilder canonical,
            ApiComposition composition,
            ApiEnvironmentId environmentId,
            ApiEndpointCatalog catalog)
        {
            if (catalog == null)
            {
                return false;
            }

            var entries = new List<ApiEndpointCatalogEntry>(catalog.Endpoints);
            entries.Sort((left, right) => string.Compare(
                left?.EndpointId,
                right?.EndpointId,
                StringComparison.Ordinal));
            AppendValue(canonical, entries.Count.ToString(
                CultureInfo.InvariantCulture));
            for (int i = 0; i < entries.Count; i++)
            {
                ApiEndpointCatalogEntry definition = entries[i];
                if (definition == null ||
                    !ApiEndpointId.TryParse(
                        definition.EndpointId,
                        out ApiEndpointId endpointId) ||
                    !composition.TryResolveEndpoint(
                        environmentId,
                        endpointId,
                        out ApiResolvedEndpoint endpoint,
                        out _))
                {
                    return false;
                }

                AppendValue(canonical, endpoint.EndpointId.Value);
                AppendValue(canonical, endpoint.Client.ClientId.Value);
                AppendValue(canonical, endpoint.Endpoint.Path);
                AppendValue(canonical, ((int)endpoint.Endpoint.Method).ToString(
                    CultureInfo.InvariantCulture));
                AppendValue(
                    canonical,
                    ((int)endpoint.Endpoint.Authentication).ToString(
                        CultureInfo.InvariantCulture));
                AppendValue(
                    canonical,
                    ((int)endpoint.Endpoint.ResponseFormat).ToString(
                        CultureInfo.InvariantCulture));
                AppendValue(
                    canonical,
                    endpoint.Endpoint.SuppressLogging ? "1" : "0");
                AppendPairs(canonical, endpoint.Endpoint.DefaultHeaders);
                AppendPairs(
                    canonical,
                    endpoint.Endpoint.DefaultQueryParameters);
                AppendPolicy(canonical, endpoint.RequestPolicy);
            }

            return true;
        }

        private static void AppendPairs(
            StringBuilder canonical,
            IReadOnlyDictionary<string, string> pairs)
        {
            var keys = new List<string>(pairs?.Keys ?? Array.Empty<string>());
            keys.Sort(StringComparer.Ordinal);
            AppendValue(canonical, keys.Count.ToString(
                CultureInfo.InvariantCulture));
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                AppendValue(canonical, key);
                AppendValue(canonical, pairs[key]);
            }
        }

        private static void AppendPolicy(
            StringBuilder canonical,
            ApiRequestPolicy policy)
        {
            if (policy == null)
            {
                AppendValue(canonical, null);
                return;
            }

            AppendValue(canonical, policy.TimeoutSeconds.ToString(
                CultureInfo.InvariantCulture));
            AppendValue(canonical, policy.MaxRetryAttempts.ToString(
                CultureInfo.InvariantCulture));
            AppendValue(
                canonical,
                policy.InitialRetryBackoffMilliseconds.ToString(
                    CultureInfo.InvariantCulture));
            AppendValue(canonical, policy.RetryBackoffMultiplier.ToString(
                "R",
                CultureInfo.InvariantCulture));
            AppendValue(
                canonical,
                policy.MaximumRetryBackoffMilliseconds.ToString(
                    CultureInfo.InvariantCulture));
            AppendValue(canonical, policy.RateLimitRequestCountHint.ToString(
                CultureInfo.InvariantCulture));
            AppendValue(canonical, policy.RateLimitWindowSecondsHint.ToString(
                "R",
                CultureInfo.InvariantCulture));
        }

        private static void AppendValue(StringBuilder builder, string value)
        {
            string normalized = value ?? string.Empty;
            builder.Append(normalized.Length.ToString(
                CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(normalized);
        }
    }
}
