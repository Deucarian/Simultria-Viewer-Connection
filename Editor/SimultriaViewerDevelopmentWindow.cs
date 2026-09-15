using System;
using System.Threading;
using Deucarian.API.Models;
using Deucarian.Authentication;
using Deucarian.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    public sealed class SimultriaViewerDevelopmentWindow : EditorWindow
    {
        internal static Vector2 CompactMinimumSize => new Vector2(420, 340);
        private CancellationTokenSource operationCancellation;
        private SimultriaViewerDevelopmentPage view;
        private bool active;
        private double nextRefresh;
        internal string Message { get; private set; } = string.Empty;
        internal bool Busy => operationCancellation != null;

        public static void Open()
        {
            var window = DeucarianEditorWindowPages.ShowStandalone<SimultriaViewerDevelopmentWindow>(
                "Viewer development", CompactMinimumSize);
            window.Focus();
        }

        public static IDeucarianEditorPage CreatePage() =>
            DeucarianEditorWindowPages.Create<SimultriaViewerDevelopmentWindow>((window, root) => window.Build(root),
                activate: (window, _) => window.Activate(), deactivate: window => window.Deactivate(),
                update: window => window.Tick());

        public void CreateGUI() => Build(rootVisualElement);
        private void Build(VisualElement root) { view?.Dispose(); view = new SimultriaViewerDevelopmentPage(root, this); }
        private void OnEnable()
        {
            active = true;
            AuthenticationTargetRegistry.TargetsChanged += Refresh;
            SimultriaViewerEditorAuthenticationHost.EnvironmentResolutionChanged += Refresh;
            Undo.undoRedoPerformed += Refresh;
            SimultriaViewerEditorAuthenticationHost.RequestRefresh();
        }
        private void OnDisable()
        {
            Deactivate();
            AuthenticationTargetRegistry.TargetsChanged -= Refresh;
            SimultriaViewerEditorAuthenticationHost.EnvironmentResolutionChanged -= Refresh;
            Undo.undoRedoPerformed -= Refresh;
            view?.Dispose(); view = null;
        }
        private void Activate() { active = true; Refresh(); }
        private void Deactivate()
        {
            active = false;
            if (Busy) Message = "Export canceled.";
            operationCancellation?.Cancel();
            operationCancellation = null;
        }
        internal void Refresh() { if (!active) return; view?.Refresh(); Repaint(); }
        private void OnInspectorUpdate() => Tick();
        private void Tick()
        {
            if (!active || EditorApplication.timeSinceStartup < nextRefresh) return;
            nextRefresh = EditorApplication.timeSinceStartup + .25;
            Refresh();
        }

        internal static SimultriaViewerDevelopmentContext CreateProfile()
        {
            string path = EditorUtility.SaveFilePanelInProject("Create viewer development context",
                "SimultriaViewerDevelopmentContext", "asset", "Choose a path for the credential-free context.");
            if (string.IsNullOrWhiteSpace(path)) return null;
            var profile = CreateInstance<SimultriaViewerDevelopmentContext>();
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssetIfDirty(profile);
            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
            return profile;
        }

        internal async void ExportAsync(SimultriaViewerDevelopmentContext profile)
        {
            if (!active || Busy || profile == null) return;
            var cancellation = new CancellationTokenSource();
            operationCancellation = cancellation;
            Message = "Resolving environment…"; Refresh();
            try
            {
                var creation = await SimultriaViewerDevelopmentCommandService.CreateCommandAsync(
                    profile, SimultriaViewerEnvironmentResolver.CreateDefault(), cancellation.Token);
                if (!active || cancellation.IsCancellationRequested || operationCancellation != cancellation) return;
                if (creation?.Succeeded != true)
                    Message = creation?.Message ?? "The effective environment could not be resolved.";
                else
                {
                    SimultriaViewerWebGlDevelopmentExporter.TryExport(creation.Command, out string result);
                    Message = result;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (active) Message = "Export failed. Check the connection configuration and try again."; }
            finally
            {
                if (operationCancellation == cancellation) operationCancellation = null;
                cancellation.Dispose();
                Refresh();
            }
        }

        internal void ClearExport()
        {
            if (Busy) return;
            SimultriaViewerWebGlDevelopmentExporter.TryClear(out string result);
            Message = result; Refresh();
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
    }
}
