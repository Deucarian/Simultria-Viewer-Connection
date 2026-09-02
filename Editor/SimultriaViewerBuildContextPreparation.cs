using System;
using Deucarian.API.Models;
using Deucarian.BuildPipeline;
using UnityEditor.Build;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    internal delegate bool SimultriaViewerDevelopmentContextExport(
        SimultriaViewerDevelopmentContext profile,
        ApiEnvironmentId environmentId,
        out string message);

    internal interface ISimultriaViewerBuildContextPreparation
    {
        IDisposable Prepare(
            DeucarianBuildEnvironment environment,
            SimultriaViewerDevelopmentContext profile,
            ApiEnvironmentId effectiveEnvironmentId);
    }

    internal sealed class SimultriaViewerBuildContextPreparation :
        ISimultriaViewerBuildContextPreparation
    {
        private readonly Func<SimultriaViewerBuildContextFileScope>
            scopeFactory;
        private readonly SimultriaViewerDevelopmentContextExport exporter;

        internal SimultriaViewerBuildContextPreparation()
            : this(
                () => new SimultriaViewerBuildContextFileScope(),
                SimultriaViewerWebGlDevelopmentExporter.TryExport)
        {
        }

        internal SimultriaViewerBuildContextPreparation(
            Func<SimultriaViewerBuildContextFileScope> contextScopeFactory,
            SimultriaViewerDevelopmentContextExport contextExporter)
        {
            scopeFactory = contextScopeFactory ??
                throw new ArgumentNullException(nameof(contextScopeFactory));
            exporter = contextExporter ??
                throw new ArgumentNullException(nameof(contextExporter));
        }

        public IDisposable Prepare(
            DeucarianBuildEnvironment environment,
            SimultriaViewerDevelopmentContext profile,
            ApiEnvironmentId effectiveEnvironmentId)
        {
            SimultriaViewerBuildContextFileScope scope = null;
            try
            {
                scope = scopeFactory();
                if (scope == null)
                {
                    throw new InvalidOperationException();
                }

                scope.RemoveAll();
                if (environment == DeucarianBuildEnvironment.Development)
                {
                    if (profile == null || effectiveEnvironmentId.IsEmpty ||
                        !exporter(
                            profile,
                            effectiveEnvironmentId,
                            out _) ||
                        !SimultriaViewerBuildContextValidator.TryValidateFile(
                            scope.CurrentPath,
                            out _))
                    {
                        throw new BuildFailedException(
                            "The credential-free Simultria viewer development " +
                            "context could not be prepared.");
                    }
                }

                return scope;
            }
            catch (Exception exception)
            {
                bool restored = TryRestore(scope);
                if (!restored)
                {
                    throw new BuildFailedException(
                        "Simultria viewer build preparation failed and its " +
                        "project context files could not be restored.");
                }

                if (exception is BuildFailedException buildFailure)
                {
                    throw buildFailure;
                }

                throw new BuildFailedException(
                    "The Simultria viewer build context could not be prepared.");
            }
        }

        private static bool TryRestore(IDisposable scope)
        {
            if (scope == null)
            {
                return true;
            }

            try
            {
                scope.Dispose();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
