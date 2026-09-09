using System;
using System.Collections.Generic;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.Editor;
using Deucarian.API.Core;
using Deucarian.Authentication;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    public sealed partial class SimultriaViewerDevelopmentWindow
    {


        private static void CreateProfile()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Simultria Viewer Development Context",
                "SimultriaViewerDevelopmentContext",
                "asset",
                "Choose a project asset path for the credential-free context.");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var profile = CreateInstance<SimultriaViewerDevelopmentContext>();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
            if (SimultriaViewerConnectionProjectSettings.instance.DefaultProfile == null)
            {
                SimultriaViewerConnectionProjectSettings.instance.DefaultProfile = profile;
            }
        }

        private static void DrawEnvironmentSelection(
            SimultriaViewerDevelopmentContext profile)
        {
            EditorGUILayout.LabelField(
                "Version directory", "Central Production (fixed)");
            if (profile.EnvironmentResolutionMode ==
                SimultriaViewerEnvironmentResolutionMode.Manual)
            {
                DrawManualEnvironmentChooser(profile);
                return;
            }

            bool resolved = SimultriaViewerEditorAuthenticationHost
                .TryGetEffectiveEnvironment(
                    profile,
                    out ApiEnvironmentId environmentId,
                    out _,
                    out string resolutionMessage);
            if (resolved)
            {
                EditorGUILayout.LabelField(
                    "Environment",
                    environmentId.Value);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    resolutionMessage ??
                    "The automatic environment has not resolved yet.",
                    MessageType.Info);
            }

            EditorGUILayout.LabelField(
                "Automatic routing details are edited on the context asset.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private static void DrawManualEnvironmentChooser(
            SimultriaViewerDevelopmentContext profile)
        {

            ApiEnvironmentId current = profile.EnvironmentId;
            BuildEnvironmentOptions(
                current,
                out string[] options,
                out ApiEnvironmentId[] values,
                out int currentIndex);

            int selected = EditorGUILayout.Popup("Environment", currentIndex, options);
            if (selected == currentIndex)
            {
                return;
            }

            profile.EnvironmentId = values[selected];
            SaveProfileAndRefresh(profile);
        }

        internal static DevelopmentReadiness BuildReadiness(
            bool hasContext,
            bool environmentResolved,
            bool authenticated,
            bool isPlaying,
            bool commandRouteReady,
            string contextError = null,
            string environmentError = null)
        {
            if (!hasContext)
            {
                return DevelopmentReadiness.NeedsAction(
                    string.IsNullOrWhiteSpace(contextError)
                        ? "Choose a development context."
                        : contextError);
            }

            if (!environmentResolved)
            {
                return DevelopmentReadiness.NeedsAction(
                    string.IsNullOrWhiteSpace(environmentError)
                        ? "Resolve the development environment."
                        : environmentError);
            }

            if (!authenticated)
            {
                return DevelopmentReadiness.NeedsAction(
                    "Sign in with Viewer Authentication.");
            }

            if (isPlaying && !commandRouteReady)
            {
                return DevelopmentReadiness.Waiting(
                    "Waiting for the running viewer.");
            }

            return DevelopmentReadiness.Ready(
                isPlaying
                    ? "Ready and connected to the running viewer."
                    : "Ready. Enter Play Mode to auto-load this context.");
        }

        internal enum DevelopmentReadinessLevel
        {
            NeedsAction,
            Waiting,
            Ready
        }

        internal readonly struct DevelopmentReadiness
        {
            private DevelopmentReadiness(
                DevelopmentReadinessLevel level,
                string message)
            {
                Level = level;
                Message = message;
            }

            public DevelopmentReadinessLevel Level { get; }

            public string Message { get; }

            public static DevelopmentReadiness NeedsAction(string message) =>
                new DevelopmentReadiness(
                    DevelopmentReadinessLevel.NeedsAction,
                    message);

            public static DevelopmentReadiness Waiting(string message) =>
                new DevelopmentReadiness(
                    DevelopmentReadinessLevel.Waiting,
                    message);

            public static DevelopmentReadiness Ready(string message) =>
                new DevelopmentReadiness(
                    DevelopmentReadinessLevel.Ready,
                    message);
        }

        internal static void BuildEnvironmentOptions(
            ApiEnvironmentId current,
            out string[] options,
            out ApiEnvironmentId[] values,
            out int selectedIndex) =>
            SimultriaViewerEnvironmentOptions.BuildEnvironmentOptions(
                current, out options, out values, out selectedIndex);

        internal static void BuildDirectoryEnvironmentOptions(
            ApiEnvironmentId current,
            out string[] options,
            out ApiEnvironmentId[] values,
            out int selectedIndex) =>
            SimultriaViewerEnvironmentOptions.BuildDirectoryEnvironmentOptions(
                current, out options, out values, out selectedIndex);

        private static void SaveProfileAndRefresh(
            SimultriaViewerDevelopmentContext profile)
        {
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            SimultriaViewerEditorAuthenticationHost.RequestRefresh();
        }

        private static string ResolveDisplayedBuildVersion(
            SimultriaViewerDevelopmentContext profile)
        {
            if (profile == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(profile.BuildVersionOverride)
                ? Application.version
                : profile.BuildVersionOverride;
        }

        private void SetResult(bool succeeded, string resultMessage)
        {
            message = resultMessage ?? string.Empty;
            messageStatus = succeeded
                    ? DeucarianEditorStatus.Success
                    : DeucarianEditorStatus.Error;
            Repaint();
        }

    }
}
