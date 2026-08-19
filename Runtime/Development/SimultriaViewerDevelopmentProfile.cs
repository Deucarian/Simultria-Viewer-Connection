using System;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection
{
    /// <summary>
    /// Credential-free development context for a Simultria-backed viewer.
    /// Environment URL resolution remains in Simultria API.
    /// </summary>
    [CreateAssetMenu(
        menuName = "Deucarian/Viewer/Simultria Development Profile",
        fileName = "SimultriaViewerDevelopmentProfile")]
    public sealed class SimultriaViewerDevelopmentProfile : ScriptableObject
    {
        [SerializeField] private ApiEnvironmentId environmentId =
            SimultriaEnvironmentIds.Development;
        [SerializeField] private SimultriaApiProfile apiProfileReference;
        [SerializeField] private int projectId;
        [SerializeField] private int modelId;
        [SerializeField] private int modelVersionId;
        [SerializeField] private Vector3 placementPosition;
        [SerializeField] private Vector3 placementRotationEuler;
        [SerializeField] private Vector3 placementScale = Vector3.one;
        [SerializeField] private bool forceShowLoadedModelObjects = true;
        [SerializeField, TextArea(3, 10)] private string metadataJson = string.Empty;

        /// <summary>Optional stable environment identifier.</summary>
        public ApiEnvironmentId EnvironmentId
        {
            get => environmentId.IsEmpty
                ? SimultriaEnvironmentIds.Development
                : environmentId;
            set => environmentId = value;
        }

        /// <summary>
        /// Optional Simultria API composition asset. The running viewer may
        /// provide its own composition when this reference is absent.
        /// </summary>
        public SimultriaApiProfile ApiProfileReference
        {
            get => apiProfileReference;
            set => apiProfileReference = value;
        }

        /// <summary>
        /// Explicit profile when assigned; otherwise the credential-free
        /// Simultria API package default.
        /// </summary>
        public SimultriaApiProfile EffectiveApiProfile =>
            apiProfileReference ?? SimultriaApiProfileDefaults.Load();

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
            SimultriaApiProfile profile = EffectiveApiProfile;
            if (profile == null)
            {
                error =
                    "The package-provided Simultria API profile is missing.";
                return false;
            }

            return profile.TryGetEnvironmentStatus(
                EnvironmentId,
                out status,
                out error);
        }

        /// <summary>Creates the safe typed initialization payload.</summary>
        public bool TryCreatePayload(
            long revision,
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

            if (!TryParseMetadata(out JToken metadata, out error))
            {
                return false;
            }

            payload = new SimultriaViewerInitializationPayload
            {
                Revision = revision,
                EnvironmentId = EnvironmentId.Value,
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
