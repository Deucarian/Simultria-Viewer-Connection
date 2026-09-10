using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    internal static partial class SimultriaViewerEditorAuthenticationHost
    {
        internal static bool TryGetEffectiveEnvironment(
            SimultriaViewerDevelopmentContext profile,
            out Deucarian.API.Models.ApiEnvironmentId environmentId,
            out SimultriaViewerEnvironmentResolution resolution,
            out string message)
        {
            environmentId = default(Deucarian.API.Models.ApiEnvironmentId);
            resolution = null;
            if (profile == null)
            {
                message = "No Simultria viewer development profile is selected.";
                return false;
            }

            if (profile.EnvironmentResolutionMode ==
                SimultriaViewerEnvironmentResolutionMode.Manual)
            {
                environmentId = profile.EnvironmentId;
                message = null;
                return true;
            }

            string key = BuildEnvironmentResolutionKey(profile);
            if (!ReferenceEquals(environmentResolutionProfile, profile) ||
                !string.Equals(
                    environmentResolutionKey,
                    key,
                    StringComparison.Ordinal))
            {
                message = "Resolving the environment from the Simultria " +
                          "Unity build directory.";
                return false;
            }

            resolution = environmentResolution;
            if (resolution?.Succeeded != true)
            {
                message = environmentResolutionInFlight
                    ? "Resolving the environment from the Simultria Unity " +
                      "build directory."
                    : resolution?.Message ??
                      "The automatic environment has not been resolved.";
                return false;
            }

            environmentId = resolution.EnvironmentId;
            message = null;
            return true;
        }

        private static void StartEnvironmentResolution(
            SimultriaViewerDevelopmentContext profile)
        {
            if (profile == null || testSuspensionCount > 0)
            {
                return;
            }

            _ = StartEnvironmentResolution(profile, (context, token) =>
                SimultriaViewerEnvironmentResolver.CreateDefault().ResolveAsync(context, token));
        }

        // The editor host owns request lifetime; the supplied resolver owns transport.
        // This also lets overlap/cancellation tests run without external requests.
        internal static Task StartEnvironmentResolution(
            SimultriaViewerDevelopmentContext profile,
            Func<SimultriaViewerDevelopmentContext, CancellationToken,
                Task<SimultriaViewerEnvironmentResolution>> resolveAsync)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (resolveAsync == null) throw new ArgumentNullException(nameof(resolveAsync));
            string key = BuildEnvironmentResolutionKey(profile);
            if (ReferenceEquals(environmentResolutionProfile, profile) &&
                string.Equals(
                    environmentResolutionKey,
                    key,
                    StringComparison.Ordinal) &&
                (environmentResolutionInFlight || environmentResolution != null))
            {
                return Task.CompletedTask;
            }

            environmentResolutionCancellation?.Cancel();
            environmentResolutionCancellation?.Dispose();
            environmentResolutionCancellation = new CancellationTokenSource();
            environmentResolutionProfile = profile;
            environmentResolutionKey = key;
            environmentResolution = null;
            environmentResolutionInFlight = true;
            return ResolveEnvironmentAsync(
                profile,
                key,
                environmentResolutionCancellation.Token,
                resolveAsync);
        }

        private static async Task ResolveEnvironmentAsync(
            SimultriaViewerDevelopmentContext profile,
            string key,
            CancellationToken cancellationToken,
            Func<SimultriaViewerDevelopmentContext, CancellationToken,
                Task<SimultriaViewerEnvironmentResolution>> resolveAsync)
        {
            SimultriaViewerEnvironmentResolution result;
            try
            {
                result = await resolveAsync(profile, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                result = SimultriaViewerEnvironmentResolution.Failure(
                    SimultriaViewerEnvironmentResolutionMode
                        .AutomaticFromUnityBuildVersion,
                    profile == null ? null : profile.BuildVersionOverride,
                    profile == null ? null : profile.BuildProduct,
                    "environment_resolution_failed",
                    "Automatic environment resolution failed (" +
                    exception.GetType().Name + ").",
                    SimultriaViewerRuntimeKind.Editor,
                    Application.productName,
                    editorOverrideActive: false);
            }

            if (shuttingDown || cancellationToken.IsCancellationRequested ||
                profile == null ||
                !ReferenceEquals(environmentResolutionProfile, profile) ||
                !string.Equals(
                    environmentResolutionKey,
                    key,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    BuildEnvironmentResolutionKey(profile),
                    key,
                    StringComparison.Ordinal))
            {
                return;
            }

            environmentResolution = result;
            environmentResolutionInFlight = false;
            Lease.Invalidate();
            EnvironmentResolutionChanged?.Invoke();
            ReconcileNow();
        }

        private static string BuildEnvironmentResolutionKey(
            SimultriaViewerDevelopmentContext profile)
        {
            if (profile == null)
            {
                return string.Empty;
            }

            ScriptableObject connection = profile.EffectiveProfileReference;
            return profile.GetInstanceID() + "|" +
                   (int)profile.EnvironmentResolutionMode + "|" +
                   (int)profile.LookupEnvironment + "|" +
                   profile.BuildProduct + "|" +
                   profile.BuildVersionOverride + "|" +
                   Application.version + "|" +
                   (connection == null ? 0 : connection.GetInstanceID());
        }

        private static void InvalidateEnvironmentResolution()
        {
            environmentResolutionCancellation?.Cancel();
            environmentResolutionCancellation?.Dispose();
            environmentResolutionCancellation = null;
            environmentResolutionProfile = null;
            environmentResolution = null;
            environmentResolutionKey = null;
            environmentResolutionInFlight = false;
            EnvironmentResolutionChanged?.Invoke();
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
                RequestRefresh(invalidateEnvironmentResolution: false);
            }
        }
    }
}
