using System;
using System.Collections.Generic;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection
{
    /// <summary>Credential-free, optional mapping from build metadata to a canonical Simultria environment.</summary>
    [CreateAssetMenu(menuName = "Deucarian/Viewer/Simultria Build Environment Routing Policy", fileName = "SimultriaBuildEnvironmentRoutingPolicy")]
    public sealed class SimultriaBuildEnvironmentRoutingPolicy : ScriptableObject
    {
        [Serializable]
        public sealed class Rule
        {
            [SerializeField] private string buildMetadata;
            [SerializeField] private ApiEnvironmentId environmentId;
            public string BuildMetadata { get => buildMetadata ?? string.Empty; set => buildMetadata = value ?? string.Empty; }
            public ApiEnvironmentId EnvironmentId { get => environmentId; set => environmentId = value; }
        }

        [SerializeField] private List<Rule> rules = new List<Rule>();
        public IReadOnlyList<Rule> Rules => rules;

        public bool TryResolve(string buildMetadata, out ApiEnvironmentId environmentId, out string error)
        {
            environmentId = default;
            if (string.IsNullOrWhiteSpace(buildMetadata))
            {
                error = "Automatic environment routing requires build metadata.";
                return false;
            }

            Rule match = null;
            foreach (Rule rule in rules)
            {
                if (rule == null || !string.Equals(rule.BuildMetadata.Trim(), buildMetadata.Trim(), StringComparison.Ordinal)) continue;
                if (match != null)
                {
                    error = "Build metadata matches more than one Simultria environment rule.";
                    return false;
                }
                match = rule;
            }

            if (match == null)
            {
                error = "No Simultria environment rule matches build metadata '" + buildMetadata.Trim() + "'.";
                return false;
            }

            foreach (SimultriaEnvironmentDescriptor descriptor in SimultriaEnvironmentDescriptors.Standard)
            {
                if (descriptor.EnvironmentId == match.EnvironmentId)
                {
                    environmentId = match.EnvironmentId;
                    error = null;
                    return true;
                }
            }

            error = "Automatic routing requires a canonical Simultria environment ID.";
            return false;
        }
    }
}
