using System;
using System.Collections.Generic;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.CommandRouting;
using Deucarian.Editor;
using Deucarian.API.Core;
using Deucarian.Authentication;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    public sealed class SimultriaViewerDevelopmentWindow : EditorWindow
    {
        private Vector2 scroll;
        private string preview = string.Empty;
        private string message = string.Empty;
        private DeucarianEditorStatus messageStatus = DeucarianEditorStatus.Info;
        private bool sending;
        private CancellationTokenSource operationCancellation;

        [MenuItem("Tools/Deucarian/Simultria Viewer Development")]
        public static void Open()
        {
            SimultriaViewerDevelopmentWindow window =
                GetWindow<SimultriaViewerDevelopmentWindow>(
                    "Simultria Viewer Development");
            window.minSize = new Vector2(470f, 520f);
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
                        "Choose one credential-free project/model context. " +
                        "Deployment hosts stay in the project-owned API " +
                        "connection settings; authentication and Command " +
                        "Routing stay package-owned.",
                        "VIEWER DEVELOPMENT");
                    DrawStatus();
                    DrawSelection();
                    DrawRuntimeTargets();
                    DrawActions();
                    DrawPreview();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        DeucarianEditorStatusPanel.DrawStatusCard(message, messageStatus);
                    }

                    DeucarianEditorChrome.DrawFooterVersion(
                        "com.deucarian.simultria-viewer-integration");
                }

                GUILayout.Space(12f);
            }

            GUILayout.Space(12f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawStatus()
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

        private void DrawSelection()
        {
            SimultriaViewerConnectionProjectSettings project =
                SimultriaViewerConnectionProjectSettings.instance;
            SimultriaViewerConnectionUserSettings user =
                SimultriaViewerConnectionUserSettings.instance;
            DeucarianEditorCards.DrawCard(
                "Development context",
                () =>
                {
                    SimultriaViewerDevelopmentContext projectProfile =
                        (SimultriaViewerDevelopmentContext)EditorGUILayout.ObjectField(
                            "Project default",
                            project.DefaultProfile,
                            typeof(SimultriaViewerDevelopmentContext),
                            false);
                    if (projectProfile != project.DefaultProfile)
                    {
                        project.DefaultProfile = projectProfile;
                    }

                    bool useOverride = EditorGUILayout.Toggle(
                        "Use local override",
                        user.UseLocalProfileOverride);
                    if (useOverride != user.UseLocalProfileOverride)
                    {
                        user.UseLocalProfileOverride = useOverride;
                    }

                    using (new EditorGUI.DisabledScope(!user.UseLocalProfileOverride))
                    {
                        SimultriaViewerDevelopmentContext localProfile =
                            (SimultriaViewerDevelopmentContext)EditorGUILayout.ObjectField(
                                "Local context",
                                user.LocalProfile,
                                typeof(SimultriaViewerDevelopmentContext),
                                false);
                        if (localProfile != user.LocalProfile)
                        {
                            user.LocalProfile = localProfile;
                        }
                    }

                    EditorGUILayout.LabelField(
                        "The project default is shared in ProjectSettings. The local override stays in gitignored UserSettings.",
                        EditorStyles.wordWrappedMiniLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (DeucarianEditorButtons.Secondary("Create context"))
                        {
                            CreateProfile();
                        }

                        bool hasProfile = SimultriaViewerDevelopmentContextSelector.TryResolve(
                            out SimultriaViewerDevelopmentContext selected,
                            out _,
                            out _);
                        if (DeucarianEditorButtons.Secondary("Select context", hasProfile))
                        {
                            Selection.activeObject = selected;
                            EditorGUIUtility.PingObject(selected);
                        }
                    }

                    if (SimultriaViewerDevelopmentContextSelector.TryResolve(
                            out SimultriaViewerDevelopmentContext selectedProfile,
                            out string source,
                            out string selectionError))
                    {
                        DrawEnvironmentChooser(selectedProfile, source);
                    }
                    else
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField("Environment", source);
                            EditorGUILayout.HelpBox(
                                selectionError,
                                MessageType.Info);
                        }
                    }
                },
                "No endpoint URL, token, credential, header, or authentication route is stored here.");
        }

        private static void DrawRuntimeTargets()
        {
            SimultriaViewerConnectionProjectSettings settings =
                SimultriaViewerConnectionProjectSettings.instance;
            DeucarianEditorCards.DrawCard(
                "Runtime handoff",
                () =>
                {
                    bool autoLoad = EditorGUILayout.Toggle(
                        "Auto-load on Play",
                        settings.AutoLoadInPlayMode);
                    if (autoLoad != settings.AutoLoadInPlayMode)
                    {
                        settings.AutoLoadInPlayMode = autoLoad;
                    }

                    EditorGUILayout.LabelField(
                        "Auto-load requires exactly one live Authentication target and one initialized scene-owned Command Routing port. No active-viewer selector is stored.",
                        EditorStyles.wordWrappedMiniLabel);
                });
        }

        private void DrawActions()
        {
            bool hasProfile = SimultriaViewerDevelopmentContextSelector.TryResolve(
                out SimultriaViewerDevelopmentContext profile,
                out _,
                out _);
            bool canSend = hasProfile && EditorApplication.isPlaying && !sending;
            DeucarianEditorCards.DrawCard("Actions", () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (DeucarianEditorButtons.Primary(
                            sending ? "Sending..." : "Send to running viewer",
                            canSend,
                            GUILayout.ExpandWidth(true)))
                    {
                        SendAsync(profile);
                    }

                    if (DeucarianEditorButtons.Secondary(
                            "Authentication",
                            true,
                            GUILayout.Width(120f)))
                    {
                        EditorApplication.ExecuteMenuItem(
                            "Tools/Deucarian/Authentication");
                    }

                    if (DeucarianEditorButtons.Secondary(
                            "API Connections",
                            true,
                            GUILayout.Width(120f)))
                    {
                        EditorApplication.ExecuteMenuItem(
                            "Tools/Deucarian/API Connections");
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (DeucarianEditorButtons.Secondary("Preview command", hasProfile))
                    {
                        PreviewAsync(profile);
                    }

                    if (DeucarianEditorButtons.Secondary("Export local WebGL", hasProfile))
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

                if (!EditorApplication.isPlaying)
                {
                    EditorGUILayout.LabelField(
                        "Enter Play Mode to send. Preview and export remain available in Edit Mode.",
                        EditorStyles.wordWrappedMiniLabel);
                }
            });
        }

        private void DrawPreview()
        {
            if (string.IsNullOrWhiteSpace(preview))
            {
                return;
            }

            DeucarianEditorCards.DrawCard("Credential-free command preview", () =>
            {
                EditorGUILayout.TextArea(preview, GUILayout.MinHeight(150f));
                if (DeucarianEditorButtons.Secondary("Hide preview"))
                {
                    preview = string.Empty;
                }
            });
        }

        private async void SendAsync(SimultriaViewerDevelopmentContext profile)
        {
            sending = true;
            Repaint();
            try
            {
                SimultriaViewerDevelopmentCommandService
                    .DevelopmentCommandCreation creation =
                    await CreateCommandAsync(profile);
                if (creation?.Succeeded != true)
                {
                    SetResult(
                        false,
                        creation?.Message ??
                        "The effective environment could not be resolved.");
                    return;
                }

                CommandResult result = await
                    SimultriaViewerDevelopmentCommandService.DispatchAsync(
                        creation.Command,
                        operationCancellation?.Token ?? CancellationToken.None);
                SetResult(
                    result?.Succeeded == true,
                    result?.Succeeded == true
                        ? "Sent the Simultria development context through Command Routing."
                        : result?.Message ?? "The initialization command failed.");
            }
            catch (Exception exception)
            {
                SetResult(
                    false,
                    "Initialization failed with " + exception.GetType().Name + ".");
            }
            finally
            {
                sending = false;
                Repaint();
            }
        }

        private async void PreviewAsync(
            SimultriaViewerDevelopmentContext profile)
        {
            SimultriaViewerDevelopmentCommandService.DevelopmentCommandCreation
                creation = await CreateCommandAsync(profile);
            if (creation?.Succeeded != true)
            {
                preview = string.Empty;
                SetResult(
                    false,
                    creation?.Message ??
                    "The effective environment could not be resolved.");
                return;
            }

            preview = SimultriaViewerInitializationCommand.Serialize(
                creation.Command);
            SetResult(true, "Preview contains IDs and placement only; no credentials or endpoint URLs.");
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

        private void DrawEnvironmentChooser(
            SimultriaViewerDevelopmentContext profile,
            string source)
        {
            SimultriaViewerEnvironmentResolutionMode mode =
                (SimultriaViewerEnvironmentResolutionMode)
                EditorGUILayout.EnumPopup(
                    "Resolution",
                    profile.EnvironmentResolutionMode);
            if (mode != profile.EnvironmentResolutionMode)
            {
                profile.EnvironmentResolutionMode = mode;
                SaveProfileAndRefresh(profile);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Environment source", source);
                EditorGUILayout.LabelField(
                    mode == SimultriaViewerEnvironmentResolutionMode.Manual
                        ? (profile.EnvironmentId.IsEmpty
                            ? "Development (fallback)"
                            : profile.EnvironmentId.Value)
                        : "Simultria Unity build directory",
                    EditorStyles.miniLabel);
            }

            if (mode == SimultriaViewerEnvironmentResolutionMode.Manual)
            {
                DrawManualEnvironmentChooser(profile);
                return;
            }

            DrawAutomaticEnvironmentChooser(profile);
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

        private static void DrawAutomaticEnvironmentChooser(
            SimultriaViewerDevelopmentContext profile)
        {
            BuildDirectoryEnvironmentOptions(
                profile.BuildDirectoryEnvironmentId,
                out string[] options,
                out ApiEnvironmentId[] values,
                out int currentIndex);
            int selected = EditorGUILayout.Popup(
                "Build directory host",
                currentIndex,
                options);
            string product = EditorGUILayout.TextField(
                "Build product",
                profile.BuildProduct);
            string buildOverride = EditorGUILayout.TextField(
                "Version override",
                profile.BuildVersionOverride);
            bool changed = selected != currentIndex ||
                           !string.Equals(
                               product,
                               profile.BuildProduct,
                               StringComparison.Ordinal) ||
                           !string.Equals(
                               buildOverride,
                               profile.BuildVersionOverride,
                               StringComparison.Ordinal);
            if (changed)
            {
                profile.BuildDirectoryEnvironmentId = values[selected];
                profile.BuildProduct = product;
                profile.BuildVersionOverride = buildOverride;
                SaveProfileAndRefresh(profile);
            }

            EditorGUILayout.LabelField(
                string.IsNullOrWhiteSpace(profile.BuildVersionOverride)
                    ? "Runtime version: " + Application.version
                    : "Using the explicit local/editor version override.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                "The selected API environment supplies only the build-directory host. The portal response chooses the effective viewer environment.",
                EditorStyles.wordWrappedMiniLabel);
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
