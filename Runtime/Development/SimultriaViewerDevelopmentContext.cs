using System;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

#if UNITY_EDITOR
namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>
    /// Credential-free development context for a Simultria-backed viewer.
    /// Environment URL resolution remains in Simultria API.
    /// </summary>
    [CreateAssetMenu(
        menuName = "Deucarian/Viewer/Simultria Development Context",
        fileName = "SimultriaViewerDevelopmentContext")]
    public sealed class SimultriaViewerDevelopmentContext : ScriptableObject
    {
        [SerializeField] private ApiEnvironmentId environmentId =
            SimultriaEnvironmentIds.Development;
        [Tooltip(
            "Manual uses the selected environment. Automatic asks the " +
            "Simultria Unity build directory which environment owns this build.")]
        [SerializeField] private SimultriaViewerEnvironmentResolutionMode
            environmentResolutionMode;
        [Tooltip(
            "API environment whose configured host exposes the public Unity " +
            "build directory. Required for automatic resolution; no " +
            "Production fallback is assumed.")]
        [SerializeField] private ApiEnvironmentId buildDirectoryEnvironmentId;
        [Tooltip(
            "Product identifier used by the Simultria Unity build directory, " +
            "for example a portal-defined viewer product key.")]
        [SerializeField] private string buildProduct = string.Empty;
        [Tooltip(
            "Optional local/editor override. Leave blank to use " +
            "Application.version at runtime.")]
        [SerializeField] private string buildVersionOverride = string.Empty;
        [Tooltip(
            "Project-owned generic API connection. Hosts remain editable " +
            "in that asset and are never stored in this development context.")]
        [SerializeField] private ApiConnectionSettings
            apiConnectionSettingsReference;
        [SerializeField] private int projectId;
        [SerializeField] private int modelId;
        [Tooltip(
            "Use 0 to load the model's active version. Use a positive ID " +
            "to pin that exact model version.")]
        [SerializeField] private int modelVersionId;
        [SerializeField] private Vector3 placementPosition;
        [SerializeField] private Vector3 placementRotationEuler;
        [SerializeField] private Vector3 placementScale = Vector3.one;
        [SerializeField] private bool forceShowLoadedModelObjects = true;
        [SerializeField, TextArea(3, 10)] private string metadataJson = string.Empty;

        /// <summary>
        /// Explicit stable environment identifier. Legacy Manual assets that
        /// serialized no value keep their historical Development behavior.
        /// </summary>
        public ApiEnvironmentId EnvironmentId
        {
            get => environmentResolutionMode ==
                       SimultriaViewerEnvironmentResolutionMode.Manual &&
                   environmentId.IsEmpty
                ? SimultriaEnvironmentIds.Development
                : environmentId;
            set => environmentId = value;
        }

        public SimultriaViewerEnvironmentResolutionMode
            EnvironmentResolutionMode
        {
            get => environmentResolutionMode;
            set => environmentResolutionMode = value;
        }

        public ApiEnvironmentId BuildDirectoryEnvironmentId
        {
            get => buildDirectoryEnvironmentId;
            set => buildDirectoryEnvironmentId = value;
        }

        public string BuildProduct
        {
            get => buildProduct ?? string.Empty;
            set => buildProduct = value ?? string.Empty;
        }

        public string BuildVersionOverride
        {
            get => buildVersionOverride ?? string.Empty;
            set => buildVersionOverride = value ?? string.Empty;
        }

        /// <summary>Project-owned generic API connection settings.</summary>
        public ApiConnectionSettings ConnectionSettingsReference
        {
            get => apiConnectionSettingsReference;
            set => apiConnectionSettingsReference = value;
        }

        /// <summary>The exact settings asset selected for identity checks.</summary>
        public ScriptableObject EffectiveProfileReference =>
            apiConnectionSettingsReference;

        public int ProjectId
        {
            get => projectId;
            set => projectId = value;
        }

        public int ModelId
        {
            get => modelId;
            set => modelId = value;
        }

        /// <summary>
        /// Zero selects the model's active version. A positive value pins the
        /// exact version with that ID.
        /// </summary>
        public int ModelVersionId
        {
            get => modelVersionId;
            set => modelVersionId = value;
        }

        public Vector3 PlacementPosition
        {
            get => placementPosition;
            set => placementPosition = value;
        }

        public Vector3 PlacementRotationEuler
        {
            get => placementRotationEuler;
            set => placementRotationEuler = value;
        }

        public Vector3 PlacementScale
        {
            get => placementScale;
            set => placementScale = value;
        }

        public bool ForceShowLoadedModelObjects
        {
            get => forceShowLoadedModelObjects;
            set => forceShowLoadedModelObjects = value;
        }

        public string MetadataJson
        {
            get => metadataJson ?? string.Empty;
            set => metadataJson = value ?? string.Empty;
        }

        /// <summary>Resolves sanitized environment status through Simultria API.</summary>
        public bool TryResolveEnvironment(
            out ApiEnvironmentStatus status,
            out string error)
        {
            status = null;
            if (environmentResolutionMode !=
                SimultriaViewerEnvironmentResolutionMode.Manual)
            {
                error = "Automatic environment resolution must complete " +
                        "through SimultriaViewerEnvironmentResolver before " +
                        "the effective environment can be used.";
                return false;
            }

            return TryResolveEnvironment(EnvironmentId, out status, out error);
        }

        public bool TryResolveEnvironment(
            ApiEnvironmentId effectiveEnvironmentId,
            out ApiEnvironmentStatus status,
            out string error)
        {
            status = null;
            if (!TryCreateComposition(
                    out ApiComposition composition,
                    out error))
            {
                return false;
            }

            status = composition.GetEnvironmentStatus(effectiveEnvironmentId);
            error = status.Message;
            return status.IsResolved;
        }

        /// <summary>
        /// Creates the selected Simultria-compatible composition.
        /// </summary>
        public bool TryCreateComposition(
            out ApiComposition composition,
            out string error)
        {
            if (apiConnectionSettingsReference == null)
            {
                composition = null;
                error =
                    "Assign API connection settings to the Simultria " +
                    "viewer development context.";
                return false;
            }

            return SimultriaApiConnectionSettingsAdapter.TryCreateComposition(
                apiConnectionSettingsReference,
                out composition,
                out error);
        }

        /// <summary>Creates the safe typed initialization payload.</summary>
        public bool TryCreatePayload(
            long revision,
            out SimultriaViewerInitializationPayload payload,
            out string error)
        {
            if (environmentResolutionMode !=
                SimultriaViewerEnvironmentResolutionMode.Manual)
            {
                payload = null;
                error = "Resolve the automatic environment before creating " +
                        "an initialization payload.";
                return false;
            }

            return TryCreatePayload(
                revision,
                EnvironmentId,
                out payload,
                out error);
        }

        public bool TryCreatePayload(
            long revision,
            ApiEnvironmentId effectiveEnvironmentId,
            out SimultriaViewerInitializationPayload payload,
            out string error)
        {
            payload = null;
            if (revision <= 0)
            {
                error = "Revision must be positive.";
                return false;
            }

            if (projectId <= 0)
            {
                error = "Project ID must be positive.";
                return false;
            }

            if (modelId <= 0)
            {
                error = "Model ID must be positive.";
                return false;
            }

            if (modelVersionId < 0)
            {
                error = "Model version ID must be zero for the active version " +
                        "or a positive exact version ID.";
                return false;
            }

            if (effectiveEnvironmentId.IsEmpty)
            {
                error = "An effective Simultria environment is required.";
                return false;
            }

            if (!TryParseMetadata(out JToken metadata, out error))
            {
                return false;
            }

            payload = new SimultriaViewerInitializationPayload
            {
                Revision = revision,
                EnvironmentId = effectiveEnvironmentId.Value,
                ProjectId = projectId,
                ModelId = modelId,
                ModelVersionId = modelVersionId > 0 ? (int?)modelVersionId : null,
                Placement = new SimultriaViewerPlacementAlignment(
                    SimultriaViewerVector3.From(placementPosition),
                    SimultriaViewerVector3.From(placementRotationEuler),
                    SimultriaViewerVector3.From(placementScale)),
                ForceShowLoadedModelObjects = forceShowLoadedModelObjects,
                Metadata = metadata
            };

            return payload.IsValid(out error);
        }

        private bool TryParseMetadata(out JToken metadata, out string error)
        {
            metadata = null;
            error = null;
            if (string.IsNullOrWhiteSpace(metadataJson))
            {
                return true;
            }

            try
            {
                metadata = JToken.Parse(metadataJson);
            }
            catch (JsonException)
            {
                error = "Metadata must contain valid JSON.";
                return false;
            }

            if (!SimultriaViewerMetadataSafety.IsSafe(metadata, out error))
            {
                metadata = null;
                return false;
            }

            return true;
        }

        private void OnValidate()
        {
            if (placementScale == Vector3.zero)
            {
                placementScale = Vector3.one;
            }
        }
    }
}
#endif
