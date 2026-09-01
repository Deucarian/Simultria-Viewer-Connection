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
                bool sessionSourceInvoked = false;

                IViewerRuntimeConnectionProvider provider;
                string error;
                using (SimultriaViewerRuntimeConnectionProviderFactory
                    .OverrideInitialSessionFactoryForTests(
                        (candidateSettings, candidateEnvironment) =>
                        {
                            sessionSourceInvoked = true;
                            Assert.That(
                                candidateSettings,
                                Is.SameAs(connection));
                            Assert.That(
                                candidateEnvironment,
                                Is.EqualTo(environment));
                            return null;
                        }))
                {
                    Assert.That(
                        SimultriaViewerDevelopmentAutoLoader
                            .TryCreateRuntimeConnectionProvider(
                                profile,
                                environment,
                                out provider,
                                out error),
                        Is.True,
                        error);
                }

                Assert.That(sessionSourceInvoked, Is.True);
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
