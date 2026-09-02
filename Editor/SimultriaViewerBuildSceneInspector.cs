using System;
using System.Collections.Generic;
using Deucarian.API.Configuration;
using Deucarian.BuildPipeline;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    internal interface ISimultriaViewerBuildSceneInspector
    {
        bool TryInspect(
            DeucarianBuildRequest request,
            out SimultriaViewerBuildSceneSnapshot snapshot,
            out string issue);
    }

    internal sealed class SimultriaViewerBuildSceneSnapshot
    {
        internal SimultriaViewerBuildSceneSnapshot(
            string scenePath,
            int gateCount,
            SimultriaViewerBuildConfiguration configuration,
            IReadOnlyList<ApiConnectionSettings> sourceSettings,
            string inspectionIssue = null)
        {
            ScenePath = scenePath ?? string.Empty;
            GateCount = gateCount;
            Configuration = configuration;
            SourceSettings = sourceSettings ??
                Array.Empty<ApiConnectionSettings>();
            InspectionIssue = inspectionIssue ?? string.Empty;
        }

        internal string ScenePath { get; }
        internal int GateCount { get; }
        internal SimultriaViewerBuildConfiguration Configuration { get; }
        internal IReadOnlyList<ApiConnectionSettings> SourceSettings { get; }
        internal string InspectionIssue { get; }
        internal bool ContainsGate => GateCount > 0;
    }

    /// <summary>
    /// Inspects the one scene selected by a Build Profile in a preview scene,
    /// without changing, saving, or dirtying the user's normal scene set.
    /// </summary>
    internal sealed class SimultriaViewerBuildSceneInspector :
        ISimultriaViewerBuildSceneInspector
    {
        public bool TryInspect(
            DeucarianBuildRequest request,
            out SimultriaViewerBuildSceneSnapshot snapshot,
            out string issue)
        {
            snapshot = null;
            if (!TryGetEnabledScenePaths(
                    request?.BuildProfile?.scenes,
                    out IReadOnlyList<string> scenePaths,
                    out string selectionIssue))
            {
                issue = selectionIssue;
                return false;
            }

            int gateCount = 0;
            SimultriaViewerBuildConfiguration configuration = null;
            var sourceSettings = new List<ApiConnectionSettings>();
            string inspectionIssue = selectionIssue;
            for (int sceneIndex = 0;
                 sceneIndex < scenePaths.Count;
                 sceneIndex++)
            {
                string scenePath = scenePaths[sceneIndex];
                if (!TryInspectScene(
                        scenePath,
                        ref gateCount,
                        ref configuration,
                        sourceSettings,
                        out string sceneIssue) &&
                    string.IsNullOrWhiteSpace(inspectionIssue))
                {
                    inspectionIssue = sceneIssue;
                }
            }

            snapshot = new SimultriaViewerBuildSceneSnapshot(
                scenePaths.Count == 1 ? scenePaths[0] : string.Empty,
                gateCount,
                configuration,
                sourceSettings,
                inspectionIssue);
            issue = string.Empty;
            return true;
        }

        private static bool TryInspectScene(
            string scenePath,
            ref int gateCount,
            ref SimultriaViewerBuildConfiguration configuration,
            ICollection<ApiConnectionSettings> sourceSettings,
            out string issue)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                issue = "A selected Build Profile scene is unavailable.";
                return false;
            }

            Scene previewScene = default(Scene);
            bool inspected = false;
            try
            {
                previewScene = EditorSceneManager.OpenPreviewScene(scenePath);
                GameObject[] roots = previewScene.GetRootGameObjects();
                for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    Component[] components = roots[rootIndex]
                        .GetComponentsInChildren<Component>(true);
                    for (int index = 0; index < components.Length; index++)
                    {
                        Component component = components[index];
                        if (component is SimultriaViewerBuildConnectionGate gate)
                        {
                            gateCount++;
                            if (configuration == null)
                            {
                                configuration = gate.BuildConfiguration;
                            }
                        }

                        if (component is
                            ISimultriaViewerConnectionSettingsSource source)
                        {
                            sourceSettings.Add(ReadSettings(source));
                        }
                    }
                }

                inspected = true;
            }
            catch (Exception)
            {
                issue = "A selected Build Profile scene could not be " +
                        "inspected safely.";
                return false;
            }
            finally
            {
                if (previewScene.IsValid() && previewScene.isLoaded)
                {
                    try
                    {
                        EditorSceneManager.ClosePreviewScene(previewScene);
                    }
                    catch (Exception)
                    {
                        inspected = false;
                    }
                }
            }

            issue = inspected
                ? string.Empty
                : "A selected Build Profile preview scene could not be " +
                  "closed safely.";
            return inspected;
        }

        private static ApiConnectionSettings ReadSettings(
            ISimultriaViewerConnectionSettingsSource source)
        {
            try
            {
                return source.SimultriaViewerConnectionSettings;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static bool TryGetEnabledScenePaths(
            EditorBuildSettingsScene[] scenes,
            out IReadOnlyList<string> scenePaths,
            out string issue)
        {
            var enabled = new List<string>();
            if (scenes != null)
            {
                for (int index = 0; index < scenes.Length; index++)
                {
                    EditorBuildSettingsScene scene = scenes[index];
                    if (scene == null || !scene.enabled)
                    {
                        continue;
                    }

                    string path = scene.path?.Trim().Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(path) ||
                        !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                        !path.EndsWith(
                            ".unity",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        scenePaths = Array.Empty<string>();
                        issue = "A selected Build Profile scene path is invalid.";
                        return false;
                    }

                    enabled.Add(path);
                }
            }

            scenePaths = enabled;
            if (enabled.Count == 0)
            {
                issue = "The Build Profile must select one enabled scene.";
                return false;
            }

            issue = enabled.Count == 1
                ? string.Empty
                : "The Build Profile must select exactly one enabled scene.";
            return true;
        }
    }
}
