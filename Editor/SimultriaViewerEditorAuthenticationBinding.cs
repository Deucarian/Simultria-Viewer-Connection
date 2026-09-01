using Deucarian.API.Configuration;
using Deucarian.API.Models;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    internal static class SimultriaViewerEditorAuthenticationBinding
    {
        internal static string Create(
            SimultriaViewerEditorAuthenticationConfiguration configuration)
        {
            return configuration == null
                ? string.Empty
                : Create(
                    configuration.ProfileReference,
                    configuration.EnvironmentId);
        }

        internal static string Create(
            ScriptableObject profile,
            ApiEnvironmentId environmentId)
        {
            if (!(profile is ApiConnectionSettings settings) ||
                environmentId.IsEmpty ||
                !SimultriaViewerConnectionCompositionFingerprint.TryCreate(
                    settings,
                    environmentId,
                    out string fingerprint))
            {
                return string.Empty;
            }

            return Create(profile, environmentId, fingerprint);
        }

        internal static string Create(
            ScriptableObject profile,
            ApiEnvironmentId environmentId,
            string compositionFingerprint)
        {
            if (profile == null || environmentId.IsEmpty ||
                string.IsNullOrWhiteSpace(compositionFingerprint))
            {
                return string.Empty;
            }

            string assetPath = AssetDatabase.GetAssetPath(profile);
            string assetGuid = string.IsNullOrWhiteSpace(assetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(assetPath);
            return string.IsNullOrWhiteSpace(assetGuid)
                ? string.Empty
                : assetGuid + "|" + environmentId.Value + "|sha256:" +
                  compositionFingerprint.Trim();
        }
    }
}
