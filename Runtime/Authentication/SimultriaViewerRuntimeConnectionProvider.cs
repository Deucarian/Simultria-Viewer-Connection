using System;
using System.Collections.Generic;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Deucarian.Authentication;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration
{
    internal static class SimultriaViewerRuntimeConnectionProviderFactory
    {
        private static Func<ApiConnectionSettings, ApiEnvironmentId,
            SimultriaViewerInitialSession> initialSessionFactory;

        internal static SimultriaViewerRuntimeConnectionProvider Create(
            ApiConnectionSettings settings,
            ApiEnvironmentId environmentId)
        {
            SimultriaViewerInitialSession initialSession = null;
            try
            {
                initialSession = initialSessionFactory?.Invoke(
                    settings,
                    environmentId);
            }
            catch (Exception)
            {
                initialSession = null;
            }

            return new SimultriaViewerRuntimeConnectionProvider(
                settings,
                environmentId,
                initialSession?.Session,
                initialSession?.CompositionFingerprint);
        }

        internal static void ConfigureInitialSessionFactory(
            Func<ApiConnectionSettings, ApiEnvironmentId,
                SimultriaViewerInitialSession> factory)
        {
            initialSessionFactory = factory;
        }

        internal static IDisposable OverrideInitialSessionFactoryForTests(
            Func<ApiConnectionSettings, ApiEnvironmentId,
                SimultriaViewerInitialSession> factory)
        {
            Func<ApiConnectionSettings, ApiEnvironmentId,
                SimultriaViewerInitialSession> previous =
                initialSessionFactory;
            initialSessionFactory = factory;
            return new InitialSessionFactoryScope(previous);
        }

        private sealed class InitialSessionFactoryScope : IDisposable
        {
            private Func<ApiConnectionSettings, ApiEnvironmentId,
                SimultriaViewerInitialSession> previous;

            internal InitialSessionFactoryScope(
                Func<ApiConnectionSettings, ApiEnvironmentId,
                    SimultriaViewerInitialSession> previousFactory)
            {
                previous = previousFactory;
            }

            public void Dispose()
            {
                initialSessionFactory = previous;
                previous = null;
            }
        }
    }

    internal sealed class SimultriaViewerInitialSession
    {
        private SimultriaViewerInitialSession(
            AuthenticationSession session,
            string compositionFingerprint)
        {
            Session = session;
            CompositionFingerprint = compositionFingerprint;
        }

        internal AuthenticationSession Session { get; }

        internal string CompositionFingerprint { get; }

        internal static SimultriaViewerInitialSession Capture(
            ApiConnectionSettings settings,
            ApiEnvironmentId environmentId,
            AuthenticationSession session)
        {
            return session != null &&
                   SimultriaViewerConnectionCompositionFingerprint.TryCreate(
                       settings,
                       environmentId,
                       out string fingerprint)
                ? new SimultriaViewerInitialSession(session, fingerprint)
                : null;
        }

        internal static SimultriaViewerInitialSession Create(
            AuthenticationSession session,
            string compositionFingerprint)
        {
            return session == null ||
                   string.IsNullOrWhiteSpace(compositionFingerprint)
                ? null
                : new SimultriaViewerInitialSession(
                    session,
                    compositionFingerprint.Trim());
        }
    }

    /// <summary>
    /// Supplies the optional generic viewer runtime with one Simultria-backed
    /// authentication session and its matching API composition.
    /// </summary>
    public sealed class SimultriaViewerRuntimeConnectionProvider :
        IViewerRuntimeConnectionProvider
    {
        public const string ProviderId = "simultria.runtime-connection";

        private readonly object gate = new object();
        private readonly ApiConnectionSettings connectionSettings;
        private readonly ApiEnvironmentId environmentId;
        private AuthenticationSession initialSession;
        private string initialSessionFingerprint;
        private bool leased;

        /// <summary>
        /// Creates a provider from project-owned connection settings.
        /// </summary>
        public SimultriaViewerRuntimeConnectionProvider(
            ApiConnectionSettings profile,
            ApiEnvironmentId environment)
            : this(profile, environment, null)
        {
        }

        internal SimultriaViewerRuntimeConnectionProvider(
            ApiConnectionSettings profile,
            ApiEnvironmentId environment,
            AuthenticationSession session)
            : this(
                profile,
                environment,
                session,
                SimultriaViewerInitialSession.Capture(
                    profile,
                    environment,
                    session)?.CompositionFingerprint)
        {
        }

        internal SimultriaViewerRuntimeConnectionProvider(
            ApiConnectionSettings profile,
            ApiEnvironmentId environment,
            AuthenticationSession session,
            string compositionFingerprint)
        {
            connectionSettings = profile;
            environmentId = environment;
            initialSession = session;
            initialSessionFingerprint = compositionFingerprint ?? string.Empty;
        }

        public string Id => ProviderId;

        public bool TryCreate(
            out ViewerRuntimeConnection connection,
            out string error)
        {
            connection = null;
            error = null;
            lock (gate)
            {
                if (leased)
                {
                    error =
                        "The Simultria runtime connection is already in use.";
                    return false;
                }

                leased = true;
            }

            AuthenticationSession session = initialSession;
            IDisposable targetRegistration = null;
            SimultriaViewerRuntimeConnectionLifetime lifetime = null;
            try
            {
                if (!TryCreateComposition(
                        out ApiComposition composition,
                        out error) ||
                    !SimultriaViewerConnectionCompositionFingerprint.TryCreate(
                        connectionSettings,
                        composition,
                        environmentId,
                        out string compositionFingerprint) ||
                    !composition.TryResolveClient(
                        environmentId,
                        SimultriaClientIds.Primary,
                        out ApiResolvedClient resolvedClient,
                        out error))
                {
                    ReleaseLease();
                    return false;
                }

                if (session != null && !string.Equals(
                        initialSessionFingerprint,
                        compositionFingerprint,
                        StringComparison.Ordinal))
                {
                    DiscardInitialSession(session);
                    session = null;
                }

                IReadOnlyCollection<string> authenticatedOrigins =
                    SimultriaViewerAuthenticatedOriginResolver.Resolve(
                        composition,
                        environmentId);
                session = session ?? AuthenticationSession.CreateTransient();

                IApiClient apiClient =
                    SimultriaViewerConnectionAuthentication
                        .CreateSessionApiClient(
                            session,
                            resolvedClient.BaseUrl);
                if (!TryRegister(
                        composition,
                        session,
                        apiClient,
                        out targetRegistration,
                        out error))
                {
                    ReleaseLease();
                    return false;
                }

                if (!SimultriaViewerConnectionCompositionFingerprint.TryCreate(
                        connectionSettings,
                        environmentId,
                        out string currentFingerprint) ||
                    !string.Equals(
                        compositionFingerprint,
                        currentFingerprint,
                        StringComparison.Ordinal))
                {
                    targetRegistration.Dispose();
                    targetRegistration = null;
                    DiscardInitialSession(session);
                    ReleaseLease();
                    error = "The Simultria API configuration changed while " +
                            "the runtime connection was being created.";
                    return false;
                }

                lifetime = new SimultriaViewerRuntimeConnectionLifetime(
                    targetRegistration,
                    session,
                    ReleaseLease);
                targetRegistration = null;
                connection = new ViewerRuntimeConnection(
                    SimultriaViewerConnectionAuthentication.DefaultTargetId,
                    lifetime.Session,
                    apiClient,
                    resolvedClient.BaseUrl,
                    authenticatedOrigins,
                    lifetime);
                if (ReferenceEquals(initialSession, session))
                {
                    initialSession = null;
                    initialSessionFingerprint = string.Empty;
                }

                session = null;
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                lifetime?.Dispose();
                targetRegistration?.Dispose();
                if (lifetime == null)
                {
                    ReleaseLease();
                }
                connection = null;
                error = "The Simultria runtime connection could not be " +
                        "created (" + exception.GetType().Name + ").";
                return false;
            }
        }

        private void ReleaseLease()
        {
            lock (gate)
            {
                leased = false;
            }
        }

        private bool TryCreateComposition(
            out ApiComposition composition,
            out string error)
        {
            return SimultriaApiConnectionSettingsAdapter.TryCreateComposition(
                connectionSettings,
                out composition,
                out error);
        }

        private bool TryRegister(
            ApiComposition composition,
            IAuthenticationSession session,
            IApiClient apiClient,
            out IDisposable registration,
            out string error)
        {
            return SimultriaViewerConnectionAuthentication.TryRegister(
                connectionSettings,
                composition,
                environmentId,
                SimultriaViewerConnectionAuthentication.DefaultTargetId,
                SimultriaViewerConnectionAuthentication.DefaultDisplayName,
                session,
                out registration,
                out _,
                out error,
                apiClient);
        }

        private void DiscardInitialSession(AuthenticationSession session)
        {
            if (!ReferenceEquals(initialSession, session))
            {
                return;
            }

            initialSession = null;
            initialSessionFingerprint = string.Empty;
        }
    }

    internal sealed class SimultriaViewerRuntimeConnectionLifetime :
        IDisposable
    {
        private IDisposable registration;
        private AuthenticationSession session;
        private Action release;

        internal SimultriaViewerRuntimeConnectionLifetime(
            IDisposable registration,
            AuthenticationSession session,
            Action release)
        {
            this.registration = registration;
            this.session = session;
            this.release = release;
        }

        internal AuthenticationSession Session => session;

        public void Dispose()
        {
            IDisposable currentRegistration = registration;
            Action currentRelease = release;
            registration = null;
            session = null;
            release = null;
            try
            {
                currentRegistration?.Dispose();
            }
            finally
            {
                currentRelease?.Invoke();
            }
        }
    }

}
