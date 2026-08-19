using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection.Editor
{
    [FilePath(
        SettingsPath,
        FilePathAttribute.Location.ProjectFolder)]
    public sealed class SimultriaViewerConnectionProjectSettings :
        ScriptableSingleton<SimultriaViewerConnectionProjectSettings>
    {
        public const string SettingsPath =
            "ProjectSettings/DeucarianSimultriaViewerConnection.asset";

        [SerializeField] private string defaultProfileGuid = string.Empty;
        [SerializeField] private bool autoLoadInPlayMode = true;

        public SimultriaViewerDevelopmentProfile DefaultProfile
        {
            get => LoadProfile(defaultProfileGuid);
            set
            {
                defaultProfileGuid = ToGuid(value);
                Save(true);
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

        internal static SimultriaViewerDevelopmentProfile LoadProfile(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid.Trim());
            return string.IsNullOrWhiteSpace(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<SimultriaViewerDevelopmentProfile>(path);
        }

        private static string ToGuid(SimultriaViewerDevelopmentProfile profile)
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
