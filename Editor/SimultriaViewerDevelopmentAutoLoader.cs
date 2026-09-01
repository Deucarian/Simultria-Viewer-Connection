using System;
using System.Threading;
using Deucarian.API.Configuration;
using Deucarian.API.Models;
using Deucarian.CommandRouting;
using Deucarian.Logging;
using Deucarian.Simultria.API.Configuration;
using Deucarian.Authentication;
using UnityEditor;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    [InitializeOnLoad]
    internal static class SimultriaViewerDevelopmentAutoLoader
    {
        private const string PendingCommandKey =
            "Deucarian.SimultriaViewerIntegration.PendingCommand";
        private const string PendingWarningKey =
            "Deucarian.SimultriaViewerIntegration.PendingWarning";
        private const string PendingAutomaticKey =
            "Deucarian.SimultriaViewerIntegration.PendingAutomatic";
        private const double MaximumWaitSeconds = 120d;

        private static readonly DLog Log = DLog.For("SimultriaViewerConnection.Development");
        private static CancellationTokenSource cancellation;
        private static CommandEnvelope pendingCommand;
        private static string pendingWarning;
        private static double deadline;
        private static string lastWaitReason;
        private static bool dispatching;
        private static bool pendingAutomatic;
        private static bool resolvingAutomatic;
        private static IDisposable runtimeProviderRegistration;

        static SimultriaViewerDevelopmentAutoLoader()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                Prepare();
                return;
            }

            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Restore();
                RegisterRuntimeConnectionProvider();
                deadline = EditorApplication.timeSinceStartup + MaximumWaitSeconds;
                cancellation = new CancellationTokenSource();
                dispatching = false;
                lastWaitReason = null;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
                return;
            }

            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Stop();
                ReleaseRuntimeConnectionProvider();
                ClearPending();
            }
        }

        private static void Prepare()
        {
            ClearPending();
            if (!SimultriaViewerConnectionProjectSettings.instance.AutoLoadInPlayMode)
            {
                return;
            }

            if (!SimultriaViewerDevelopmentContextSelector.TryResolve(
                    out SimultriaViewerDevelopmentContext profile,
                    out _,
                    out pendingWarning))
            {
                pendingCommand = null;
            }
            else if (profile.EnvironmentResolutionMode ==
                SimultriaViewerEnvironmentResolutionMode
                    .AutomaticFromUnityBuildVersion)
            {
                pendingAutomatic = true;
                pendingWarning = null;
            }
            else if (!SimultriaViewerDevelopmentCommandService.TryCreateCommand(
                         profile,
                         out pendingCommand,
                         out pendingWarning))
            {
                pendingCommand = null;
            }

            SessionState.SetString(
                PendingCommandKey,
                pendingCommand == null
                    ? string.Empty
                    : SimultriaViewerInitializationCommand.Serialize(pendingCommand, false));
            SessionState.SetString(PendingWarningKey, pendingWarning ?? string.Empty);
            SessionState.SetBool(PendingAutomaticKey, pendingAutomatic);
        }

        private static void Restore()
        {
            pendingWarning = SessionState.GetString(PendingWarningKey, string.Empty);
            pendingAutomatic = SessionState.GetBool(PendingAutomaticKey, false);
            string json = SessionState.GetString(PendingCommandKey, string.Empty);
            pendingCommand = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var codec = new JsonCommandProtocolCodec();
            if (!codec.TryDecode(json, out pendingCommand, out CommandResult failure))
            {
                pendingWarning = failure?.Message ??
                                 "The prepared development command could not be restored.";
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || dispatching)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(pendingWarning))
            {
                Log.Error("Development context was not auto-loaded. " + pendingWarning);
                Stop();
                return;
            }

            if (pendingCommand == null)
            {
                if (pendingAutomatic)
                {
                    if (!resolvingAutomatic)
                    {
                        ResolveAutomaticCommandAsync();
                    }

                    return;
                }

                Stop();
                return;
            }

            if (!SimultriaViewerDevelopmentCommandService.TryResolveLivePort(
                    out CommandRoutePortBehaviour port,
                    out AuthenticationTarget authenticationTarget,
                    out string waitReason))
            {
                if (!string.Equals(lastWaitReason, waitReason, StringComparison.Ordinal))
                {
                    lastWaitReason = waitReason;
                    Log.Info(waitReason);
                }

                if (SimultriaViewerDevelopmentCommandService
                        .IsWaitingForAuthentication(waitReason))
                {
                    // Authentication is an explicit user interaction and must not
                    // expire while the sign-in window is open. Once authentication
                    // succeeds, the normal component wait retains a full deadline.
                    deadline = EditorApplication.timeSinceStartup +
                               MaximumWaitSeconds;
                }
                else if (EditorApplication.timeSinceStartup >= deadline)
                {
                    Log.Error("Development context auto-load timed out. " + waitReason);
                    Stop();
                }

                return;
            }

            dispatching = true;
            EditorApplication.update -= Tick;
            DispatchAsync(port, authenticationTarget);
        }

        private static async void DispatchAsync(
            CommandRoutePortBehaviour port,
            AuthenticationTarget authenticationTarget)
        {
            try
            {
                if (!SimultriaViewerDevelopmentContextSelector.TryResolve(
                        out SimultriaViewerDevelopmentContext profile,
                        out _,
                        out string profileError))
                {
                    Log.Error(
                        "Development context was not auto-loaded. " +
                        profileError);
                    return;
                }

                CommandResult result = await
                    SimultriaViewerDevelopmentCommandService.DispatchToPortAsync(
                        pendingCommand,
                        profile,
                        authenticationTarget,
                        port,
                        cancellation?.Token ?? CancellationToken.None);
                if (result != null && result.Succeeded)
                {
                    Log.Info("Auto-loaded the selected Simultria development profile through Command Routing.");
                }
                else
                {
                    Log.Error(
                        "Simultria development initialization was rejected" +
                        (string.IsNullOrWhiteSpace(result?.ErrorCode)
                            ? "."
                            : " with '" + result.ErrorCode + "'.") +
                        (string.IsNullOrWhiteSpace(result?.Message)
                            ? string.Empty
                            : " " + result.Message));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Log.Error(
                    "Simultria development initialization failed with " +
                    exception.GetType().Name + ".");
            }
            finally
            {
                Stop();
            }
        }

        private static async void ResolveAutomaticCommandAsync()
        {
            resolvingAutomatic = true;
            try
            {
                if (!SimultriaViewerDevelopmentContextSelector.TryResolve(
                        out SimultriaViewerDevelopmentContext profile,
                        out _,
                        out string profileError))
                {
                    pendingWarning = profileError;
                    pendingAutomatic = false;
                    return;
                }

                SimultriaViewerDevelopmentCommandService
                    .DevelopmentCommandCreation creation =
                    await SimultriaViewerDevelopmentCommandService
                        .CreateCommandAsync(
                            profile,
                            SimultriaViewerEnvironmentResolver.CreateDefault(),
                            cancellation?.Token ?? CancellationToken.None);
                pendingCommand = creation?.Command;
                pendingWarning = creation?.Succeeded == true
                    ? null
                    : creation?.Message ??
                      "The automatic Simultria environment could not be resolved.";
                pendingAutomatic = false;
            }
            catch (OperationCanceledException)
            {
                pendingAutomatic = false;
            }
            catch (Exception exception)
            {
                pendingWarning =
                    "Automatic Simultria environment resolution failed with " +
                    exception.GetType().Name + ".";
                pendingAutomatic = false;
            }
            finally
            {
                resolvingAutomatic = false;
            }
        }

        private static void Stop()
        {
            EditorApplication.update -= Tick;
            dispatching = false;
            pendingCommand = null;
            pendingAutomatic = false;
            resolvingAutomatic = false;
            lastWaitReason = null;
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
        }

        private static void RegisterRuntimeConnectionProvider()
        {
            ReleaseRuntimeConnectionProvider();
            if (UnityEngine.Object.FindFirstObjectByType<
                    SimultriaViewerBuildConnectionGate>(
                    UnityEngine.FindObjectsInactive.Include) != null)
            {
                // The scene gate owns both environment resolution and the
                // runtime provider. The Editor auto-loader still dispatches
                // the optional development command after startup opens.
                return;
            }

            if (!SimultriaViewerConnectionProjectSettings.instance
                    .AutoLoadInPlayMode ||
                pendingAutomatic ||
                !string.IsNullOrWhiteSpace(pendingWarning))
            {
                return;
            }

            if (!SimultriaViewerDevelopmentContextSelector.TryResolve(
                    out SimultriaViewerDevelopmentContext profile,
                    out _,
                    out string profileError))
            {
                pendingWarning = profileError;
                return;
            }

            if (!TryCreateRuntimeConnectionProvider(
                    profile,
                    profile.EnvironmentId,
                    out IViewerRuntimeConnectionProvider provider,
                    out string error))
            {
                pendingWarning = error;
                return;
            }

            try
            {
                runtimeProviderRegistration =
                    ViewerRuntimeConnectionProviderRegistry.Register(provider);
            }
            catch (Exception exception)
            {
                pendingWarning =
                    "The selected development runtime connection could not " +
                    "be registered (" + exception.GetType().Name + ").";
            }
        }

        internal static bool TryCreateRuntimeConnectionProvider(
            SimultriaViewerDevelopmentContext profile,
            ApiEnvironmentId effectiveEnvironmentId,
            out IViewerRuntimeConnectionProvider provider,
            out string error)
        {
            provider = null;
            if (profile == null)
            {
                error = "A Simultria viewer development context is required.";
                return false;
            }

            if (effectiveEnvironmentId.IsEmpty)
            {
                error = "A resolved Simultria environment is required.";
                return false;
            }

            ApiConnectionSettings settings =
                profile.ConnectionSettingsReference;
            if (settings == null)
            {
                error = "The development context has no API connection settings.";
                return false;
            }

            provider = SimultriaViewerRuntimeConnectionProviderFactory.Create(
                settings,
                effectiveEnvironmentId);
            error = string.Empty;
            return true;
        }

        private static void ReleaseRuntimeConnectionProvider()
        {
            runtimeProviderRegistration?.Dispose();
            runtimeProviderRegistration = null;
        }

        private static void ClearPending()
        {
            pendingCommand = null;
            pendingWarning = null;
            pendingAutomatic = false;
            resolvingAutomatic = false;
            SessionState.EraseString(PendingCommandKey);
            SessionState.EraseString(PendingWarningKey);
            SessionState.EraseBool(PendingAutomaticKey);
        }
    }
}
