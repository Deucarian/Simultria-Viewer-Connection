#if UNITY_EDITOR
namespace Deucarian.SimultriaViewerIntegration
{
    internal interface ISimultriaViewerEditorProfileProvider
    {
        bool TryResolve(
            out SimultriaViewerDevelopmentContext profile,
            out string source,
            out string error);
    }

    internal static class SimultriaViewerEditorProfileProvider
    {
        private static ISimultriaViewerEditorProfileProvider provider;

        internal static void Register(
            ISimultriaViewerEditorProfileProvider value)
        {
            provider = value;
        }

        internal static bool TryResolve(
            out SimultriaViewerDevelopmentContext profile,
            out string source,
            out string error)
        {
            if (provider == null)
            {
                profile = null;
                source = "Editor override";
                error = "The Simultria Viewer Development Editor override " +
                        "provider is unavailable.";
                return false;
            }

            return provider.TryResolve(out profile, out source, out error);
        }
    }
}
#endif
