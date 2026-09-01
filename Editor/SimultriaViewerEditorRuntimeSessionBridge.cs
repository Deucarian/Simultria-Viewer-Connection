using Deucarian.API.Configuration;
using Deucarian.API.Models;
using Deucarian.Authentication;
using Deucarian.Authentication.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    /// <summary>
    /// Installs the Editor-only session handoff before scene startup so both
    /// gate-owned and auto-loader-owned runtime providers receive the exact
    /// authenticated Editor session for their selected environment.
    /// </summary>
    [InitializeOnLoad]
    internal static class SimultriaViewerEditorRuntimeSessionBridge
    {
        static SimultriaViewerEditorRuntimeSessionBridge()
        {
            Install();
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Install()
        {
            SimultriaViewerRuntimeConnectionProviderFactory
                .ConfigureInitialSessionFactory(CreateInitialSession);
        }

        private static SimultriaViewerInitialSession CreateInitialSession(
            ApiConnectionSettings settings,
            ApiEnvironmentId environmentId)
        {
            if (!SimultriaViewerConnectionCompositionFingerprint.TryCreate(
                    settings,
                    environmentId,
                    out string fingerprint))
            {
                return null;
            }

            AuthenticationEditorSessionHandoff.TryCreateSession(
                SimultriaViewerEditorAuthenticationBinding.Create(
                    settings,
                    environmentId,
                    fingerprint),
                out AuthenticationSession session);
            return SimultriaViewerInitialSession.Create(
                session,
                fingerprint);
        }
    }
}
