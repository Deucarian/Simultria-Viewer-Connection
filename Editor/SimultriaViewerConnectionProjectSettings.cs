using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    [FilePath(
        SettingsPath,
        FilePathAttribute.Location.ProjectFolder)]
    public sealed class SimultriaViewerConnectionProjectSettings :
        ScriptableSingleton<SimultriaViewerConnectionProjectSettings>
    {
        public const string SettingsPath =
            "ProjectSettings/DeucarianSimultriaViewerConnection.asset";
        internal const bool DefaultAutoLoadInPlayMode = false;

        [SerializeField] private string defaultProfileGuid = string.Empty;
        [SerializeField] private bool autoLoadInPlayMode =
            DefaultAutoLoadInPlayMode;

        public SimultriaViewerDevelopmentContext DefaultProfile
        {
            get => LoadProfile(defaultProfileGuid);
            set
            {
                string nextGuid = ToGuid(value);
                if (string.Equals(
                        defaultProfileGuid,
                        nextGuid,
                        System.StringComparison.Ordinal))
                {
                    return;
                }

                defaultProfileGuid = nextGuid;
                Save(true);
                SimultriaViewerEditorAuthenticationHost.RequestRefresh();
            }
        }

        public bool AutoLoadInPlayMode
        {
            get => autoLoadInPlayMode;
            set
            {
                if (autoLoadInPlayMode == value)
                {
                    return;
                }

                autoLoadInPlayMode = value;
                Save(true);
            }
        }

        internal static SimultriaViewerDevelopmentContext LoadProfile(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid.Trim());
            return string.IsNullOrWhiteSpace(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<SimultriaViewerDevelopmentContext>(path);
        }

        private static string ToGuid(SimultriaViewerDevelopmentContext profile)
        {
            if (profile == null)
            {
                return string.Empty;
            }

            string path = AssetDatabase.GetAssetPath(profile);
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(path);
        }

    }
}
