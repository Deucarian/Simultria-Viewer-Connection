using System;
using System.Threading;
using System.Threading.Tasks;
using Deucarian.Logging;
using Deucarian.Authentication;
using Deucarian.API.Configuration;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration
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
        [SerializeField, HideInInspector] private ApiEnvironmentId
            buildProfileEnvironmentId;
        [Tooltip(
            "Viewer startup components that must remain disabled until the " +
            "effective environment and runtime connection are ready.")]
        [SerializeField] private Behaviour[] startupBehaviours =
            Array.Empty<Behaviour>();

        private CancellationTokenSource cancellation;
        private IDisposable providerRegistration;
        private SimultriaViewerEnvironmentResolver resolver;
        private Func<SimultriaViewerEnvironmentResolver> resolverFactory;
        private Func<ApiConnectionSettings, ApiEnvironmentId,
            IViewerRuntimeConnectionProvider> providerFactory;
        private bool started;
        private bool disposed;

        public SimultriaViewerBuildConfiguration BuildConfiguration =>
            buildConfiguration;

        internal ApiEnvironmentId BuildProfileEnvironmentId =>
            buildProfileEnvironmentId;

#if UNITY_EDITOR
        internal void CaptureBuildProfileEnvironment(bool developmentBuild)
        {
            buildProfileEnvironmentId = developmentBuild
                ? SimultriaEnvironmentIds.Development
                : SimultriaEnvironmentIds.Production;
        }
#endif

        public SimultriaViewerEnvironmentResolution Resolution { get; private set; }

        public Task PendingResolution { get; private set; } =
            Task.CompletedTask;

        /// <summary>
        /// Subscribe to StartupStatusChanged first, then read this property to
        /// replay the current state. Routed/Fallback mean connection readiness,
        /// not viewer or model readiness. No diagnostic payload is exposed.
        /// </summary>
        public SimultriaViewerBuildStartupSnapshot StartupStatus { get; private set; } =
            new SimultriaViewerBuildStartupSnapshot(
                SimultriaViewerBuildStartupPhase.NotStarted);

        /// <summary>
        /// Instance-owned lifecycle notification. Observer exceptions do not
        /// affect startup. Disposed is final and releases existing observers.
        /// </summary>
        public event Action<SimultriaViewerBuildStartupSnapshot> StartupStatusChanged;

        /// <summary>Checks explicit ownership without discovering scene objects.</summary>
        public bool ContainsStartupBehaviour(Behaviour behaviour)
        {
            if (behaviour == null || behaviour == this || startupBehaviours == null)
                return false;
            for (int i = 0; i < startupBehaviours.Length; i++)
                if (startupBehaviours[i] == behaviour)
                    return true;
            return false;
        }

        private void Awake() => BeginStartup();

        internal void BeginStartup()
        {
            if (started || disposed)
                return;
            started = true;
            cancellation = new CancellationTokenSource();
            CancellationToken token = cancellation.Token;
            SetStartupEnabled(false);
            if (disposed)
                return;
            Publish(new SimultriaViewerBuildStartupSnapshot(
                SimultriaViewerBuildStartupPhase.Resolving));
            if (!disposed)
                PendingResolution = ResolveAndOpenAsync(token);
        }

        private async Task ResolveAndOpenAsync(
            CancellationToken cancellationToken)
        {
            var failure = SimultriaViewerBuildStartupFailureCode.EnvironmentResolutionFailed;
            try
            {
                resolver = resolver ?? (resolverFactory != null
                    ? resolverFactory()
                    : SimultriaViewerEnvironmentResolver.CreateDefault(
                        buildProfileEnvironmentId));
                if (StopIfCancelled(cancellationToken))
                    return;
                SimultriaViewerEnvironmentResolution result =
                    await resolver.ResolveForCurrentRuntimeAsync(
                        buildConfiguration,
                        cancellationToken);
                if (StopIfCancelled(cancellationToken))
                    return;

                Resolution = result;
                if (result?.Succeeded != true)
                {
                    Fail(failure);
                    return;
                }

                failure = SimultriaViewerBuildStartupFailureCode.ProviderCreationFailed;
                bool created = TryCreateProvider(result,
                    out IViewerRuntimeConnectionProvider provider, out _);
                if (StopIfCancelled(cancellationToken))
                    return;
                if (!created || provider == null)
                {
                    Fail(failure);
                    return;
                }

                failure = SimultriaViewerBuildStartupFailureCode.ProviderRegistrationFailed;
                providerRegistration =
                    ViewerRuntimeConnectionProviderRegistry.Register(provider);
                if (StopIfCancelled(cancellationToken))
                {
                    ReleaseProvider();
                    return;
                }

                failure = SimultriaViewerBuildStartupFailureCode.EnvironmentActivationFailed;
                bool activated = SimultriaViewerRuntimeEnvironment.TryActivate(
                    result, out _);
                if (StopIfCancelled(cancellationToken))
                    return;
                if (!activated)
                {
                    Fail(failure);
                    return;
                }

                Publish(new SimultriaViewerBuildStartupSnapshot(
                    result.UsedBuildProfileFallback
                        ? SimultriaViewerBuildStartupPhase.Fallback
                        : SimultriaViewerBuildStartupPhase.Routed,
                    environmentId: result.EnvironmentId));
                if (!StopIfCancelled(cancellationToken))
                    SetStartupEnabled(true);
            }
            catch (OperationCanceledException)
            {
                Fail(SimultriaViewerBuildStartupFailureCode.StartupCancelled);
            }
            catch (Exception)
            {
                Fail(failure);
            }
        }

        private bool StopIfCancelled(CancellationToken token)
        {
            if (disposed)
                return true;
            if (!token.IsCancellationRequested)
                return false;
            Fail(SimultriaViewerBuildStartupFailureCode.StartupCancelled);
            return true;
        }

        private void Fail(SimultriaViewerBuildStartupFailureCode code)
        {
            if (disposed || StartupStatus.Phase == SimultriaViewerBuildStartupPhase.Failed)
                return;
            ReleaseProvider();
            SetStartupEnabled(false);
            Publish(new SimultriaViewerBuildStartupSnapshot(
                SimultriaViewerBuildStartupPhase.Failed, code));
            Log.Error("Viewer startup stopped: " + code + ".", this);
        }

        private bool TryCreateProvider(
            SimultriaViewerEnvironmentResolution resolution,
            out IViewerRuntimeConnectionProvider provider,
            out string error)
        {
            provider = null;
            if (!resolver.TryResolveConnectionSettingsForCurrentRuntime(
                    buildConfiguration,
                    out Deucarian.API.Configuration.ApiConnectionSettings
                        connection,
                    out error))
            {
                return false;
            }

            provider = providerFactory != null
                ? providerFactory(connection, resolution.EnvironmentId)
                : CreateProvider(connection, resolution.EnvironmentId);
            error = string.Empty;
            return true;
        }

        internal static IViewerRuntimeConnectionProvider CreateProvider(
            Deucarian.API.Configuration.ApiConnectionSettings connection,
            Deucarian.API.Models.ApiEnvironmentId environmentId)
        {
            return SimultriaViewerRuntimeConnectionProviderFactory.Create(
                connection,
                environmentId);
        }

        private void SetStartupEnabled(bool value)
        {
            if (startupBehaviours == null)
            {
                return;
            }

            for (int i = 0; i < startupBehaviours.Length; i++)
            {
                if (value && disposed)
                    return;
                Behaviour behaviour = startupBehaviours[i];
                if (behaviour != null && behaviour != this)
                {
                    behaviour.enabled = value;
                }
            }
        }

        private void Publish(SimultriaViewerBuildStartupSnapshot snapshot)
        {
            if (disposed && snapshot.Phase != SimultriaViewerBuildStartupPhase.Disposed)
                return;
            StartupStatus = snapshot;
            Delegate[] observers = StartupStatusChanged?.GetInvocationList();
            if (observers == null)
                return;
            foreach (Delegate observer in observers)
            {
                // A prior callback may have disposed this gate. Do not replay
                // the superseded state to remaining observers.
                if (!ReferenceEquals(StartupStatus, snapshot))
                    break;
                try
                {
                    ((Action<SimultriaViewerBuildStartupSnapshot>)observer)(snapshot);
                }
                catch (Exception)
                {
                    Log.Warning("A viewer startup status observer failed.", this);
                }
            }
        }

        private void ReleaseProvider()
        {
            IDisposable registration = providerRegistration;
            providerRegistration = null;
            registration?.Dispose();
        }

        private void OnDestroy() => DisposeStartup();

        internal void DisposeStartup()
        {
            if (disposed)
                return;
            disposed = true;
            CancellationTokenSource source = cancellation;
            cancellation = null;
            try
            {
                source?.Cancel();
            }
            catch (Exception)
            {
                Log.Warning("Viewer startup cancellation cleanup failed.", this);
            }
            finally
            {
                source?.Dispose();
                ReleaseProvider();
                SetStartupEnabled(false);
                Publish(new SimultriaViewerBuildStartupSnapshot(
                    SimultriaViewerBuildStartupPhase.Disposed));
                StartupStatusChanged = null;
            }
        }

        internal void ConfigureForTests(
            SimultriaViewerBuildConfiguration configuration,
            Behaviour[] behaviours,
            SimultriaViewerEnvironmentResolver testResolver,
            Func<ApiConnectionSettings, ApiEnvironmentId,
                IViewerRuntimeConnectionProvider> testProviderFactory = null,
            Func<SimultriaViewerEnvironmentResolver> testResolverFactory = null)
        {
            buildConfiguration = configuration;
            startupBehaviours = behaviours ?? Array.Empty<Behaviour>();
            resolver = testResolver;
            providerFactory = testProviderFactory;
            resolverFactory = testResolverFactory;
        }
    }
}
