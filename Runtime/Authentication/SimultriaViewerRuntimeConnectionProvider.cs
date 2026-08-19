using System;
using System.Threading;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using Deucarian.ViewerAuthentication;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection
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
        private readonly SimultriaApiProfile apiProfile;
        private readonly ApiEnvironmentId environmentId;
        private bool leased;

        public SimultriaViewerRuntimeConnectionProvider(
            SimultriaApiProfile profile,
            ApiEnvironmentId environment)
        {
            apiProfile = profile;
            environmentId = environment;
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

            ViewerAuthenticationSession session = null;
            IDisposable targetRegistration = null;
            SimultriaViewerRuntimeConnectionLifetime lifetime = null;
            try
            {
                if (apiProfile == null ||
                    !apiProfile.TryCreateComposition(
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

                session = ViewerAuthenticationSession.CreateTransient();
                IApiClient apiClient =
                    SimultriaViewerConnectionAuthentication
                        .CreateSessionApiClient(
                            session,
                            resolvedClient.BaseUrl);
                if (!SimultriaViewerConnectionAuthentication.TryRegister(
                        apiProfile,
                        environmentId,
                        session,
                        out targetRegistration,
                        out _,
                        out error,
                        apiClient))
                {
                    _ = session.ClearAsync(CancellationToken.None);
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
                    null,
                    lifetime);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                lifetime?.Dispose();
                targetRegistration?.Dispose();
                if (session != null)
                {
                    _ = session.ClearAsync(CancellationToken.None);
                }

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

    }

    internal sealed class SimultriaViewerRuntimeConnectionLifetime :
        IDisposable
    {
        private IDisposable registration;
        private ViewerAuthenticationSession session;
        private Action release;

        internal SimultriaViewerRuntimeConnectionLifetime(
            IDisposable registration,
            ViewerAuthenticationSession session,
            Action release)
        {
            this.registration = registration;
            this.session = session;
            this.release = release;
        }

        internal ViewerAuthenticationSession Session => session;

        public void Dispose()
        {
            IDisposable currentRegistration = registration;
            ViewerAuthenticationSession currentSession = session;
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
                try
                {
                    if (currentSession != null)
                    {
                        _ = currentSession.ClearAsync(CancellationToken.None);
                    }
                }
                finally
                {
                    currentRelease?.Invoke();
                }
            }
        }
    }

    internal static class SimultriaViewerRuntimeConnectionBootstrap
    {
        private static IDisposable providerRegistration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterProvider()
        {
            providerRegistration?.Dispose();
            providerRegistration =
                ViewerRuntimeConnectionProviderRegistry.Register(
                    new SimultriaViewerRuntimeConnectionProvider(
                        SimultriaApiProfileDefaults.Load(),
                        SimultriaEnvironmentIds.Development));
        }
    }
}
