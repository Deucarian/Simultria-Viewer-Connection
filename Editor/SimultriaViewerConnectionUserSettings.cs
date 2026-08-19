using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection.Editor
{
    [FilePath(
        SettingsPath,
        FilePathAttribute.Location.ProjectFolder)]
    public sealed class SimultriaViewerConnectionUserSettings :
        ScriptableSingleton<SimultriaViewerConnectionUserSettings>
    {
        public const string SettingsPath =
            "UserSettings/DeucarianSimultriaViewerConnection.asset";

        [SerializeField] private bool useLocalProfileOverride;
        [SerializeField] private string localProfileGuid = string.Empty;

        public bool UseLocalProfileOverride
        {
            get => useLocalProfileOverride;
            set
            {
                if (useLocalProfileOverride == value)
                {
                    return;
                }

                useLocalProfileOverride = value;
                Save(true);
                SimultriaViewerEditorAuthenticationHost.RequestRefresh();
            }
        }

        public SimultriaViewerDevelopmentProfile LocalProfile
        {
            get => SimultriaViewerConnectionProjectSettings.LoadProfile(
                localProfileGuid);
            set
            {
                string path = value == null
                    ? string.Empty
                    : AssetDatabase.GetAssetPath(value);
                string nextGuid = string.IsNullOrWhiteSpace(path)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(path);
                if (string.Equals(
                        localProfileGuid,
                        nextGuid,
                        System.StringComparison.Ordinal))
                {
                    return;
                }

                localProfileGuid = nextGuid;
                Save(true);
                SimultriaViewerEditorAuthenticationHost.RequestRefresh();
            }
        }
    }
}
