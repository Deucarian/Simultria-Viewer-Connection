using System;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>
    /// Credential-free player-build identity for the public Simultria Unity
    /// build directory. It deliberately contains no target-environment or
    /// build-version override.
    /// </summary>
    [CreateAssetMenu(
        menuName = "Deucarian/Build/Simultria Unity Build Configuration",
        fileName = "SimultriaViewerBuildConfiguration")]
    public sealed class SimultriaViewerBuildConfiguration : ScriptableObject
    {
        [Tooltip(
            "Project-owned API connection containing runtime backend " +
            "environments. Version lookup always uses central Production.")]
        [SerializeField] private ApiConnectionSettings connectionSettings;
        [Tooltip(
            "Canonical backend product identifier, for example " +
            "design_and_sales or holo_helmet.")]
        [SerializeField] private string product = string.Empty;

        public ApiConnectionSettings ConnectionSettings
        {
            get => connectionSettings;
            set => connectionSettings = value;
        }

        [Obsolete("The Unity build directory is fixed on central Production. " +
                  "This legacy selection is ignored.")]
        public ApiEnvironmentId BuildDirectoryEnvironmentId
        {
            get => SimultriaEnvironmentIds.Production;
            set { }
        }

        public string Product
        {
            get => product ?? string.Empty;
            set => product = value ?? string.Empty;
        }

        public bool TryCreateComposition(
            out ApiComposition composition,
            out string error)
        {
            if (connectionSettings == null)
            {
                composition = null;
                error = "Assign API connection settings to the Simultria " +
                        "viewer build configuration.";
                return false;
            }

            return SimultriaApiConnectionSettingsAdapter.TryCreateComposition(
                connectionSettings,
                out composition,
                out error);
        }
    }
}
