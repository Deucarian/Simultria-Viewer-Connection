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
    public sealed class SimultriaViewerDevelopmentWindow : EditorWindow
    {
        internal static Vector2 CompactMinimumSize =>
            new Vector2(420f, 340f);

        private Vector2 scroll;
        private string message = string.Empty;
        private DeucarianEditorStatus messageStatus = DeucarianEditorStatus.Info;
        private bool showAdvanced;
        private CancellationTokenSource operationCancellation;

        [MenuItem("Tools/Deucarian/Simultria Viewer Development")]
        public static void Open()
        {
            SimultriaViewerDevelopmentWindow window =
                GetWindow<SimultriaViewerDevelopmentWindow>(
                    "Simultria Viewer Development");
            window.minSize = CompactMinimumSize;
            window.Focus();
        }

        private void OnEnable()
        {
            AuthenticationTargetRegistry.TargetsChanged += Repaint;
            SimultriaViewerEditorAuthenticationHost
                .EnvironmentResolutionChanged += Repaint;
            SimultriaViewerEditorAuthenticationHost.RequestRefresh();
        }

        private void OnDisable()
        {
            AuthenticationTargetRegistry.TargetsChanged -= Repaint;
            SimultriaViewerEditorAuthenticationHost
                .EnvironmentResolutionChanged -= Repaint;
            operationCancellation?.Cancel();
            operationCancellation?.Dispose();
            operationCancellation = null;
        }

        private void OnGUI()
        {
            DeucarianEditorWindowChrome.DrawImGuiWindowBackground(
                new Rect(0f, 0f, position.width, position.height));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUILayout.Space(12f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12f);
                using (new EditorGUILayout.VerticalScope())
                {
                    DeucarianEditorCards.DrawHeaderCard(
                        "Simultria Viewer Development",
                        "Choose the credential-free context used when you " +
                        "press Play locally.",
                        "VIEWER DEVELOPMENT");
                    DrawReadiness();
                    DrawDevelopmentContext();
                    showAdvanced = EditorGUILayout.Foldout(
                        showAdvanced,
                        "Advanced",
                        true);
                    if (showAdvanced)
                    {
                        DrawDetailedStatus();
                        DrawLocalWebGlExport();
                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            DeucarianEditorStatusPanel.DrawStatusCard(
                                message,
                                messageStatus);
                        }
                    }

                    DeucarianEditorChrome.DrawFooterVersion(
                        "com.deucarian.simultria-viewer-integration");
                }

                GUILayout.Space(12f);
            }

            GUILayout.Space(12f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawReadiness()
        {
            bool hasContext =
                SimultriaViewerDevelopmentContextSelector.TryResolve(
                    out SimultriaViewerDevelopmentContext profile,
                    out _,
                    out string selectionError);
            bool environmentResolved =
                SimultriaViewerEditorAuthenticationHost
                    .TryGetEffectiveEnvironment(
                        profile,
                        out ApiEnvironmentId effectiveEnvironment,
                        out _,
                        out string resolutionMessage);
            SimultriaViewerConnectionStatus status = environmentResolved
                ? SimultriaViewerConnectionStatus.Capture(
                    profile,
                    effectiveEnvironment)
                : SimultriaViewerConnectionStatus.Capture(
                    profile,
                    default(ApiEnvironmentId));
            bool authenticated =
                status.Authentication?.HasAccessToken == true;
            bool commandRouteReady =
                SimultriaViewerDevelopmentCommandService
                    .TryResolveCommandRoute(out _, out _);
            DevelopmentReadiness readiness = BuildReadiness(
                hasContext,
                environmentResolved,
                authenticated,
                EditorApplication.isPlaying,
                commandRouteReady,
                selectionError,
                resolutionMessage ?? status.EnvironmentMessage);

            DeucarianEditorStatusPanel.DrawStatusCard(
                readiness.Message,
                readiness.Level switch
                {
                    DevelopmentReadinessLevel.Ready =>
                        DeucarianEditorStatus.Success,
                    DevelopmentReadinessLevel.Waiting =>
                        DeucarianEditorStatus.Info,
                    _ => DeucarianEditorStatus.Warning
                });
        }

        private void DrawDetailedStatus()
        {
            bool selected = SimultriaViewerDevelopmentContextSelector.TryResolve(
                out SimultriaViewerDevelopmentContext profile,
                out string source,
                out string selectionError);
            bool environmentSelected =
                SimultriaViewerEditorAuthenticationHost
                    .TryGetEffectiveEnvironment(
                        profile,
                        out ApiEnvironmentId effectiveEnvironment,
                        out SimultriaViewerEnvironmentResolution resolution,
                        out string resolutionMessage);
            SimultriaViewerConnectionStatus status = environmentSelected
                ? SimultriaViewerConnectionStatus.Capture(
                    profile,
                    effectiveEnvironment)
                : SimultriaViewerConnectionStatus.Capture(
                    profile,
                    default(ApiEnvironmentId));
            bool commandRouteReady =
                SimultriaViewerDevelopmentCommandService.TryResolveCommandRoute(
                    out _,
                    out _);

            DeucarianEditorCards.DrawCard("Connection status", () =>
            {
                DrawStatusRow(
                    "Profile",
                    selected ? source : "Missing",
                    selected ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Warning);

                ApiEnvironmentStatus environment = status.Environment;
                DrawStatusRow(
                    "Environment",
                    environmentSelected && environment?.IsResolved == true
                        ? environment.DisplayName
                        : resolutionMessage ?? status.EnvironmentMessage ??
                          selectionError,
                    environmentSelected && environment?.IsResolved == true
                        ? DeucarianEditorStatus.Success
                        : DeucarianEditorStatus.Warning);

                if (profile?.EnvironmentResolutionMode ==
                    SimultriaViewerEnvironmentResolutionMode
                        .AutomaticFromUnityBuildVersion)
                {
                    DrawStatusRow(
                        "Build input",
                        resolution?.BuildVersion ??
                        ResolveDisplayedBuildVersion(profile),
                        string.IsNullOrWhiteSpace(
                            resolution?.BuildVersion ??
                            ResolveDisplayedBuildVersion(profile))
                            ? DeucarianEditorStatus.Warning
                            : DeucarianEditorStatus.Info);
                    DrawStatusRow(
                        "Resolution",
                        resolution?.Source ?? "Simultria Unity build directory",
                        resolution?.Succeeded == true
                            ? DeucarianEditorStatus.Success
                            : DeucarianEditorStatus.Info);
                }

                AuthenticationStatusSnapshot authentication = status.Authentication;
                DrawStatusRow(
                    "Authentication",
                    authentication == null
                        ? "No unambiguous target"
                        : authentication.Status.ToString(),
                    authentication?.HasAccessToken == true
                        ? DeucarianEditorStatus.Success
                        : DeucarianEditorStatus.Warning);
                DrawStatusRow(
                    "Initialization",
                    commandRouteReady
                        ? "Command route ready"
                        : "Waiting for viewer",
                    commandRouteReady
                        ? DeucarianEditorStatus.Success
                        : DeucarianEditorStatus.Info);
            });
        }

        private static void DrawStatusRow(
            string label,
            string value,
            DeucarianEditorStatus status)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(112f));
                EditorGUILayout.LabelField(
                    string.IsNullOrWhiteSpace(value) ? "-" : value,
                    GUILayout.ExpandWidth(true));
                DeucarianEditorStatusBadge.Draw(
                    status.ToString(),
                    status,
                    GUILayout.Width(76f));
            }
        }

        private void DrawDevelopmentContext()
        {
            SimultriaViewerConnectionProjectSettings project =
                SimultriaViewerConnectionProjectSettings.instance;
            SimultriaViewerConnectionUserSettings user =
                SimultriaViewerConnectionUserSettings.instance;
            bool useOverride = user.UseLocalProfileOverride;
            DeucarianEditorCards.DrawCard(
                "Development context",
                () =>
                {
                    useOverride = EditorGUILayout.Toggle(
                        "Use local override",
                        user.UseLocalProfileOverride);
                    if (useOverride != user.UseLocalProfileOverride)
                    {
                        user.UseLocalProfileOverride = useOverride;
                        if (useOverride && user.LocalProfile == null)
                        {
                            user.LocalProfile = project.DefaultProfile;
                        }
                    }

                    SimultriaViewerDevelopmentContext selected = useOverride
                        ? user.LocalProfile
                        : project.DefaultProfile;
                    SimultriaViewerDevelopmentContext context =
                        (SimultriaViewerDevelopmentContext)
                        EditorGUILayout.ObjectField(
                            "Context",
                            selected,
                            typeof(SimultriaViewerDevelopmentContext),
                            false);
                    if (context != selected)
                    {
                        if (useOverride)
                        {
                            user.LocalProfile = context;
                        }
                        else
                        {
                            project.DefaultProfile = context;
                        }

                        selected = context;
                    }

                    if (selected != null)
                    {
                        DrawEnvironmentSelection(selected);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "Choose or create a development context.",
                            MessageType.Info);
                    }

                    bool autoLoad = EditorGUILayout.Toggle(
                        "Auto-load on Play",
                        project.AutoLoadInPlayMode);
                    if (autoLoad != project.AutoLoadInPlayMode)
                    {
                        project.AutoLoadInPlayMode = autoLoad;
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (selected == null &&
                            DeucarianEditorButtons.Secondary(
                                "Create context"))
                        {
                            CreateProfile();
                        }

                        if (DeucarianEditorButtons.Primary(
                                "Open context asset",
                                selected != null,
                                GUILayout.ExpandWidth(true)))
                        {
                            Selection.activeObject = selected;
                            EditorGUIUtility.PingObject(selected);
                        }
                    }
                },
                useOverride
                    ? "This override stays in gitignored UserSettings."
                    : "The project default is shared in ProjectSettings.");
        }

        private void DrawLocalWebGlExport()
        {
            bool hasProfile =
                SimultriaViewerDevelopmentContextSelector.TryResolve(
                    out SimultriaViewerDevelopmentContext profile,
                    out _,
                    out _);
            DeucarianEditorCards.DrawCard("Local WebGL export", () =>
            {
                EditorGUILayout.LabelField(
                    "Optional credential-free export for a local WebGL harness.",
                    EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (DeucarianEditorButtons.Secondary(
                            "Export",
                            hasProfile))
                    {
                        ExportAsync(profile);
                    }

                    if (DeucarianEditorButtons.Secondary("Clear export"))
                    {
                        SimultriaViewerWebGlDevelopmentExporter.TryClear(
                            out string clearMessage);
                        SetResult(true, clearMessage);
                    }
                }
            });
        }

        private async void ExportAsync(
            SimultriaViewerDevelopmentContext profile)
        {
            SimultriaViewerDevelopmentCommandService.DevelopmentCommandCreation
                creation = await CreateCommandAsync(profile);
            if (creation?.Succeeded != true)
            {
                SetResult(
                    false,
                    creation?.Message ??
                    "The effective environment could not be resolved.");
                return;
            }

            SetResult(
                SimultriaViewerWebGlDevelopmentExporter.TryExport(
                    creation.Command,
                    out string exportMessage),
                exportMessage);
        }

        private async Task<SimultriaViewerDevelopmentCommandService
            .DevelopmentCommandCreation> CreateCommandAsync(
                SimultriaViewerDevelopmentContext profile)
        {
            operationCancellation?.Cancel();
            operationCancellation?.Dispose();
            operationCancellation = new CancellationTokenSource();
            try
            {
                return await SimultriaViewerDevelopmentCommandService
                    .CreateCommandAsync(
                        profile,
                        SimultriaViewerEnvironmentResolver.CreateDefault(),
                        operationCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return SimultriaViewerDevelopmentCommandService
                    .DevelopmentCommandCreation.Failure(
                        null,
                        "Environment resolution was canceled.");
            }
        }

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
            out int selectedIndex)
        {
            ApiEnvironmentId fallbackCurrent = current.IsEmpty
                ? SimultriaEnvironmentIds.Development
                : current;
            var optionLabels = new List<string>();
            var optionValues = new List<ApiEnvironmentId>();
            foreach (var descriptor in SimultriaEnvironmentDescriptors.Standard)
            {
                ApiEnvironmentId environmentId = descriptor.EnvironmentId;
                if (environmentId.IsEmpty)
                {
                    continue;
                }

                optionLabels.Add(descriptor.DisplayName);
                optionValues.Add(environmentId);
            }

            selectedIndex = FindOptionIndex(optionValues, fallbackCurrent);
            if (selectedIndex < 0 && !current.IsEmpty)
            {
                selectedIndex = optionLabels.Count;
                optionLabels.Add($"Custom ({current.Value})");
                optionValues.Add(current);
            }

            options = optionLabels.ToArray();
            values = optionValues.ToArray();
        }

        internal static void BuildDirectoryEnvironmentOptions(
            ApiEnvironmentId current,
            out string[] options,
            out ApiEnvironmentId[] values,
            out int selectedIndex)
        {
            BuildEnvironmentOptions(
                current,
                out string[] canonicalLabels,
                out ApiEnvironmentId[] canonicalValues,
                out int canonicalIndex);
            options = new string[canonicalLabels.Length + 1];
            values = new ApiEnvironmentId[canonicalValues.Length + 1];
            options[0] = "Choose configured environment...";
            Array.Copy(canonicalLabels, 0, options, 1, canonicalLabels.Length);
            Array.Copy(canonicalValues, 0, values, 1, canonicalValues.Length);
            selectedIndex = current.IsEmpty ? 0 : canonicalIndex + 1;
        }

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

        private static int FindOptionIndex(
            List<ApiEnvironmentId> optionValues,
            ApiEnvironmentId selected)
        {
            string selectedValue = selected.Value;
            for (int i = 0; i < optionValues.Count; i++)
            {
                if (string.Equals(
                        optionValues[i].Value,
                        selectedValue,
                        StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
