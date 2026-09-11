using System;
using Deucarian.API.Configuration;
using Deucarian.API.Models;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Controls = Deucarian.Editor.DeucarianEditorWorkspaceControls;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    internal sealed class SimultriaViewerDevelopmentPage : IDisposable
    {
        private readonly SimultriaViewerDevelopmentWindow owner;
        private readonly DeucarianEditorWorkspace workspace;
        private readonly DeucarianEditorWorkspaceForm scope;
        private readonly DeucarianEditorWorkspaceForm advanced;
        private readonly VisualElement configuration;
        private readonly DeucarianEditorStatusSummary runtime;
        private readonly Label readiness;
        private readonly VisualElement readinessIcon;
        private readonly VisualElement contextStatus;
        private readonly Button apply;
        private readonly Button export;
        private readonly Button clearExport;
        private readonly Label result;
        private SimultriaViewerDevelopmentContext profile;
        private DeucarianEditorWorkspaceForm fields;
        private int project, model, initialProject, initialModel;
        private ApiEnvironmentId environment, initialEnvironment;
        private ApiConnectionSettings connection, initialConnection;
        private SimultriaViewerEnvironmentResolutionMode initialMode;
        private bool useLocal, autoLoad, dirty;

        internal SimultriaViewerDevelopmentPage(VisualElement root, SimultriaViewerDevelopmentWindow owner)
        {
            this.owner = owner;
            workspace = new DeucarianEditorWorkspace(root, Application.productName);
            workspace.Title.text = "Viewer development";
            workspace.Subtitle.text = "Set the context for a local viewer session.";
            DeucarianEditorWorkspaceNavigation.Populate(workspace, DeucarianToolIds.SimultriaViewerDevelopment);
            var scroll = Controls.Scroll("viewer-development");
            workspace.Content.Add(scroll);
            var card = Controls.Panel("viewer-context");
            scroll.Add(card);
            scope = new DeucarianEditorWorkspaceForm(card);
            scope.Asset("viewer-context-asset", "Context asset", typeof(SimultriaViewerDevelopmentContext),
                () => profile, value => Select(value as SimultriaViewerDevelopmentContext));
            configuration = new VisualElement();
            card.Add(configuration);
            card.Add(Controls.Divider());
            contextStatus = Controls.Region("viewer-context-status", "dw-summary-row");
            readinessIcon = Controls.Icon(DeucarianEditorIconIds.Success);
            readinessIcon.AddToClassList("dw-status-success");
            contextStatus.Add(readinessIcon);
            readiness = Controls.Label(string.Empty, "dw-readonly");
            contextStatus.Add(readiness);
            apply = Controls.Button("Apply context", Apply, true);
            apply.name = "viewer-apply-context";
            contextStatus.Add(apply);
            card.Add(contextStatus);
            runtime = new DeucarianEditorStatusSummary("viewer-runtime-session");
            Controls.Show(runtime.Root.Q(className: "dw-icon"), false);
            runtime.Actions.Add(Controls.IconButton("Open authentication", DeucarianEditorIconIds.ChevronRight,
                () => DeucarianEditorNavigation.Open(workspace.Root, DeucarianToolIds.Authentication)));
            scroll.Add(runtime.Root);
            advanced = new DeucarianEditorWorkspaceForm(scroll).Section("Advanced settings", true);
            advanced.Toggle("viewer-local-override", "Use local override", () => useLocal, value => { useLocal = value; dirty = true; Refresh(); });
            advanced.Toggle("viewer-auto-load", "Auto-load on Play", () => autoLoad, value => { autoLoad = value; dirty = true; Refresh(); });
            advanced.Note(() => useLocal ? "Context selection stays in gitignored UserSettings." : "Context selection is shared in ProjectSettings.");
            advanced.Action("viewer-create-context", "Create context", () =>
            {
                var created = SimultriaViewerDevelopmentWindow.CreateProfile();
                if (created != null) Select(created);
            });
            advanced.Action("viewer-open-context", "Open context asset", () =>
            {
                if (profile != null) { Selection.activeObject = profile; EditorGUIUtility.PingObject(profile); }
            }, () => profile != null);
            export = Controls.Button("Export local WebGL context", () => owner.ExportAsync(profile));
            clearExport = Controls.Button("Clear export", owner.ClearExport);
            advanced.Root.Add(Controls.Actions(export, clearExport));
            advanced.Note(() => "Optional, credential-free export for a local WebGL harness.");
            result = Controls.Label(string.Empty, "dw-note"); advanced.Root.Add(result);
            useLocal = SimultriaViewerConnectionUserSettings.instance.UseLocalProfileOverride;
            autoLoad = SimultriaViewerConnectionProjectSettings.instance.AutoLoadInPlayMode;
            SimultriaViewerDevelopmentContextSelector.TryResolve(out var selected, out _, out _);
            Select(selected);
            dirty = false;
            scope.Refresh();
            advanced.Refresh();
            Refresh();
        }

        private bool CanEdit => profile != null && AssetDatabase.GetAssetPath(profile).Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal);
        private bool HasConflict => profile != null && (profile.ProjectId != initialProject ||
            profile.ModelId != initialModel || profile.EnvironmentId != initialEnvironment ||
            profile.ConnectionSettingsReference != initialConnection || profile.EnvironmentResolutionMode != initialMode);

        private void Select(SimultriaViewerDevelopmentContext selected)
        {
            profile = selected;
            initialProject = project = profile != null ? profile.ProjectId : 0;
            initialModel = model = profile != null ? profile.ModelId : 0;
            initialEnvironment = environment = profile != null ? profile.EnvironmentId : default;
            initialConnection = connection = profile != null ? profile.ConnectionSettingsReference : null;
            initialMode = profile != null ? profile.EnvironmentResolutionMode : default;
            dirty = true;
            configuration.Clear();
            fields = new DeucarianEditorWorkspaceForm(configuration);
            if (profile == null)
            {
                fields.Note(() => "Choose a context asset, or create one in Advanced settings.");
                Refresh(); return;
            }
            bool automatic = profile.EnvironmentResolutionMode != SimultriaViewerEnvironmentResolutionMode.Manual;
            if (automatic)
            {
                fields.ReadOnly("viewer-runtime-environment", "Environment", () =>
                    SimultriaViewerEditorAuthenticationHost.TryGetEffectiveEnvironment(profile, out var id, out _, out _)
                        ? id.Value : "Resolving from the exact build version…");
                var lookup = fields.Section("Version lookup", true);
                SimultriaViewerEnvironmentOptions.BuildLookupEnvironmentOptions(profile.LookupEnvironment,
                    out var lookupNames, out var lookupValues, out int lookupIndex);
                var choice = lookup.Choice("viewer-lookup-environment", "Lookup directory", lookupNames,
                    () => Array.IndexOf(lookupValues, profile.LookupEnvironment), value =>
                    {
                        if (!CanEdit) return;
                        Undo.RecordObject(profile, "Change version lookup directory");
                        profile.LookupEnvironment = lookupValues[value];
                        EditorUtility.SetDirty(profile);
                        AssetDatabase.SaveAssetIfDirty(profile);
                        SimultriaViewerEditorAuthenticationHost.RequestRefresh();
                    });
                choice.SetEnabled(CanEdit);
                lookup.Note(() => "This directory finds the version record. That record selects the runtime environment.");
            }
            else
            {
                SimultriaViewerDevelopmentWindow.BuildEnvironmentOptions(environment, out var names, out var values, out int selectedIndex);
                fields.Choice("viewer-environment", "Environment", names, () => Array.IndexOf(values, environment),
                    value => { environment = values[value]; dirty = true; Refresh(); }).SetEnabled(CanEdit);
            }
            fields.Integer("viewer-project", "Project ID", () => project, value => { project = value; dirty = true; Refresh(); }).SetEnabled(CanEdit);
            fields.Integer("viewer-model", "Model ID", () => model, value => { model = value; dirty = true; Refresh(); }).SetEnabled(CanEdit);
            if (connection == null)
                fields.Asset("viewer-api-connection", "API connection", typeof(ApiConnectionSettings),
                    () => connection, value => { connection = value as ApiConnectionSettings; dirty = true; Refresh(); }).SetEnabled(CanEdit);
            Refresh();
        }

        private void Apply()
        {
            if (profile == null || HasConflict || project <= 0 || model <= 0) return;
            if (CanEdit)
            {
                Undo.RecordObject(profile, "Apply viewer development context");
                profile.ProjectId = project; profile.ModelId = model;
                profile.ConnectionSettingsReference = connection;
                if (profile.EnvironmentResolutionMode == SimultriaViewerEnvironmentResolutionMode.Manual)
                    profile.EnvironmentId = environment;
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }
            var user = SimultriaViewerConnectionUserSettings.instance;
            var settings = SimultriaViewerConnectionProjectSettings.instance;
            if (useLocal) user.LocalProfile = profile; else settings.DefaultProfile = profile;
            user.UseLocalProfileOverride = useLocal;
            settings.AutoLoadInPlayMode = autoLoad;
            initialProject = profile.ProjectId; initialModel = profile.ModelId;
            initialEnvironment = profile.EnvironmentId; initialConnection = profile.ConnectionSettingsReference;
            dirty = false;
            SimultriaViewerEditorAuthenticationHost.RequestRefresh();
            Refresh();
        }

        internal void Refresh()
        {
            scope.Refresh();
            fields?.Refresh();
            advanced.Refresh();
            bool conflicted = HasConflict;
            readiness.text = profile == null ? "Choose a context" : conflicted ? "Asset changed externally. Reselect it to reload."
                : project <= 0 || model <= 0 ? "Enter positive project and model IDs"
                : connection == null ? "Choose an API connection" : dirty ? "Ready to apply" : "Context applied";
            apply.SetEnabled(profile != null && !conflicted && project > 0 && model > 0 && connection != null && !owner.Busy);
            Controls.Show(readinessIcon, profile != null && !conflicted && project > 0 && model > 0 && connection != null);
            SimultriaViewerDevelopmentContextSelector.TryResolve(out var selected, out _, out string selectionError);
            bool resolved = SimultriaViewerEditorAuthenticationHost.TryGetEffectiveEnvironment(selected,
                out var effective, out _, out string environmentMessage);
            var status = SimultriaViewerConnectionStatus.Capture(selected, resolved ? effective : default);
            bool routed = SimultriaViewerDevelopmentCommandService.TryResolveCommandRoute(out _, out _);
            var state = SimultriaViewerDevelopmentWindow.BuildReadiness(selected != null, resolved,
                status.Authentication?.HasAccessToken == true, EditorApplication.isPlaying, routed,
                selectionError, environmentMessage);
            runtime.Set("Runtime session", EditorApplication.isPlaying ? state.Message
                : "Start Play Mode to inspect the active viewer.",
                EditorApplication.isPlaying && state.Level == SimultriaViewerDevelopmentWindow.DevelopmentReadinessLevel.Ready
                    ? DeucarianEditorStatus.Success : DeucarianEditorStatus.Info);
            export.SetEnabled(profile != null && !owner.Busy && !dirty && !conflicted);
            export.tooltip = dirty ? "Apply the context before exporting." : "Export a credential-free context.";
            clearExport.SetEnabled(!owner.Busy);
            result.text = owner.Message;
            Controls.Show(result, !string.IsNullOrWhiteSpace(owner.Message));
        }

        public void Dispose() => workspace.Dispose();
    }
}
