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
            "Project-owned API connection containing the public Unity build " +
            "directory and all environments that the directory may return.")]
        [SerializeField] private ApiConnectionSettings connectionSettings;
        [Tooltip(
            "Configured API environment that hosts the public Unity build " +
            "directory. This is the directory location, not the build's " +
            "assigned target environment.")]
        [SerializeField] private ApiEnvironmentId buildDirectoryEnvironmentId;
        [Tooltip(
            "Canonical backend product identifier, for example " +
            "design_and_sales or holo_helmet.")]
        [SerializeField] private string product = string.Empty;

        public ApiConnectionSettings ConnectionSettings
        {
            get => connectionSettings;
            set => connectionSettings = value;
        }

        public ApiEnvironmentId BuildDirectoryEnvironmentId
        {
            get => buildDirectoryEnvironmentId;
            set => buildDirectoryEnvironmentId = value;
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
