using System;
using Deucarian.API.Configuration;
using Deucarian.Authentication;
using Deucarian.Authentication.Editor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    internal sealed class SimultriaViewerEditorAuthenticationConfiguration
    {
        internal SimultriaViewerEditorAuthenticationConfiguration(
            ApiConnectionSettings settings,
            Deucarian.API.Models.ApiEnvironmentId environmentId)
        {
            Settings = settings;
            EnvironmentId = environmentId;
        }

        internal ApiConnectionSettings Settings { get; }

        internal ScriptableObject ProfileReference => Settings;

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
            AuthenticationSession,
            IDisposable> registerTarget;
        private IDisposable registration;
        private AuthenticationSession session;
        private string attemptedBinding;
        private bool attemptRecorded;
        private bool invalidated = true;
        private bool reconciling;

        internal SimultriaViewerEditorAuthenticationLease(
            Func<SimultriaViewerEditorAuthenticationConfiguration>
                resolveConfiguration,
            Action<string, string> rebindRememberedTokenOwner = null,
            Func<
                SimultriaViewerEditorAuthenticationConfiguration,
                AuthenticationSession,
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
                string binding =
                    SimultriaViewerEditorAuthenticationBinding.Create(
                        configuration);
                if (configuration == null || profileReference == null ||
                    string.IsNullOrWhiteSpace(binding))
                {
                    ClearChangedHandoff(string.Empty);
                    Release(invalidate: false);
                    ClearAttempt();
                    return;
                }

                bool sameAttempt = IsSameAttempt(binding);
                if (!invalidated && sameAttempt)
                {
                    if (registration == null || HasOwnTarget())
                    {
                        return;
                    }

                    Release(invalidate: true);
                }

                ClearChangedHandoff(binding);
                Release(invalidate: false);
                if (AuthenticationTargetRegistry.Targets.Count != 0)
                {
                    invalidated = true;
                    return;
                }

                AuthenticationSession candidate =
                    AuthenticationSession.CreateTransient();
                AuthenticationEditorSessionHandoff.TryApply(
                    binding,
                    candidate);
                IDisposable candidateRegistration =
                    registerTarget(configuration, candidate);
                if (candidateRegistration == null)
                {
                    invalidated = true;
                    return;
                }

                session = candidate;
                registration = candidateRegistration;
                RecordAttempt(binding);
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

            var targets = AuthenticationTargetRegistry.Targets;
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
            var targets = AuthenticationTargetRegistry.Targets;
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
            AuthenticationSession session)
        {
            return SimultriaViewerConnectionAuthentication.TryRegister(
                    configuration.Settings,
                    configuration.EnvironmentId,
                    session,
                    out IDisposable registration,
                    out _,
                    out _)
                ? registration
                : null;
        }

        private bool IsSameAttempt(string binding)
        {
            return attemptRecorded &&
                   string.Equals(
                       attemptedBinding,
                       binding,
                       StringComparison.Ordinal);
        }

        private void RecordAttempt(string binding)
        {
            attemptedBinding = binding;
            attemptRecorded = true;
            invalidated = false;
        }

        private void ClearChangedHandoff(string binding)
        {
            if (attemptRecorded &&
                !string.Equals(
                    attemptedBinding,
                    binding,
                    StringComparison.Ordinal))
            {
                AuthenticationEditorSessionHandoff.Clear(attemptedBinding);
            }
        }

        private void ClearAttempt()
        {
            attemptedBinding = null;
            attemptRecorded = false;
            invalidated = true;
        }

        private void Release(bool invalidate)
        {
            IDisposable previousRegistration = registration;
            registration = null;
            session = null;
            if (invalidate)
            {
                invalidated = true;
            }

            previousRegistration?.Dispose();
        }
    }
}
