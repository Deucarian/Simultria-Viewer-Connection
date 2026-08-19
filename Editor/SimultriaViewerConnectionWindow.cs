using System;
using System.Threading;
using Deucarian.CommandRouting;
using Deucarian.Editor;
using Deucarian.API.Core;
using Deucarian.ViewerAuthentication;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection.Editor
{
    public sealed class SimultriaViewerConnectionWindow : EditorWindow
    {
        private Vector2 scroll;
        private string preview = string.Empty;
        private string message = string.Empty;
        private DeucarianEditorStatus messageStatus = DeucarianEditorStatus.Info;
        private bool sending;

        [MenuItem("Tools/Deucarian/Viewer/Simultria Connection")]
        public static void Open()
        {
            SimultriaViewerConnectionWindow window =
                GetWindow<SimultriaViewerConnectionWindow>("Simultria Connection");
            window.minSize = new Vector2(470f, 520f);
            window.Focus();
        }

        private void OnEnable()
        {
            ViewerAuthenticationTargetRegistry.TargetsChanged += Repaint;
        }

        private void OnDisable()
        {
            ViewerAuthenticationTargetRegistry.TargetsChanged -= Repaint;
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
                        "Simultria Connection",
                        "Choose one credential-free project/model profile. Environment, authentication, and Command Routing stay package-owned.",
                        "OPTIONAL VIEWER CONNECTION");
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
                        "Simultria Viewer Connection",
                        "0.1.0");
                }

                GUILayout.Space(12f);
            }

            GUILayout.Space(12f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawStatus()
        {
            bool selected = SimultriaViewerDevelopmentProfileSelector.TryResolve(
                out SimultriaViewerDevelopmentProfile profile,
                out string source,
                out string selectionError);
            SimultriaViewerConnectionStatus status =
                SimultriaViewerConnectionStatus.Capture(profile);
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
                    environment?.IsResolved == true
                        ? environment.DisplayName
                        : status.EnvironmentMessage ?? selectionError,
                    environment?.IsResolved == true
                        ? DeucarianEditorStatus.Success
                        : DeucarianEditorStatus.Warning);

                ViewerAuthenticationStatusSnapshot authentication = status.Authentication;
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
                "Development profile",
                () =>
                {
                    SimultriaViewerDevelopmentProfile projectProfile =
                        (SimultriaViewerDevelopmentProfile)EditorGUILayout.ObjectField(
                            "Project default",
                            project.DefaultProfile,
                            typeof(SimultriaViewerDevelopmentProfile),
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
                        SimultriaViewerDevelopmentProfile localProfile =
                            (SimultriaViewerDevelopmentProfile)EditorGUILayout.ObjectField(
                                "Local profile",
                                user.LocalProfile,
                                typeof(SimultriaViewerDevelopmentProfile),
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
                        if (DeucarianEditorButtons.Secondary("Create profile"))
                        {
                            CreateProfile();
                        }

                        bool hasProfile = SimultriaViewerDevelopmentProfileSelector.TryResolve(
                            out SimultriaViewerDevelopmentProfile selected,
                            out _,
                            out _);
                        if (DeucarianEditorButtons.Secondary("Select profile", hasProfile))
                        {
                            Selection.activeObject = selected;
                            EditorGUIUtility.PingObject(selected);
                        }
                    }
                },
                "No endpoint URL, token, login route, or product-specific fields are stored here.");
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
                        "Auto-load requires exactly one live Viewer Authentication target and one initialized scene-owned Command Routing port. No active-viewer selector is stored.",
                        EditorStyles.wordWrappedMiniLabel);
                });
        }

        private void DrawActions()
        {
            bool hasProfile = SimultriaViewerDevelopmentProfileSelector.TryResolve(
                out SimultriaViewerDevelopmentProfile profile,
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
                            "Tools/Deucarian/Viewer/Authentication");
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (DeucarianEditorButtons.Secondary("Preview command", hasProfile))
                    {
                        Preview(profile);
                    }

                    if (DeucarianEditorButtons.Secondary("Export local WebGL", hasProfile))
                    {
                        SetResult(
                            SimultriaViewerWebGlDevelopmentExporter.TryExport(
                                profile,
                                out string exportMessage),
                            exportMessage);
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

        private async void SendAsync(SimultriaViewerDevelopmentProfile profile)
        {
            if (!SimultriaViewerDevelopmentCommandService.TryCreateCommand(
                    profile,
                    out CommandEnvelope command,
                    out string error))
            {
                SetResult(false, error);
                return;
            }

            sending = true;
            Repaint();
            try
            {
                CommandResult result = await
                    SimultriaViewerDevelopmentCommandService.DispatchAsync(
                        command,
                        CancellationToken.None);
                SetResult(
                    result?.Succeeded == true,
                    result?.Succeeded == true
                        ? "Sent the Simultria development profile through Command Routing."
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

        private void Preview(SimultriaViewerDevelopmentProfile profile)
        {
            if (!SimultriaViewerDevelopmentCommandService.TryCreateCommand(
                    profile,
                    out CommandEnvelope command,
                    out string error))
            {
                preview = string.Empty;
                SetResult(false, error);
                return;
            }

            preview = SimultriaViewerInitializationCommand.Serialize(command);
            SetResult(true, "Preview contains IDs and placement only; no credentials or endpoint URLs.");
        }

        private static void CreateProfile()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Simultria Viewer Development Profile",
                "SimultriaViewerDevelopmentProfile",
                "asset",
                "Choose a project asset path for the credential-free profile.");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var profile = CreateInstance<SimultriaViewerDevelopmentProfile>();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
            if (SimultriaViewerConnectionProjectSettings.instance.DefaultProfile == null)
            {
                SimultriaViewerConnectionProjectSettings.instance.DefaultProfile = profile;
            }
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
