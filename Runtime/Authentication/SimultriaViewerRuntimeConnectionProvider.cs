using System;
using System.Threading;
using Deucarian.API.Configuration;
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
        private readonly ApiConnectionProfile connectionProfile;
        private readonly SimultriaApiProfile legacyApiProfile;
        private readonly ApiEnvironmentId environmentId;
        private bool leased;

        /// <summary>
        /// Creates a provider from a project-owned generic connection
        /// profile. Deployment hosts remain authored in that profile.
        /// </summary>
        public SimultriaViewerRuntimeConnectionProvider(
            ApiConnectionProfile profile,
            ApiEnvironmentId environment)
        {
            connectionProfile = profile;
            environmentId = environment;
        }

        /// <summary>
        /// Legacy constructor retained for source compatibility. New
        /// integrations should use <see cref="ApiConnectionProfile"/>.
        /// </summary>
        public SimultriaViewerRuntimeConnectionProvider(
            SimultriaApiProfile profile,
            ApiEnvironmentId environment)
        {
            legacyApiProfile = profile;
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

                session = ViewerAuthenticationSession.CreateTransient();
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

        private bool TryCreateComposition(
            out ApiComposition composition,
            out string error)
        {
            if (connectionProfile != null)
            {
                return SimultriaApiConnectionProfileAdapter
                    .TryCreateComposition(
                        connectionProfile,
                        out composition,
                        out error);
            }

            if (legacyApiProfile != null)
            {
                return legacyApiProfile.TryCreateComposition(
                    out composition,
                    out error);
            }

            composition = null;
            error = "A Simultria API connection profile is required.";
            return false;
        }

        private bool TryRegister(
            IViewerAuthenticationSession session,
            IApiClient apiClient,
            out IDisposable registration,
            out string error)
        {
            if (connectionProfile != null)
            {
                return SimultriaViewerConnectionAuthentication.TryRegister(
                    connectionProfile,
                    environmentId,
                    session,
                    out registration,
                    out _,
                    out error,
                    apiClient);
            }

            return SimultriaViewerConnectionAuthentication.TryRegister(
                legacyApiProfile,
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
            TryRegisterDefaultProvider();
        }

        /// <summary>
        /// Registers the legacy package default only when it is actually
        /// configured. Blank package defaults leave the generic viewer
        /// registry empty so a project-owned composition can take over.
        /// </summary>
        internal static bool TryRegisterDefaultProvider()
        {
            providerRegistration?.Dispose();
            providerRegistration = null;
            try
            {
                SimultriaApiProfile profile =
                    SimultriaApiProfileDefaults.Load();
                if (profile == null ||
                    !profile.TryCreateComposition(
                        out ApiComposition composition,
                        out _) ||
                    !composition.TryResolveClient(
                        SimultriaEnvironmentIds.Development,
                        SimultriaClientIds.Primary,
                        out _,
                        out _))
                {
                    return false;
                }

                providerRegistration =
                    ViewerRuntimeConnectionProviderRegistry.Register(
                        new SimultriaViewerRuntimeConnectionProvider(
                            profile,
                            SimultriaEnvironmentIds.Development));
                return true;
            }
            catch (Exception)
            {
                providerRegistration?.Dispose();
                providerRegistration = null;
                return false;
            }
        }
    }
}
