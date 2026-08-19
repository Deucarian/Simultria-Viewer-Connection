using System;
using System.Threading;
using Deucarian.API.Configuration;
using Deucarian.Simultria.API.Configuration;
using Deucarian.ViewerAuthentication;
using Deucarian.ViewerAuthentication.Editor;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection.Editor
{
    internal sealed class SimultriaViewerEditorAuthenticationConfiguration
    {
        internal SimultriaViewerEditorAuthenticationConfiguration(
            ApiConnectionProfile connectionProfile,
            Deucarian.API.Models.ApiEnvironmentId environmentId)
        {
            ConnectionProfile = connectionProfile;
            EnvironmentId = environmentId;
        }

        internal SimultriaViewerEditorAuthenticationConfiguration(
            SimultriaApiProfile apiProfile,
            Deucarian.API.Models.ApiEnvironmentId environmentId)
        {
            ApiProfile = apiProfile;
            EnvironmentId = environmentId;
        }

        internal ApiConnectionProfile ConnectionProfile { get; }

        internal SimultriaApiProfile ApiProfile { get; }

        internal ScriptableObject ProfileReference =>
            ConnectionProfile != null
                ? (ScriptableObject)ConnectionProfile
                : ApiProfile;

        internal Deucarian.API.Models.ApiEnvironmentId EnvironmentId { get; }
    }

    /// <summary>
    /// Owns one private, transient authentication target while the editor is
    /// not playing and no real viewer target exists.
    /// </summary>
    internal sealed class SimultriaViewerEditorAuthenticationLease : IDisposable
    {
        private readonly Func<
            SimultriaViewerEditorAuthenticationConfiguration> resolveConfiguration;
        private readonly Action<string, string> rebindRememberedTokenOwner;
        private readonly Func<
            SimultriaViewerEditorAuthenticationConfiguration,
            ViewerAuthenticationSession,
            IDisposable> registerTarget;
        private IDisposable registration;
        private ViewerAuthenticationSession session;
        private ScriptableObject attemptedProfile;
        private string attemptedEnvironmentId;
        private bool attemptRecorded;
        private bool invalidated = true;
        private bool reconciling;

        internal SimultriaViewerEditorAuthenticationLease(
            Func<SimultriaViewerEditorAuthenticationConfiguration>
                resolveConfiguration,
            Action<string, string> rebindRememberedTokenOwner = null,
            Func<
                SimultriaViewerEditorAuthenticationConfiguration,
                ViewerAuthenticationSession,
                IDisposable> registerTarget = null)
        {
            this.resolveConfiguration = resolveConfiguration ??
                throw new ArgumentNullException(nameof(resolveConfiguration));
            this.rebindRememberedTokenOwner = rebindRememberedTokenOwner;
            this.registerTarget = registerTarget ?? RegisterTarget;
        }

        internal bool IsRegistered =>
            registration != null && HasOwnTarget();

        internal void Invalidate()
        {
            invalidated = true;
        }

        internal void Reconcile(bool suspendForPlayMode)
        {
            if (reconciling)
            {
                return;
            }

            reconciling = true;
            try
            {
                if (suspendForPlayMode || HasForeignTarget())
                {
                    Release(invalidate: true);
                    return;
                }

                SimultriaViewerEditorAuthenticationConfiguration configuration =
                    resolveConfiguration();
                ScriptableObject profileReference =
                    configuration?.ProfileReference;
                string environmentId = configuration?.EnvironmentId.Value;
                if (configuration == null || profileReference == null)
                {
                    Release(invalidate: false);
                    ClearAttempt();
                    return;
                }

                bool sameAttempt = IsSameAttempt(
                    profileReference,
                    environmentId);
                if (!invalidated && sameAttempt)
                {
                    if (registration == null || HasOwnTarget())
                    {
                        return;
                    }

                    Release(invalidate: true);
                }

                Release(invalidate: false);
                if (ViewerAuthenticationTargetRegistry.Targets.Count != 0)
                {
                    invalidated = true;
                    return;
                }

                ViewerAuthenticationSession candidate =
                    ViewerAuthenticationSession.CreateTransient();
                IDisposable candidateRegistration =
                    registerTarget(configuration, candidate);
                if (candidateRegistration == null)
                {
                    _ = candidate.ClearAsync(CancellationToken.None);
                    invalidated = true;
                    return;
                }

                session = candidate;
                registration = candidateRegistration;
                RecordAttempt(profileReference, environmentId);
                RebindRememberedTokenOwner(
                    SimultriaViewerEditorAuthenticationHost
                        .LegacyReportViewerTargetId,
                    SimultriaViewerConnectionAuthentication.DefaultTargetId);
            }
            finally
            {
                reconciling = false;
            }
        }

        public void Dispose()
        {
            Release(invalidate: true);
            ClearAttempt();
        }

        private bool HasOwnTarget()
        {
            if (session == null)
            {
                return false;
            }

            var targets = ViewerAuthenticationTargetRegistry.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                if (ReferenceEquals(targets[i].Session, session))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasForeignTarget()
        {
            var targets = ViewerAuthenticationTargetRegistry.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                if (session == null ||
                    !ReferenceEquals(targets[i].Session, session))
                {
                    return true;
                }
            }

            return false;
        }

        private void RebindRememberedTokenOwner(
            string expectedCurrentTargetId,
            string targetId)
        {
            if (!string.IsNullOrWhiteSpace(expectedCurrentTargetId) &&
                !string.IsNullOrWhiteSpace(targetId))
            {
                rebindRememberedTokenOwner?.Invoke(
                    expectedCurrentTargetId,
                    targetId);
            }
        }

        private static IDisposable RegisterTarget(
            SimultriaViewerEditorAuthenticationConfiguration configuration,
            ViewerAuthenticationSession session)
        {
            if (configuration.ConnectionProfile != null)
            {
                return SimultriaViewerConnectionAuthentication.TryRegister(
                        configuration.ConnectionProfile,
                        configuration.EnvironmentId,
                        session,
                        out IDisposable connectionRegistration,
                        out _,
                        out _)
                    ? connectionRegistration
                    : null;
            }

            return SimultriaViewerConnectionAuthentication.TryRegister(
                    configuration.ApiProfile,
                    configuration.EnvironmentId,
                    session,
                    out IDisposable registration,
                    out _,
                    out _)
                ? registration
                : null;
        }

        private bool IsSameAttempt(
            ScriptableObject profile,
            string environmentId)
        {
            return attemptRecorded &&
                   ReferenceEquals(attemptedProfile, profile) &&
                   string.Equals(
                       attemptedEnvironmentId,
                       environmentId,
                       StringComparison.Ordinal);
        }

        private void RecordAttempt(
            ScriptableObject profile,
            string environmentId)
        {
            attemptedProfile = profile;
            attemptedEnvironmentId = environmentId;
            attemptRecorded = true;
            invalidated = false;
        }

        private void ClearAttempt()
        {
            attemptedProfile = null;
            attemptedEnvironmentId = null;
            attemptRecorded = false;
            invalidated = true;
        }

        private void Release(bool invalidate)
        {
            IDisposable previousRegistration = registration;
            ViewerAuthenticationSession previousSession = session;
            registration = null;
            session = null;
            if (invalidate)
            {
                invalidated = true;
            }

            previousRegistration?.Dispose();
            if (previousSession != null)
            {
                _ = previousSession.ClearAsync(CancellationToken.None);
            }
        }
    }

    [InitializeOnLoad]
    internal static class SimultriaViewerEditorAuthenticationHost
    {
        internal const string LegacyReportViewerTargetId = "report-viewer";
        private const double PollIntervalSeconds = 1d;
        private static readonly SimultriaViewerEditorAuthenticationLease Lease;
        private static double nextPollAt;
        private static bool refreshScheduled;
        private static bool shuttingDown;
        private static int testSuspensionCount;

        static SimultriaViewerEditorAuthenticationHost()
        {
            Lease = new SimultriaViewerEditorAuthenticationLease(
                ResolveConfiguration,
                RebindRememberedTokenOwner);
            ViewerAuthenticationTargetRegistry.RegistrationsChanged +=
                OnRegistrationsChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.quitting += Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            RequestRefresh();
        }

        internal static void RequestRefresh()
        {
            if (shuttingDown)
            {
                return;
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
            if (SimultriaViewerDevelopmentProfileSelector.TryResolve(
                    out SimultriaViewerDevelopmentProfile profile,
                    out _,
                    out _))
            {
                if (profile.ConnectionProfileReference != null)
                {
                    return new
                        SimultriaViewerEditorAuthenticationConfiguration(
                            profile.ConnectionProfileReference,
                            profile.EnvironmentId);
                }

                return new SimultriaViewerEditorAuthenticationConfiguration(
                    profile.EffectiveApiProfile,
                    profile.EnvironmentId);
            }

            SimultriaApiProfile defaultProfile =
                SimultriaApiProfileDefaults.Load();
            return defaultProfile == null
                ? null
                : new SimultriaViewerEditorAuthenticationConfiguration(
                    defaultProfile,
                    SimultriaEnvironmentIds.Development);
        }

        private static void OnRegistrationsChanged()
        {
            RequestRefresh();
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

            Lease.Reconcile(
                testSuspensionCount > 0 ||
                EditorApplication.isPlayingOrWillChangePlaymode);
        }

        private static void RebindRememberedTokenOwner(
            string expectedCurrentTargetId,
            string targetId)
        {
            ViewerAuthenticationRememberedTokenFacade.TryRebindOwner(
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

            ViewerAuthenticationTargetRegistry.RegistrationsChanged -=
                OnRegistrationsChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.quitting -= Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
            Lease.Dispose();
        }

        private sealed class TestSuspension : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                testSuspensionCount = Math.Max(0, testSuspensionCount - 1);
                RequestRefresh();
            }
        }
    }
}
