using System;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection
{
    /// <summary>
    /// One immutable, sanitized environment decision for the current viewer
    /// session. No host, credential, or token is exposed here.
    /// </summary>
    public static class SimultriaViewerRuntimeEnvironment
    {
        private static readonly object Gate = new object();
        private static SimultriaViewerEnvironmentResolution current;

        public static event Action<SimultriaViewerEnvironmentResolution>
            Changed;

        public static SimultriaViewerEnvironmentResolution Current
        {
            get
            {
                lock (Gate)
                {
                    return current;
                }
            }
        }

        public static bool TryGetCurrent(
            out SimultriaViewerEnvironmentResolution resolution)
        {
            resolution = Current;
            return resolution?.Succeeded == true;
        }

        /// <summary>
        /// Publishes the decision made by the application startup owner.
        /// Replacing it after startup is intentionally unsupported.
        /// </summary>
        public static bool TryActivate(
            SimultriaViewerEnvironmentResolution resolution,
            out string error)
        {
            if (resolution?.Succeeded != true)
            {
                error = "Only a successful Simultria environment resolution " +
                        "can become active.";
                return false;
            }

            lock (Gate)
            {
                if (current != null &&
                    !ReferenceEquals(current, resolution))
                {
                    error = "The Simultria environment is already fixed for " +
                            "this application session.";
                    return false;
                }

                current = resolution;
            }

            Changed?.Invoke(resolution);
            error = string.Empty;
            return true;
        }

        internal static void ResetForLifecycle(
            SimultriaViewerEnvironmentResolution owner = null)
        {
            bool changed;
            lock (Gate)
            {
                changed = current != null &&
                    (owner == null || ReferenceEquals(current, owner));
                if (changed)
                {
                    current = null;
                }
            }

            if (changed)
            {
                Changed?.Invoke(null);
            }
        }

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForRuntime()
        {
            lock (Gate)
            {
                current = null;
                Changed = null;
            }
        }
    }
}
