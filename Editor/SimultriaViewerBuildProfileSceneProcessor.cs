using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    /// <summary>Captures build identity in the build's scene copy, not assets.</summary>
    public sealed class SimultriaViewerBuildProfileSceneProcessor :
        IProcessSceneWithReport
    {
        public int callbackOrder => 100;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // Unity also processes scenes for Play Mode with a null report.
            if (report == null)
            {
                return;
            }

            // Build Pipeline validates these actual flags against the selected
            // profile's environment before invoking Unity. No Editor override
            // or mutable active-profile selection participates in this stamp.
            StampScene(scene,
                (report.summary.options & BuildOptions.Development) != 0);
        }

        internal static void StampScene(Scene scene, bool developmentBuild)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SimultriaViewerBuildConnectionGate gate in
                         root.GetComponentsInChildren<SimultriaViewerBuildConnectionGate>(true))
                {
                    gate.CaptureBuildProfileEnvironment(developmentBuild);
                }
            }
        }
    }
}
