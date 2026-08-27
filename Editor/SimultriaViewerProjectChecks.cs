using System;
using System.Collections.Generic;
using Deucarian.API.Configuration;
using Deucarian.API.Editor;
using Deucarian.Authentication.Editor;
using Deucarian.Editor;
using Deucarian.Simultria.API.Configuration;
using UnityEditor;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    [InitializeOnLoad]
    internal static class SimultriaViewerProjectChecksRegistration
    {
        private static readonly IDisposable Registration;

        static SimultriaViewerProjectChecksRegistration()
        {
            Registration = DeucarianProjectValidationRegistry.Register(
                new SimultriaViewerProjectChecks());
        }
    }

    internal sealed class SimultriaViewerProjectChecks :
        IDeucarianProjectCheckProvider
    {
        public string Id => "com.deucarian.simultria-viewer-integration";

        public void Evaluate(ICollection<DeucarianProjectIssue> issues)
        {
            if (!AuthenticationSecureSessionStore
                    .IsPlatformProtectionAvailable)
            {
                issues.Add(new DeucarianProjectIssue(
                    "DEU-AUTH-001",
                    DeucarianProjectIssueSeverity.Error,
                    Id,
                    "No secure local credential-store implementation is " +
                    "available for this Editor platform.",
                    openSetup: OpenAuthentication));
            }

            bool hasBinding = ApiConnectionProjectSettings.instance.TryResolve(
                SimultriaServiceIds.ApiV2,
                out ApiConnectionSettings boundSettings,
                out string bindingError);
            if (!hasBinding)
            {
                string code = bindingError != null &&
                    bindingError.StartsWith("DEU-API-002",
                        StringComparison.Ordinal)
                    ? "DEU-API-002"
                    : bindingError != null && bindingError.StartsWith(
                        "DEU-API-005",
                        StringComparison.Ordinal)
                        ? "DEU-API-005"
                        : "DEU-API-001";
                issues.Add(new DeucarianProjectIssue(
                    code,
                    DeucarianProjectIssueSeverity.Error,
                    Id,
                    bindingError ?? "The Simultria API connection is invalid.",
                    "ProjectSettings/DeucarianApiConnections.asset",
                    openSetup: OpenApiConnections));
            }

            if (!SimultriaViewerDevelopmentContextSelector.TryResolve(
                    out SimultriaViewerDevelopmentContext context,
                    out string source,
                    out string contextError))
            {
                issues.Add(new DeucarianProjectIssue(
                    "DEU-VIEW-001",
                    DeucarianProjectIssueSeverity.Error,
                    Id,
                    contextError ??
                        "A Simultria viewer development context is required.",
                    source,
                    openSetup: SimultriaViewerDevelopmentWindow.Open));
                return;
            }

            Action selectContext = () =>
            {
                Selection.activeObject = context;
                EditorGUIUtility.PingObject(context);
            };
            if (context.ConnectionSettingsReference == null ||
                (hasBinding && !ReferenceEquals(
                    context.ConnectionSettingsReference,
                    boundSettings)))
            {
                issues.Add(new DeucarianProjectIssue(
                    "DEU-API-005",
                    DeucarianProjectIssueSeverity.Error,
                    Id,
                    "The development context must reference the one bound " +
                    "Simultria API connection settings asset.",
                    AssetDatabase.GetAssetPath(context),
                    select: selectContext,
                    openSetup: OpenApiConnections));
            }

            if (context.EnvironmentId.IsEmpty)
            {
                issues.Add(new DeucarianProjectIssue(
                    "DEU-API-003",
                    DeucarianProjectIssueSeverity.Error,
                    Id,
                    "Select an explicit environment; blank never means " +
                    "Development.",
                    AssetDatabase.GetAssetPath(context),
                    select: selectContext,
                    openSetup: SimultriaViewerDevelopmentWindow.Open));
            }
            else if (context.EnvironmentResolutionMode ==
                     SimultriaViewerEnvironmentResolutionMode.Manual &&
                     !context.TryResolveEnvironment(out _, out string error))
            {
                issues.Add(new DeucarianProjectIssue(
                    "DEU-API-003",
                    DeucarianProjectIssueSeverity.Error,
                    Id,
                    error,
                    AssetDatabase.GetAssetPath(context),
                    select: selectContext,
                    openSetup: OpenApiConnections));
            }

            if (context.ProjectId <= 0 || context.ModelId <= 0)
            {
                issues.Add(new DeucarianProjectIssue(
                    "DEU-VIEW-002",
                    DeucarianProjectIssueSeverity.Error,
                    Id,
                    "Project ID and model ID must be positive before viewer " +
                    "development can start.",
                    AssetDatabase.GetAssetPath(context),
                    select: selectContext,
                    openSetup: SimultriaViewerDevelopmentWindow.Open));
            }
        }

        private static void OpenApiConnections()
        {
            ApiConnectionsWindow.Open();
        }

        private static void OpenAuthentication()
        {
            AuthenticationWindow.Open();
        }
    }
}
