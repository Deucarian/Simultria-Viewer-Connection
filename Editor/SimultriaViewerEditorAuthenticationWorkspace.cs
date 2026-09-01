using System;
using System.Threading;
using Deucarian.Authentication;
using Deucarian.Authentication.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    [InitializeOnLoad]
    internal static partial class SimultriaViewerEditorAuthenticationHost
    {
        internal const string LegacyReportViewerTargetId = "report-viewer";
        private const double PollIntervalSeconds = 1d;
        private static readonly SimultriaViewerEditorAuthenticationLease Lease;
        private static double nextPollAt;
        private static bool refreshScheduled;
        private static bool shuttingDown;
        private static int testSuspensionCount;
        private static CancellationTokenSource environmentResolutionCancellation;
        private static SimultriaViewerDevelopmentContext environmentResolutionProfile;
        private static SimultriaViewerEnvironmentResolution environmentResolution;
        private static string environmentResolutionKey;
        private static bool environmentResolutionInFlight;

        internal static event Action EnvironmentResolutionChanged;

        static SimultriaViewerEditorAuthenticationHost()
        {
            Lease = new SimultriaViewerEditorAuthenticationLease(
                ResolveConfiguration,
                RebindRememberedTokenOwner);
            AuthenticationTargetRegistry.RegistrationsChanged +=
                OnRegistrationsChanged;
            AuthenticationTargetRegistry.TargetsChanged +=
                OnTargetsChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.quitting += Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            RequestRefresh();
        }

        internal static void RequestRefresh(
            bool invalidateEnvironmentResolution = true)
        {
            if (shuttingDown)
            {
                return;
            }

            if (invalidateEnvironmentResolution)
            {
                InvalidateEnvironmentResolution();
            }

            Lease.Invalidate();
            EditorApplication.delayCall -= RunScheduledRefresh;
            EditorApplication.delayCall += RunScheduledRefresh;
            refreshScheduled = true;
        }

        internal static IDisposable SuspendForTests()
        {
            testSuspensionCount++;
            Lease.Reconcile(suspendForPlayMode: true);
            return new TestSuspension();
        }

        private static SimultriaViewerEditorAuthenticationConfiguration
            ResolveConfiguration()
        {
            if (SimultriaViewerDevelopmentContextSelector.TryResolve(
                    out SimultriaViewerDevelopmentContext profile,
                    out _,
                    out _))
            {
                if (!TryGetEffectiveEnvironment(
                        profile,
                        out Deucarian.API.Models.ApiEnvironmentId environmentId,
                        out _,
                        out _))
                {
                    return null;
                }

                return new SimultriaViewerEditorAuthenticationConfiguration(
                    profile.ConnectionSettingsReference,
                    environmentId);
            }

            return null;
        }

        private static void OnRegistrationsChanged()
        {
            RequestRefresh(invalidateEnvironmentResolution: false);
        }

        private static void OnTargetsChanged()
        {
            if (!AuthenticationTargetRegistry.TryGet(
                    SimultriaViewerConnectionAuthentication.DefaultTargetId,
                    out AuthenticationTarget target))
            {
                return;
            }

            SimultriaViewerEditorAuthenticationConfiguration configuration =
                ResolveConfiguration();
            if (configuration == null ||
                !TryValidateTarget(target, configuration))
            {
                return;
            }

            AuthenticationEditorSessionHandoff.Capture(
                SimultriaViewerEditorAuthenticationBinding.Create(
                    configuration),
                target.Session);
        }

        private static bool TryValidateTarget(
            AuthenticationTarget target,
            SimultriaViewerEditorAuthenticationConfiguration configuration)
        {
            return SimultriaViewerConnectionAuthentication.TryValidateTarget(
                target,
                configuration.Settings,
                configuration.EnvironmentId,
                out _);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode ||
                state == PlayModeStateChange.EnteredPlayMode ||
                state == PlayModeStateChange.ExitingPlayMode)
            {
                Lease.Reconcile(suspendForPlayMode: true);
                return;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                RequestRefresh();
            }
        }

        private static void OnProjectChanged()
        {
            RequestRefresh();
        }

        private static void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < nextPollAt)
            {
                return;
            }

            nextPollAt =
                EditorApplication.timeSinceStartup + PollIntervalSeconds;
            ReconcileNow();
        }

        private static void RunScheduledRefresh()
        {
            refreshScheduled = false;
            ReconcileNow();
        }

        private static void ReconcileNow()
        {
            if (shuttingDown)
            {
                return;
            }

            bool suspended = testSuspensionCount > 0 ||
                             EditorApplication.isPlayingOrWillChangePlaymode;
            if (suspended)
            {
                Lease.Reconcile(suspendForPlayMode: true);
                return;
            }

            if (SimultriaViewerDevelopmentContextSelector.TryResolve(
                    out SimultriaViewerDevelopmentContext profile,
                    out _,
                    out _) &&
                profile.EnvironmentResolutionMode ==
                    SimultriaViewerEnvironmentResolutionMode
                        .AutomaticFromUnityBuildVersion &&
                !TryGetEffectiveEnvironment(
                    profile,
                    out _,
                    out _,
                    out _))
            {
                Lease.Reconcile(suspendForPlayMode: true);
                StartEnvironmentResolution(profile);
                return;
            }

            Lease.Reconcile(suspendForPlayMode: false);
        }

        private static void RebindRememberedTokenOwner(
            string expectedCurrentTargetId,
            string targetId)
        {
            AuthenticationRememberedTokenFacade.TryRebindOwner(
                expectedCurrentTargetId,
                targetId);
        }

        private static void Shutdown()
        {
            if (shuttingDown)
            {
                return;
            }

            shuttingDown = true;
            if (refreshScheduled)
            {
                EditorApplication.delayCall -= RunScheduledRefresh;
                refreshScheduled = false;
            }

            AuthenticationTargetRegistry.RegistrationsChanged -=
                OnRegistrationsChanged;
            AuthenticationTargetRegistry.TargetsChanged -=
                OnTargetsChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.quitting -= Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            InvalidateEnvironmentResolution();
            Lease.Dispose();
        }

    }
}
