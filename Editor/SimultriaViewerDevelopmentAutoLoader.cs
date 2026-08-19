using System;
using System.Threading;
using Deucarian.CommandRouting;
using Deucarian.Logging;
using Deucarian.ViewerAuthentication;
using UnityEditor;

namespace Deucarian.SimultriaViewerConnection.Editor
{
    [InitializeOnLoad]
    internal static class SimultriaViewerDevelopmentAutoLoader
    {
        private const string PendingCommandKey =
            "Deucarian.SimultriaViewerConnection.PendingCommand";
        private const string PendingWarningKey =
            "Deucarian.SimultriaViewerConnection.PendingWarning";
        private const double MaximumWaitSeconds = 120d;

        private static readonly DLog Log = DLog.For("SimultriaViewerConnection.Development");
        private static CancellationTokenSource cancellation;
        private static CommandEnvelope pendingCommand;
        private static string pendingWarning;
        private static double deadline;
        private static string lastWaitReason;
        private static bool dispatching;

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

            if (!SimultriaViewerDevelopmentProfileSelector.TryResolve(
                    out SimultriaViewerDevelopmentProfile profile,
                    out _,
                    out pendingWarning) ||
                !SimultriaViewerDevelopmentCommandService.TryCreateCommand(
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
        }

        private static void Restore()
        {
            pendingWarning = SessionState.GetString(PendingWarningKey, string.Empty);
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
                Log.Warning("Development context was not auto-loaded. " + pendingWarning);
                Stop();
                return;
            }

            if (pendingCommand == null)
            {
                Stop();
                return;
            }

            if (!SimultriaViewerDevelopmentCommandService.TryResolveLivePort(
                    out CommandRoutePortBehaviour port,
                    out ViewerAuthenticationTarget authenticationTarget,
                    out string waitReason))
            {
                if (!string.Equals(lastWaitReason, waitReason, StringComparison.Ordinal))
                {
                    lastWaitReason = waitReason;
                    Log.Info(waitReason);
                }

                if (EditorApplication.timeSinceStartup >= deadline)
                {
                    Log.Warning("Development context auto-load timed out. " + waitReason);
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
            ViewerAuthenticationTarget authenticationTarget)
        {
            try
            {
                if (!SimultriaViewerDevelopmentProfileSelector.TryResolve(
                        out SimultriaViewerDevelopmentProfile profile,
                        out _,
                        out string profileError))
                {
                    Log.Warning(
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
                    Log.Warning(
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

        private static void Stop()
        {
            EditorApplication.update -= Tick;
            dispatching = false;
            pendingCommand = null;
            lastWaitReason = null;
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
        }

        private static void ClearPending()
        {
            pendingCommand = null;
            pendingWarning = null;
            SessionState.EraseString(PendingCommandKey);
            SessionState.EraseString(PendingWarningKey);
        }
    }
}
