using System;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.BuildPipeline;
using Deucarian.Simultria.API.Configuration;
using UnityEditor.Build;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    internal delegate bool SimultriaViewerDevelopmentContextSelection(
        out SimultriaViewerDevelopmentContext profile,
        out string source,
        out string error);

    /// <summary>
    /// Owns Simultria viewer connection validation and credential-free context
    /// preparation for Build Pipeline requests whose selected scene contains
    /// a <see cref="SimultriaViewerBuildConnectionGate"/> or a canonical
    /// Simultria viewer connection-settings source.
    /// </summary>
    public sealed class SimultriaViewerBuildLifecycleContributor :
        IDeucarianBuildLifecycleContributor
    {
        public const string ContributorId = "simultria-viewer-connection";
        private static readonly ApiEnvironmentId[] PromotableEnvironments =
        {
            SimultriaEnvironmentIds.Development,
            SimultriaEnvironmentIds.Testing,
            SimultriaEnvironmentIds.Acceptance,
            SimultriaEnvironmentIds.Production
        };

        private readonly ISimultriaViewerBuildSceneInspector sceneInspector;
        private readonly SimultriaViewerDevelopmentContextSelection
            contextSelector;
        private readonly ISimultriaViewerBuildContextPreparation preparation;
        private readonly ISimultriaViewerBuildArtifactValidator artifactValidator;

        public SimultriaViewerBuildLifecycleContributor()
            : this(
                new SimultriaViewerBuildSceneInspector(),
                SimultriaViewerDevelopmentContextSelector.TryResolve,
                new SimultriaViewerBuildContextPreparation(),
                new SimultriaViewerBuildArtifactValidator())
        {
        }

        internal SimultriaViewerBuildLifecycleContributor(
            ISimultriaViewerBuildSceneInspector buildSceneInspector,
            SimultriaViewerDevelopmentContextSelection developmentSelector,
            ISimultriaViewerBuildContextPreparation contextPreparation,
            ISimultriaViewerBuildArtifactValidator buildArtifactValidator)
        {
            sceneInspector = buildSceneInspector ??
                throw new ArgumentNullException(nameof(buildSceneInspector));
            contextSelector = developmentSelector ??
                throw new ArgumentNullException(nameof(developmentSelector));
            preparation = contextPreparation ??
                throw new ArgumentNullException(nameof(contextPreparation));
            artifactValidator = buildArtifactValidator ??
                throw new ArgumentNullException(nameof(buildArtifactValidator));
        }

        public string Id => ContributorId;
        public int Order => 100;

        public bool AppliesTo(DeucarianBuildRequest request)
        {
            if (!sceneInspector.TryInspect(
                    request,
                    out SimultriaViewerBuildSceneSnapshot snapshot,
                    out _) ||
                snapshot == null)
            {
                return false;
            }

            return snapshot.ContainsGate || snapshot.SourceSettings.Count > 0;
        }

        public DeucarianBuildValidationResult ValidateBeforeBuild(
            DeucarianBuildRequest request)
        {
            return Evaluate(
                request,
                out _,
                out _);
        }

        public IDisposable Prepare(DeucarianBuildRequest request)
        {
            DeucarianBuildValidationResult validation = Evaluate(
                request,
                out SimultriaViewerDevelopmentContext profile,
                out ApiEnvironmentId effectiveEnvironmentId);
            if (!validation.IsValid)
            {
                throw new BuildFailedException(
                    validation.Format(
                        "Simultria viewer build preparation failed"));
            }

            return preparation.Prepare(
                request.Environment,
                profile,
                effectiveEnvironmentId);
        }

        public DeucarianBuildValidationResult ValidateGeneratedArtifacts(
            DeucarianBuildRequest request,
            DeucarianBuildArtifactManifest manifest)
        {
            return artifactValidator.Validate(request, manifest) ??
                   InvalidResult(
                       "The Simultria viewer artifact validator returned no " +
                       "result.");
        }

        private DeucarianBuildValidationResult Evaluate(
            DeucarianBuildRequest request,
            out SimultriaViewerDevelopmentContext developmentProfile,
            out ApiEnvironmentId developmentEnvironment)
        {
            developmentProfile = null;
            developmentEnvironment = default(ApiEnvironmentId);
            var result = new DeucarianBuildValidationResult();
            if (!sceneInspector.TryInspect(
                    request,
                    out SimultriaViewerBuildSceneSnapshot scene,
                    out string sceneIssue))
            {
                result.Add(sceneIssue);
                return result;
            }

            if (!string.IsNullOrWhiteSpace(scene.InspectionIssue))
            {
                result.Add(scene.InspectionIssue);
                return result;
            }

            if (scene.GateCount != 1)
            {
                result.Add(
                    "The selected viewer scene must contain exactly one " +
                    "Simultria viewer build connection gate.");
                return result;
            }

            SimultriaViewerBuildConfiguration configuration =
                scene.Configuration;
            if (configuration == null)
            {
                result.Add(
                    "The Simultria viewer build connection gate must reference " +
                    "exactly one build configuration.");
                return result;
            }

            ValidateConfiguration(scene, configuration, result);
            if (!result.IsValid)
            {
                return result;
            }

            if (request == null ||
                request.Environment != DeucarianBuildEnvironment.Development)
            {
                return result;
            }

            ValidateDevelopmentContext(
                configuration,
                out developmentProfile,
                out developmentEnvironment,
                result);
            return result;
        }

        private static void ValidateConfiguration(
            SimultriaViewerBuildSceneSnapshot scene,
            SimultriaViewerBuildConfiguration configuration,
            DeucarianBuildValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(configuration.Product))
            {
                result.Add(
                    "The Simultria viewer build configuration requires a " +
                    "nonblank product identifier.");
            }

            ApiConnectionSettings connection = configuration.ConnectionSettings;
            if (connection == null)
            {
                result.Add(
                    "The Simultria viewer build configuration requires API " +
                    "connection settings.");
                return;
            }

            if (scene.SourceSettings.Count == 0)
            {
                result.Add(
                    "The selected viewer scene requires at least one product " +
                    "connection-settings source.");
            }
            else
            {
                for (int index = 0; index < scene.SourceSettings.Count; index++)
                {
                    if (!ReferenceEquals(
                            scene.SourceSettings[index],
                            connection))
                    {
                        result.Add(
                            "Every viewer feature connection-settings source " +
                            "must reference the build configuration connection.");
                        break;
                    }
                }
            }

            if (!configuration.TryCreateComposition(
                    out ApiComposition composition,
                    out _))
            {
                result.Add(
                    "The Simultria viewer API composition is unavailable.");
                return;
            }

            ApiEnvironmentId directory =
                configuration.BuildDirectoryEnvironmentId;
            if (directory.IsEmpty ||
                !composition.GetEnvironmentStatus(directory).IsResolved)
            {
                result.Add(
                    "The Simultria viewer build-directory environment must be " +
                    "explicit and resolved.");
            }

            for (int index = 0; index < PromotableEnvironments.Length; index++)
            {
                if (!composition.GetEnvironmentStatus(
                        PromotableEnvironments[index]).IsResolved)
                {
                    result.Add(
                        "Every promotable Simultria environment must be " +
                        "resolved before building.");
                    break;
                }
            }
        }

        private void ValidateDevelopmentContext(
            SimultriaViewerBuildConfiguration configuration,
            out SimultriaViewerDevelopmentContext profile,
            out ApiEnvironmentId environment,
            DeucarianBuildValidationResult result)
        {
            profile = null;
            environment = default(ApiEnvironmentId);
            if (!contextSelector(out profile, out _, out _) || profile == null)
            {
                result.Add(
                    "Select one Simultria viewer development context before " +
                    "building for Development.");
                return;
            }

            if (!ReferenceEquals(
                    profile.ConnectionSettingsReference,
                    configuration.ConnectionSettings))
            {
                result.Add(
                    "The selected development context and build configuration " +
                    "must reference the same API connection settings.");
                return;
            }

            if (profile.EnvironmentResolutionMode !=
                SimultriaViewerEnvironmentResolutionMode.Manual)
            {
                result.Add(
                    "Development builds require an explicit Manual environment; " +
                    "Automatic environment resolution is not available during " +
                    "synchronous build preparation.");
                return;
            }

            environment = profile.ConfiguredEnvironmentId;
            if (environment.IsEmpty ||
                !configuration.TryCreateComposition(
                    out ApiComposition composition,
                    out _) ||
                !composition.GetEnvironmentStatus(environment).IsResolved)
            {
                result.Add(
                    "The selected Manual development environment must be " +
                    "explicit and resolved.");
                return;
            }

            if (!profile.TryCreatePayload(
                    1,
                    environment,
                    out SimultriaViewerInitializationPayload payload,
                    out _))
            {
                result.Add(
                    "The selected development context cannot create a valid " +
                    "credential-free initialization payload.");
                return;
            }

            try
            {
                string json = SimultriaViewerInitializationCommand.Serialize(
                    SimultriaViewerInitializationCommand.Create(payload));
                if (!SimultriaViewerBuildContextValidator.TryValidateJson(
                        json,
                        out _))
                {
                    result.Add(
                        "The selected development context cannot create a valid " +
                        "credential-free initialization payload.");
                }
            }
            catch (Exception)
            {
                result.Add(
                    "The selected development context cannot create a valid " +
                    "credential-free initialization payload.");
            }
        }

        private static DeucarianBuildValidationResult InvalidResult(
            string issue)
        {
            var result = new DeucarianBuildValidationResult();
            result.Add(issue);
            return result;
        }
    }
}
