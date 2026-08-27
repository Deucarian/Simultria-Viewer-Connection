using NUnit.Framework;
using Deucarian.API.Configuration;
using Deucarian.API.Models;
using Deucarian.SimultriaViewerIntegration.Editor;
using Deucarian.Authentication;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class SimultriaViewerConnectionEditorContractTests
    {
        [Test]
        public void SettingsUseSharedAndLocalProjectScopes()
        {
            Assert.That(
                SimultriaViewerConnectionProjectSettings.SettingsPath,
                Does.StartWith("ProjectSettings/"));
            Assert.That(
                SimultriaViewerConnectionUserSettings.SettingsPath,
                Does.StartWith("UserSettings/"));
        }

        [Test]
        public void NewProjectSettingsKeepDevelopmentAutoLoadOptIn()
        {
            Assert.That(
                SimultriaViewerConnectionProjectSettings
                    .DefaultAutoLoadInPlayMode,
                Is.False);
        }

        [Test]
        public void WebGlExportIsExplicitAndCredentialFreeByContract()
        {
            Assert.That(
                SimultriaViewerWebGlDevelopmentExporter.ExportAssetPath,
                Is.EqualTo("Assets/StreamingAssets/simultria-viewer-context.json"));
            Assert.That(
                SimultriaViewerWebGlDevelopmentExporter.ExportAssetPath,
                Does.Not.Contain("token"));
        }

        [Test]
        public void PlayModeAutoLoadCreatesTheSharedRuntimeProvider()
        {
            SimultriaViewerDevelopmentContext profile =
                ScriptableObject.CreateInstance<
                    SimultriaViewerDevelopmentContext>();
            ApiConnectionSettings connection =
                ScriptableObject.CreateInstance<ApiConnectionSettings>();
            try
            {
                profile.ConnectionSettingsReference = connection;
                var environment = new ApiEnvironmentId(
                    "simultria.development");

                Assert.That(
                    SimultriaViewerDevelopmentAutoLoader
                        .TryCreateRuntimeConnectionProvider(
                            profile,
                            environment,
                            out IViewerRuntimeConnectionProvider provider,
                            out string error),
                    Is.True,
                    error);
                Assert.That(
                    provider,
                    Is.TypeOf<SimultriaViewerRuntimeConnectionProvider>());
            }
            finally
            {
                Object.DestroyImmediate(connection);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void AuthenticationWaitRemainsRecognizableForInteractiveSignIn()
        {
            Assert.That(
                SimultriaViewerDevelopmentCommandService
                    .IsWaitingForAuthentication(
                        SimultriaViewerDevelopmentCommandService
                            .AuthenticationRequiredMessage),
                Is.True);
            Assert.That(
                SimultriaViewerDevelopmentCommandService
                    .IsWaitingForAuthentication(
                        "Waiting for the running viewer."),
                Is.False);
        }
    }
}
