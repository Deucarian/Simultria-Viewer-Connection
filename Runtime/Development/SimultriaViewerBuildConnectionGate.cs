using System;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.Logging;
using Deucarian.ViewerAuthentication;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection
{
    /// <summary>
    /// Holds viewer startup until one effective Simultria environment is
    /// resolved and its runtime connection provider is registered.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class SimultriaViewerBuildConnectionGate : MonoBehaviour
    {
        private static readonly DLog Log =
            DLog.For("SimultriaViewerConnection.Environment");

        [SerializeField] private SimultriaViewerBuildConfiguration
            buildConfiguration;
        [Tooltip(
            "Viewer startup components that must remain disabled until the " +
            "effective environment and runtime connection are ready.")]
        [SerializeField] private Behaviour[] startupBehaviours =
            Array.Empty<Behaviour>();

        private CancellationTokenSource cancellation;
        private IDisposable providerRegistration;
        private SimultriaViewerEnvironmentResolver resolver;

        public SimultriaViewerBuildConfiguration BuildConfiguration =>
            buildConfiguration;

        public SimultriaViewerEnvironmentResolution Resolution { get; private set; }

        public Task PendingResolution { get; private set; } =
            Task.CompletedTask;

        private void Awake()
        {
            SetStartupEnabled(false);
            cancellation = new CancellationTokenSource();
            resolver = resolver ??
                SimultriaViewerEnvironmentResolver.CreateDefault();
            PendingResolution = ResolveAndOpenAsync(cancellation.Token);
        }

        private async Task ResolveAndOpenAsync(
            CancellationToken cancellationToken)
        {
            SimultriaViewerEnvironmentResolution result;
            try
            {
                result = await resolver.ResolveForCurrentRuntimeAsync(
                    buildConfiguration,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                Log.Error(
                    "Viewer startup stopped because environment resolution " +
                    "failed (" + exception.GetType().Name + ").",
                    this);
                return;
            }

            Resolution = result;
            if (result?.Succeeded != true)
            {
                Log.Error(
                    "Viewer startup stopped: " +
                    (result?.Message ??
                     "the effective Simultria environment is unresolved.") +
                    " " + result?.ToDiagnosticString(),
                    this);
                return;
            }

            if (!TryCreateProvider(
                    result,
                    out IViewerRuntimeConnectionProvider provider,
                    out string providerError))
            {
                Log.Error(
                    "Viewer startup stopped: " + providerError + " " +
                    result.ToDiagnosticString(),
                    this);
                return;
            }

            try
            {
                providerRegistration =
                    ViewerRuntimeConnectionProviderRegistry.Register(provider);
            }
            catch (Exception exception)
            {
                Log.Error(
                    "Viewer startup stopped because its resolved runtime " +
                    "connection could not be registered (" +
                    exception.GetType().Name + "). " +
                    result.ToDiagnosticString(),
                    this);
                return;
            }

            if (!SimultriaViewerRuntimeEnvironment.TryActivate(
                    result,
                    out string activationError))
            {
                providerRegistration.Dispose();
                providerRegistration = null;
                Log.Error(
                    "Viewer startup stopped: " + activationError + " " +
                    result.ToDiagnosticString(),
                    this);
                return;
            }

            Log.Info(
                "Resolved the viewer environment. " +
                result.ToDiagnosticString(),
                this);
            SetStartupEnabled(true);
        }

        private bool TryCreateProvider(
            SimultriaViewerEnvironmentResolution resolution,
            out IViewerRuntimeConnectionProvider provider,
            out string error)
        {
            provider = null;
            if (!resolver.TryResolveConnectionProfileForCurrentRuntime(
                    buildConfiguration,
                    out Deucarian.API.Configuration.ApiConnectionProfile
                        connection,
                    out error))
            {
                return false;
            }

            provider = new SimultriaViewerRuntimeConnectionProvider(
                connection,
                resolution.EnvironmentId);
            error = string.Empty;
            return true;
        }

        private void SetStartupEnabled(bool value)
        {
            if (startupBehaviours == null)
            {
                return;
            }

            for (int i = 0; i < startupBehaviours.Length; i++)
            {
                Behaviour behaviour = startupBehaviours[i];
                if (behaviour != null && behaviour != this)
                {
                    behaviour.enabled = value;
                }
            }
        }

        private void OnDestroy()
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
            providerRegistration?.Dispose();
            providerRegistration = null;
        }

        internal void ConfigureForTests(
            SimultriaViewerBuildConfiguration configuration,
            Behaviour[] behaviours,
            SimultriaViewerEnvironmentResolver testResolver)
        {
            buildConfiguration = configuration;
            startupBehaviours = behaviours ?? Array.Empty<Behaviour>();
            resolver = testResolver;
        }
    }
}
