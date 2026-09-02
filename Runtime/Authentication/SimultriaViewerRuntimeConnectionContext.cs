using System;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>
    /// Client-bearing companion to <see cref="SimultriaViewerRuntimeEnvironment"/>
    /// for the one active Simultria runtime connection lease. The context
    /// exposes no authentication session or bearer value and never creates a
    /// replacement API client.
    /// </summary>
    public sealed class SimultriaViewerRuntimeConnectionContext
    {
        private static readonly object Gate = new object();
        private static SimultriaViewerRuntimeConnectionContext current;

        private SimultriaViewerRuntimeConnectionContext(
            ApiEnvironmentId environmentId,
            ApiResolvedClient primaryClient,
            ApiComposition composition,
            IApiClient apiClient)
        {
            EnvironmentId = environmentId;
            PrimaryClient = primaryClient;
            Composition = composition;
            ApiClient = apiClient;
        }

        /// <summary>The exact environment selected for this lease.</summary>
        public ApiEnvironmentId EnvironmentId { get; }

        /// <summary>The primary client resolved by the lease composition.</summary>
        public ApiResolvedClient PrimaryClient { get; }

        /// <summary>The exact composition owned by the active lease.</summary>
        public ApiComposition Composition { get; }

        /// <summary>
        /// The exact authenticated client owned by the active lease. Consumers
        /// must not dispose or replace it.
        /// </summary>
        public IApiClient ApiClient { get; }

        /// <summary>
        /// Returns the context carried by the current runtime connection
        /// lease. This method never composes settings or creates a client.
        /// </summary>
        public static bool TryGetCurrent(
            out SimultriaViewerRuntimeConnectionContext context)
        {
            lock (Gate)
            {
                context = current;
                return context != null;
            }
        }

        internal static bool TryActivate(
            ApiEnvironmentId environmentId,
            ApiResolvedClient primaryClient,
            ApiComposition composition,
            IApiClient apiClient,
            out SimultriaViewerRuntimeConnectionContext context,
            out IDisposable registration,
            out string error)
        {
            context = null;
            registration = null;
            if (!TryValidate(
                    environmentId,
                    primaryClient,
                    composition,
                    apiClient,
                    out error))
            {
                return false;
            }

            var candidate = new SimultriaViewerRuntimeConnectionContext(
                environmentId,
                primaryClient,
                composition,
                apiClient);
            lock (Gate)
            {
                if (current != null)
                {
                    error = "A Simultria runtime connection context is " +
                            "already active.";
                    return false;
                }

                current = candidate;
            }

            context = candidate;
            registration = new Registration(candidate);
            error = string.Empty;
            return true;
        }

        private static bool TryValidate(
            ApiEnvironmentId environmentId,
            ApiResolvedClient primaryClient,
            ApiComposition composition,
            IApiClient apiClient,
            out string error)
        {
            if (environmentId.IsEmpty ||
                primaryClient == null ||
                composition == null ||
                apiClient == null ||
                primaryClient.EnvironmentId != environmentId ||
                primaryClient.ClientId != SimultriaClientIds.Primary)
            {
                error = "The Simultria runtime connection context is " +
                        "incomplete or inconsistent.";
                return false;
            }

            if (!composition.TryResolveClient(
                    environmentId,
                    SimultriaClientIds.Primary,
                    out ApiResolvedClient expectedClient,
                    out _) ||
                !HasSameResolvedIdentity(expectedClient, primaryClient))
            {
                error = "The Simultria runtime connection context does not " +
                        "match its API composition.";
                return false;
            }

            if (SimultriaViewerRuntimeEnvironment.TryGetCurrent(
                    out SimultriaViewerEnvironmentResolution runtime) &&
                runtime.EnvironmentId != environmentId)
            {
                error = "The Simultria runtime connection context does not " +
                        "match the active runtime environment.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool HasSameResolvedIdentity(
            ApiResolvedClient expected,
            ApiResolvedClient actual)
        {
            if (expected == null || actual == null ||
                expected.EnvironmentId != actual.EnvironmentId ||
                expected.ClientId != actual.ClientId ||
                !string.Equals(
                    expected.EnvironmentDisplayName,
                    actual.EnvironmentDisplayName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    expected.BaseUrl,
                    actual.BaseUrl,
                    StringComparison.Ordinal) ||
                !HasSameHeaders(
                    expected.DefaultHeaders,
                    actual.DefaultHeaders))
            {
                return false;
            }

            return HasSamePolicy(
                expected.RequestPolicy,
                actual.RequestPolicy);
        }

        private static bool HasSameHeaders(
            System.Collections.Generic.IReadOnlyDictionary<string, string>
                expected,
            System.Collections.Generic.IReadOnlyDictionary<string, string>
                actual)
        {
            if (expected == null || actual == null ||
                expected.Count != actual.Count)
            {
                return expected == null && actual == null;
            }

            foreach (System.Collections.Generic.KeyValuePair<string, string>
                     header in expected)
            {
                if (!actual.TryGetValue(header.Key, out string value) ||
                    !string.Equals(
                        header.Value,
                        value,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasSamePolicy(
            Deucarian.API.Configuration.ApiRequestPolicy expected,
            Deucarian.API.Configuration.ApiRequestPolicy actual)
        {
            if (expected == null || actual == null)
            {
                return expected == null && actual == null;
            }

            return expected.TimeoutSeconds == actual.TimeoutSeconds &&
                   expected.MaxRetryAttempts == actual.MaxRetryAttempts &&
                   expected.InitialRetryBackoffMilliseconds ==
                   actual.InitialRetryBackoffMilliseconds &&
                   expected.RetryBackoffMultiplier.Equals(
                       actual.RetryBackoffMultiplier) &&
                   expected.MaximumRetryBackoffMilliseconds ==
                   actual.MaximumRetryBackoffMilliseconds &&
                   expected.RateLimitRequestCountHint ==
                   actual.RateLimitRequestCountHint &&
                   expected.RateLimitWindowSecondsHint.Equals(
                       actual.RateLimitWindowSecondsHint);
        }

        internal static void ResetForLifecycle(
            SimultriaViewerRuntimeConnectionContext owner = null)
        {
            lock (Gate)
            {
                if (owner == null || ReferenceEquals(current, owner))
                {
                    current = null;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForRuntime()
        {
            lock (Gate)
            {
                current = null;
            }
        }

        private sealed class Registration : IDisposable
        {
            private SimultriaViewerRuntimeConnectionContext owner;

            internal Registration(
                SimultriaViewerRuntimeConnectionContext context)
            {
                owner = context;
            }

            public void Dispose()
            {
                SimultriaViewerRuntimeConnectionContext released = owner;
                owner = null;
                if (released != null)
                {
                    ResetForLifecycle(released);
                }
            }
        }
    }
}
