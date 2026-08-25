using System;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Models;
using Deucarian.Simultria.API.Services;

namespace Deucarian.SimultriaViewerConnection
{
    /// <summary>
    /// Credential-free resolved model source for a live viewer initialization.
    /// </summary>
    public sealed class SimultriaViewerModelInitializationResolution
    {
        private SimultriaViewerModelInitializationResolution(
            bool succeeded,
            int projectId,
            int modelId,
            int modelVersionId,
            string modelUrl,
            bool usedRequestedVersion,
            string errorCode,
            string message)
        {
            Succeeded = succeeded;
            ProjectId = projectId;
            ModelId = modelId;
            ModelVersionId = modelVersionId;
            ModelUrl = modelUrl;
            UsedRequestedVersion = usedRequestedVersion;
            ErrorCode = errorCode ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public bool Succeeded { get; }
        public int ProjectId { get; }
        public int ModelId { get; }
        public int ModelVersionId { get; }
        public string ModelUrl { get; }
        public bool UsedRequestedVersion { get; }
        public string ErrorCode { get; }
        public string Message { get; }

        internal static SimultriaViewerModelInitializationResolution Success(
            SimultriaViewerModelResolveResult resolved,
            string modelUrl) =>
            new SimultriaViewerModelInitializationResolution(
                true,
                resolved.ProjectId,
                resolved.ModelId,
                resolved.ModelVersionId,
                modelUrl,
                resolved.UsedRequestedVersion,
                string.Empty,
                resolved.Message);

        internal static SimultriaViewerModelInitializationResolution Failure(
            string code,
            string message) =>
            new SimultriaViewerModelInitializationResolution(
                false,
                0,
                0,
                0,
                string.Empty,
                false,
                code,
                message);
    }

    /// <summary>
    /// Resolves the canonical ID-only viewer payload through Simultria API.
    /// Host-provided model_url values never participate in this resolution.
    /// </summary>
    public sealed class SimultriaViewerModelInitializationResolver
    {
        private readonly Func<
            int,
            int,
            int?,
            CancellationToken,
            Task<SimultriaViewerModelResolveResult>> resolve;

        public SimultriaViewerModelInitializationResolver(
            IApiClient apiClient,
            ApiComposition composition,
            ApiEnvironmentId environmentId)
        {
            var resolver = new SimultriaViewerModelResolver(
                apiClient ?? throw new ArgumentNullException(nameof(apiClient)),
                composition ??
                    throw new ArgumentNullException(nameof(composition)),
                environmentId);
            resolve = resolver.ResolveAsync;
        }

        internal SimultriaViewerModelInitializationResolver(
            Func<
                int,
                int,
                int?,
                CancellationToken,
                Task<SimultriaViewerModelResolveResult>> resolver)
        {
            resolve = resolver ??
                throw new ArgumentNullException(nameof(resolver));
        }

        public async Task<SimultriaViewerModelInitializationResolution>
            ResolveAsync(
                SimultriaViewerInitializationPayload payload,
                CancellationToken cancellationToken = default)
        {
            if (payload == null || !payload.IsValid(out string error))
            {
                return SimultriaViewerModelInitializationResolution.Failure(
                    "invalid_payload",
                    error ?? "The initialization payload is required.");
            }

            SimultriaViewerModelResolveResult resolved = await resolve(
                payload.ProjectId,
                payload.ModelId,
                payload.ModelVersionId,
                cancellationToken);
            if (resolved == null || !resolved.Succeeded)
            {
                return SimultriaViewerModelInitializationResolution.Failure(
                    resolved?.ErrorCode ?? "model_resolution_failed",
                    resolved?.Message ??
                    "The Simultria model source could not be resolved.");
            }

            if (!TryNormalizeModelUrl(
                    resolved.DownloadUrl,
                    out string modelUrl))
            {
                return SimultriaViewerModelInitializationResolution.Failure(
                    "unsafe_model_source",
                    "The resolved model source is not a credential-free " +
                    "absolute HTTP(S) URL.");
            }

            return SimultriaViewerModelInitializationResolution.Success(
                resolved,
                modelUrl);
        }

        private static bool TryNormalizeModelUrl(
            string value,
            out string normalized)
        {
            normalized = string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim();
            if (!Uri.TryCreate(
                    normalized,
                    UriKind.Absolute,
                    out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp &&
                 uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                normalized = string.Empty;
                return false;
            }

            string query = uri.Query ?? string.Empty;
            if (ContainsAny(
                    query,
                    "access_token=",
                    "bearer="))
            {
                normalized = string.Empty;
                return false;
            }

            return true;
        }

        private static bool ContainsAny(
            string value,
            params string[] candidates)
        {
            for (int index = 0; index < candidates.Length; index++)
            {
                if (value.IndexOf(
                        candidates[index],
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
