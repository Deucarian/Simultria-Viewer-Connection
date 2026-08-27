using UnityEditor;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    [InitializeOnLoad]
    internal sealed class SimultriaViewerEditorProfileProviderRegistration :
        ISimultriaViewerEditorProfileProvider
    {
        static SimultriaViewerEditorProfileProviderRegistration()
        {
            SimultriaViewerEditorProfileProvider.Register(
                new SimultriaViewerEditorProfileProviderRegistration());
        }

        public bool TryResolve(
            out SimultriaViewerDevelopmentContext profile,
            out string source,
            out string error)
        {
            return SimultriaViewerDevelopmentContextSelector.TryResolve(
                out profile,
                out source,
                out error);
        }
    }
}
