using Deucarian.Simultria.API.Configuration;
using Deucarian.SimultriaViewerIntegration.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Deucarian.SimultriaViewerIntegration.Tests
{
    public sealed class SimultriaViewerBuildProfileSceneProcessorTests
    {
        private Scene scene;
        private SimultriaViewerBuildConnectionGate gate;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Build identity test");
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            gate = root.AddComponent<SimultriaViewerBuildConnectionGate>();
        }

        [TearDown]
        public void TearDown() => EditorSceneManager.ClosePreviewScene(scene);

        [TestCase(false)]
        [TestCase(true)]
        public void StampUsesActualBuildEnvironmentForInactiveGates(bool development)
        {
            SimultriaViewerBuildProfileSceneProcessor.StampScene(scene, development);
            Assert.That(gate.BuildProfileEnvironmentId, Is.EqualTo(development
                ? SimultriaEnvironmentIds.Development : SimultriaEnvironmentIds.Production));
        }

        [Test]
        public void PlayModeProcessingDoesNotStampOrReplaceEditorSceneState()
        {
            var processor = new SimultriaViewerBuildProfileSceneProcessor();
            processor.OnProcessScene(scene, null);
            Assert.That(gate.BuildProfileEnvironmentId.IsEmpty, Is.True);
            gate.CaptureBuildProfileEnvironment(true);
            processor.OnProcessScene(scene, null);
            Assert.That(gate.BuildProfileEnvironmentId, Is.EqualTo(SimultriaEnvironmentIds.Development));
        }

        [Test]
        public void ConsecutiveBuildsReplaceStampWithoutChangingConfiguration()
        {
            SimultriaViewerBuildProfileSceneProcessor.StampScene(scene, true);
            SimultriaViewerBuildProfileSceneProcessor.StampScene(scene, false);
            Assert.That(gate.BuildProfileEnvironmentId, Is.EqualTo(SimultriaEnvironmentIds.Production));
            Assert.That(gate.BuildConfiguration, Is.Null);
        }
    }
}
