using UnityEditor;

namespace Deucarian.SimultriaViewerConnection.Editor
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
            out SimultriaViewerDevelopmentProfile profile,
            out string source,
            out string error)
        {
            return SimultriaViewerDevelopmentProfileSelector.TryResolve(
                out profile,
                out source,
                out error);
        }
    }
}
