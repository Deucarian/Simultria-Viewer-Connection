using NUnit.Framework;
using Deucarian.SimultriaViewerConnection.Editor;

namespace Deucarian.SimultriaViewerConnection.Tests
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
        public void WebGlExportIsExplicitAndCredentialFreeByContract()
        {
            Assert.That(
                SimultriaViewerWebGlDevelopmentExporter.ExportAssetPath,
                Is.EqualTo("Assets/StreamingAssets/simultria-viewer-context.json"));
            Assert.That(
                SimultriaViewerWebGlDevelopmentExporter.ExportAssetPath,
                Does.Not.Contain("token"));
        }
    }
}
