using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection
{
    /// <summary>Backend-neutral command payload for a Simultria viewer context.</summary>
    [Serializable]
    public sealed class SimultriaViewerInitializationPayload
    {
        [JsonProperty("revision")]
        public long Revision { get; set; }

        [JsonProperty("environment_id", NullValueHandling = NullValueHandling.Ignore)]
        public string EnvironmentId { get; set; }

        [JsonProperty("project_id")]
        public int ProjectId { get; set; }

        [JsonProperty("model_id")]
        public int ModelId { get; set; }

        [JsonProperty("model_version_id", NullValueHandling = NullValueHandling.Ignore)]
        public int? ModelVersionId { get; set; }

        /// <summary>
        /// Optional live-only generic viewer source resolved through Simultria
        /// API. Credential-free previews and exports leave this empty.
        /// </summary>
        [JsonProperty("model_url", NullValueHandling = NullValueHandling.Ignore)]
        public string ModelUrl { get; set; }

        [JsonProperty("model_version", NullValueHandling = NullValueHandling.Ignore)]
        public string ModelVersion { get; set; }

        [JsonProperty("placement", NullValueHandling = NullValueHandling.Ignore)]
        public SimultriaViewerPlacementAlignment Placement { get; set; }

        [JsonProperty("force_show_loaded_model_objects")]
        public bool ForceShowLoadedModelObjects { get; set; }

        [JsonProperty("metadata", NullValueHandling = NullValueHandling.Ignore)]
        public JToken Metadata { get; set; }

        public bool IsValid(out string error)
        {
            if (Revision <= 0)
            {
                error = "Revision must be positive.";
                return false;
            }

            if (ProjectId <= 0)
            {
                error = "Project ID must be positive.";
                return false;
            }

            if (ModelId <= 0)
            {
                error = "Model ID must be positive.";
                return false;
            }

            if (ModelVersionId.HasValue && ModelVersionId.Value <= 0)
            {
                error = "Model version ID must be positive when provided.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(ModelUrl) &&
                (!Uri.TryCreate(ModelUrl.Trim(), UriKind.Absolute, out Uri modelUri) ||
                 (modelUri.Scheme != Uri.UriSchemeHttp &&
                  modelUri.Scheme != Uri.UriSchemeHttps) ||
                 !string.IsNullOrEmpty(modelUri.UserInfo) ||
                 ContainsBearerQuery(modelUri.Query)))
            {
                error =
                    "Model URL must be an absolute credential-free HTTP(S) URL.";
                return false;
            }

            if (Placement != null && !Placement.IsFinite())
            {
                error = "Placement alignment must contain finite values.";
                return false;
            }

            if (!SimultriaViewerMetadataSafety.IsSafe(Metadata, out error))
            {
                return false;
            }

            error = null;
            return true;
        }

        private static bool ContainsBearerQuery(string query)
        {
            string value = query ?? string.Empty;
            return value.IndexOf(
                       "access_token=",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf(
                       "bearer=",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    [Serializable]
    public sealed class SimultriaViewerPlacementAlignment
    {
        public SimultriaViewerPlacementAlignment()
            : this(
                new SimultriaViewerVector3(),
                new SimultriaViewerVector3(),
                new SimultriaViewerVector3(1f, 1f, 1f))
        {
        }

        public SimultriaViewerPlacementAlignment(
            SimultriaViewerVector3 position,
            SimultriaViewerVector3 rotationEuler,
            SimultriaViewerVector3 scale)
        {
            Position = position ?? new SimultriaViewerVector3();
            RotationEuler = rotationEuler ?? new SimultriaViewerVector3();
            Scale = scale ?? new SimultriaViewerVector3(1f, 1f, 1f);
        }

        [JsonProperty("position")]
        public SimultriaViewerVector3 Position { get; set; }

        [JsonProperty("rotation_euler")]
        public SimultriaViewerVector3 RotationEuler { get; set; }

        [JsonProperty("scale")]
        public SimultriaViewerVector3 Scale { get; set; }

        internal bool IsFinite()
        {
            return Position != null && Position.IsFinite() &&
                   RotationEuler != null && RotationEuler.IsFinite() &&
                   Scale != null && Scale.IsFinite();
        }
    }

    [Serializable]
    public sealed class SimultriaViewerVector3
    {
        public SimultriaViewerVector3()
        {
        }

        public SimultriaViewerVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        [JsonProperty("x")]
        public float X { get; set; }

        [JsonProperty("y")]
        public float Y { get; set; }

        [JsonProperty("z")]
        public float Z { get; set; }

        internal bool IsFinite()
        {
            return IsFinite(X) && IsFinite(Y) && IsFinite(Z);
        }

        internal static SimultriaViewerVector3 From(Vector3 value)
        {
            return new SimultriaViewerVector3(value.x, value.y, value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
