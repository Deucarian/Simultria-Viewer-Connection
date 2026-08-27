using System;
using System.Threading;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Deucarian.Authentication;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration
{
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
        {
            connectionSettings = profile;
            environmentId = environment;
            initialSession = session;
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
            initialSession = null;
            IDisposable targetRegistration = null;
            SimultriaViewerRuntimeConnectionLifetime lifetime = null;
            try
            {
                if (!TryCreateComposition(
                        out ApiComposition composition,
                        out error) ||
                    !composition.TryResolveClient(
                        environmentId,
                        SimultriaClientIds.Primary,
                        out ApiResolvedClient resolvedClient,
                        out error))
                {
                    ReleaseLease();
                    return false;
                }

                session = session ?? AuthenticationSession.CreateTransient();

                IApiClient apiClient =
                    SimultriaViewerConnectionAuthentication
                        .CreateSessionApiClient(
                            session,
                            resolvedClient.BaseUrl);
                if (!TryRegister(
                        session,
                        apiClient,
                        out targetRegistration,
                        out error))
                {
                    ReleaseLease();
                    return false;
                }

                lifetime = new SimultriaViewerRuntimeConnectionLifetime(
                    targetRegistration,
                    session,
                    ReleaseLease);
                targetRegistration = null;
                session = null;
                connection = new ViewerRuntimeConnection(
                    SimultriaViewerConnectionAuthentication.DefaultTargetId,
                    lifetime.Session,
                    apiClient,
                    resolvedClient.BaseUrl,
                    SimultriaViewerAuthenticatedOriginResolver.Resolve(
                        composition,
                        environmentId),
                    lifetime);
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
            IAuthenticationSession session,
            IApiClient apiClient,
            out IDisposable registration,
            out string error)
        {
            return SimultriaViewerConnectionAuthentication.TryRegister(
                connectionSettings,
                environmentId,
                session,
                out registration,
                out _,
                out error,
                apiClient);
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
