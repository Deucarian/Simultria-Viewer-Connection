using System;
using System.Collections.Generic;
using Deucarian.API.Core;
using Deucarian.API.Models;

namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>
    /// Resolves explicitly configured Simultria model-content hosts that may
    /// receive the live viewer session bearer.
    /// </summary>
    public static class SimultriaViewerAuthenticatedOriginResolver
    {
        public const string ModelContentClientIdValue =
            "simultria.model-content";

        public static ApiClientId ModelContentClientId =>
            new ApiClientId(ModelContentClientIdValue);

        /// <summary>
        /// Returns the exact model-content origin configured for an
        /// environment. The client is optional for same-origin deployments.
        /// </summary>
        public static IReadOnlyCollection<string> Resolve(
            ApiComposition composition,
            ApiEnvironmentId environmentId)
        {
            if (composition == null || environmentId.IsEmpty ||
                !composition.TryResolveClient(
                    environmentId,
                    ModelContentClientId,
                    out ApiResolvedClient client,
                    out _))
            {
                return Array.Empty<string>();
            }

            if (!Uri.TryCreate(
                    client.BaseUrl,
                    UriKind.Absolute,
                    out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp &&
                 uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidOperationException(
                    "The Simultria model-content client requires an absolute " +
                    "HTTP(S) base URL without user information.");
            }

            return new[]
            {
                uri.GetLeftPart(UriPartial.Authority).TrimEnd('/')
            };
        }
    }
}
