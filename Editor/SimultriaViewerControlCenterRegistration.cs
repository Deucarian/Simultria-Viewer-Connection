using System;
using System.Collections.Generic;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Editor;
using Deucarian.API.Models;
using Deucarian.Authentication;
using Deucarian.Editor;
using Deucarian.Simultria.API.Configuration;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    [InitializeOnLoad]
    internal static class SimultriaViewerControlCenterRegistration
    {
        private const string PackageId =
            "com.deucarian.simultria-viewer-integration";
        private static readonly IDisposable ToolRegistration;
        private static readonly IDisposable CardRegistration;

        static SimultriaViewerControlCenterRegistration()
        {
            ToolRegistration = DeucarianToolRegistry.Register(
                new DeucarianToolDescriptor(
                    DeucarianToolIds.SimultriaViewerDevelopment,
                    "Simultria Viewer Development",
                    "Configure the local Simultria-backed viewer development context.",
                    DeucarianControlCenterArea.Connections,
                    SimultriaViewerDevelopmentWindow.Open,
                    PackageId,
                    searchTerms: new[] { "simultria", "viewer", "development", "context" },
                    order: 120));

            CardRegistration = DeucarianControlCenterRegistry.RegisterCardProvider(
                new SimultriaViewerCardProvider());
        }
    }

    internal sealed class SimultriaViewerCardProvider :
        IDeucarianControlCenterCardProvider
    {
        private const string PackageId =
            "com.deucarian.simultria-viewer-integration";

        public string Id => PackageId + ".control-center";

        public IEnumerable<DeucarianControlCenterCard> Capture(
            DeucarianControlCenterContext context)
        {
            bool hasContext =
                SimultriaViewerDevelopmentContextSelector.TryResolve(
                    out SimultriaViewerDevelopmentContext profile,
                    out string source,
                    out string selectionError);
            bool apiBindingReady = HasCanonicalApiBinding(profile);
            ApiEnvironmentId effectiveEnvironment = default(ApiEnvironmentId);
            SimultriaViewerEnvironmentResolution resolution = null;
            string resolutionMessage = null;
            bool hasEffectiveEnvironment = hasContext &&
                SimultriaViewerEditorAuthenticationHost.TryGetEffectiveEnvironment(
                    profile,
                    out effectiveEnvironment,
                    out resolution,
                    out resolutionMessage);
            ApiEnvironmentStatus environment = null;
            string environmentError = null;
            bool environmentReady = hasEffectiveEnvironment &&
                profile.TryResolveEnvironment(
                    effectiveEnvironment,
                    out environment,
                    out environmentError);
            SimultriaViewerConnectionStatus connection =
                hasEffectiveEnvironment
                    ? SimultriaViewerConnectionStatus.Capture(
                        profile,
                        effectiveEnvironment)
                    : null;
            AuthenticationStatusSnapshot authentication =
                connection?.Authentication;
            bool isPlaying = EditorApplication.isPlaying;
            bool commandRouteReady = isPlaying &&
                SimultriaViewerDevelopmentCommandService.TryResolveCommandRoute(
                    out _,
                    out _);

            yield return CreateCard(new SimultriaViewerControlCenterSnapshot(
                hasContext,
                hasContext ? source : selectionError,
                apiBindingReady,
                environmentReady,
                ResolveEnvironmentSummary(
                    environmentReady ? environment : null,
                    resolutionMessage ?? environmentError),
                ResolveBuildInput(profile, resolution),
                profile != null && profile.ProjectId > 0 && profile.ModelId > 0,
                authentication?.HasAccessToken == true,
                authentication == null
                    ? "Unauthenticated"
                    : authentication.Status.ToString(),
                isPlaying,
                commandRouteReady,
                SimultriaViewerConnectionProjectSettings
                    .instance.AutoLoadInPlayMode));
        }

        internal static DeucarianControlCenterCard CreateCard(
            SimultriaViewerControlCenterSnapshot snapshot)
        {
            DeucarianControlCenterStatus status;
            string statusText;
            if (!snapshot.HasContext)
            {
                status = DeucarianControlCenterStatus.Warning;
                statusText = "Context required";
            }
            else if (!snapshot.ApiBindingReady)
            {
                status = DeucarianControlCenterStatus.Error;
                statusText = "API binding required";
            }
            else if (!snapshot.EnvironmentReady)
            {
                status = DeucarianControlCenterStatus.Error;
                statusText = "Environment unresolved";
            }
            else if (!snapshot.IdentifiersReady)
            {
                status = DeucarianControlCenterStatus.Error;
                statusText = "Project and model IDs required";
            }
            else if (!snapshot.AuthenticationReady)
            {
                status = DeucarianControlCenterStatus.Warning;
                statusText = "Authentication required";
            }
            else if (snapshot.IsPlaying && !snapshot.CommandRouteReady)
            {
                status = DeucarianControlCenterStatus.Warning;
                statusText = "Waiting for command route";
            }
            else
            {
                status = DeucarianControlCenterStatus.Success;
                statusText = snapshot.IsPlaying
                    ? "Viewer connection ready"
                    : "Development configuration ready";
            }

            return new DeucarianControlCenterCard(
                PackageId + ".development-context",
                DeucarianControlCenterArea.Connections,
                "Simultria Viewer Development",
                "Credential-free local viewer connection readiness.",
                PackageId,
                status,
                statusText,
                order: 120,
                details: new[]
                {
                    "Context: " + snapshot.ContextSummary,
                    "API binding: " +
                    (snapshot.ApiBindingReady ? "ready" : "missing or mismatched"),
                    "Environment: " + snapshot.EnvironmentSummary,
                    "Build input: " + snapshot.BuildInput,
                    "Project/model IDs: " +
                    (snapshot.IdentifiersReady ? "configured" : "incomplete"),
                    "Authentication: " + snapshot.AuthenticationSummary,
                    "Command route: " +
                    (snapshot.IsPlaying
                        ? snapshot.CommandRouteReady ? "ready" : "waiting"
                        : "available in Play Mode"),
                    snapshot.AutoLoad
                        ? "Play Mode auto-load: enabled"
                        : "Play Mode auto-load: disabled"
                },
                actions: new[]
                {
                    new DeucarianControlCenterAction(
                        PackageId + ".open",
                        "Open Viewer Development",
                        SimultriaViewerDevelopmentWindow.Open)
                },
                searchTerms: new[]
                {
                    "simultria", "viewer", "context", "connection",
                    "environment", "authentication", "command route"
                });
        }

        private static bool HasCanonicalApiBinding(
            SimultriaViewerDevelopmentContext profile)
        {
            return profile != null &&
                ApiConnectionProjectSettings.instance.TryResolve(
                    SimultriaServiceIds.ApiV2,
                    out ApiConnectionSettings boundSettings,
                    out _) &&
                ReferenceEquals(
                    boundSettings,
                    profile.ConnectionSettingsReference);
        }

        private static string ResolveEnvironmentSummary(
            ApiEnvironmentStatus environment,
            string message)
        {
            return environment?.IsResolved == true
                ? Bound(environment.DisplayName, 80, "Configured")
                : Bound(message, 120, "Not resolved");
        }

        private static string ResolveBuildInput(
            SimultriaViewerDevelopmentContext profile,
            SimultriaViewerEnvironmentResolution resolution)
        {
            if (profile == null)
            {
                return "Not configured";
            }

            if (profile.EnvironmentResolutionMode ==
                SimultriaViewerEnvironmentResolutionMode.Manual)
            {
                return profile.EnvironmentId.IsEmpty
                    ? "Explicit environment missing"
                    : "Explicit environment " +
                      Bound(profile.EnvironmentId.Value, 64, "selected");
            }

            string buildVersion = resolution?.BuildVersion;
            if (string.IsNullOrWhiteSpace(buildVersion))
            {
                buildVersion = string.IsNullOrWhiteSpace(
                    profile.BuildVersionOverride)
                    ? Application.version
                    : profile.BuildVersionOverride;
            }

            return "Automatic from build version " +
                   Bound(buildVersion, 80, "not available");
        }

        private static string Bound(
            string value,
            int maximumLength,
            string fallback)
        {
            string normalized = string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
            return normalized.Length <= maximumLength
                ? normalized
                : normalized.Substring(0, maximumLength) + "…";
        }
    }

    internal sealed class SimultriaViewerControlCenterSnapshot
    {
        internal SimultriaViewerControlCenterSnapshot(
            bool hasContext,
            string contextSummary,
            bool apiBindingReady,
            bool environmentReady,
            string environmentSummary,
            string buildInput,
            bool identifiersReady,
            bool authenticationReady,
            string authenticationSummary,
            bool isPlaying,
            bool commandRouteReady,
            bool autoLoad)
        {
            HasContext = hasContext;
            ContextSummary = contextSummary ?? "Not selected";
            ApiBindingReady = apiBindingReady;
            EnvironmentReady = environmentReady;
            EnvironmentSummary = environmentSummary ?? "Not resolved";
            BuildInput = buildInput ?? "Not configured";
            IdentifiersReady = identifiersReady;
            AuthenticationReady = authenticationReady;
            AuthenticationSummary = authenticationSummary ?? "Unauthenticated";
            IsPlaying = isPlaying;
            CommandRouteReady = commandRouteReady;
            AutoLoad = autoLoad;
        }

        internal bool HasContext { get; }
        internal string ContextSummary { get; }
        internal bool ApiBindingReady { get; }
        internal bool EnvironmentReady { get; }
        internal string EnvironmentSummary { get; }
        internal string BuildInput { get; }
        internal bool IdentifiersReady { get; }
        internal bool AuthenticationReady { get; }
        internal string AuthenticationSummary { get; }
        internal bool IsPlaying { get; }
        internal bool CommandRouteReady { get; }
        internal bool AutoLoad { get; }
    }
}