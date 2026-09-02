using System;
using System.Collections.Generic;
using System.IO;
using Deucarian.BuildPipeline;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    internal interface ISimultriaViewerBuildArtifactValidator
    {
        DeucarianBuildValidationResult Validate(
            DeucarianBuildRequest request,
            DeucarianBuildArtifactManifest manifest);
    }

    internal sealed class SimultriaViewerBuildArtifactValidator :
        ISimultriaViewerBuildArtifactValidator
    {
        private static readonly string CurrentFileName = Path.GetFileName(
            SimultriaViewerWebGlDevelopmentExporter.ExportAssetPath);
        private static readonly string LegacyFileName = Path.GetFileName(
            SimultriaViewerWebGlDevelopmentExporter.LegacyExportAssetPath);
        private static readonly string CurrentLoadableRelativePath =
            "StreamingAssets/" + CurrentFileName;

        public DeucarianBuildValidationResult Validate(
            DeucarianBuildRequest request,
            DeucarianBuildArtifactManifest manifest)
        {
            var result = new DeucarianBuildValidationResult();
            if (request == null || manifest?.artifacts == null)
            {
                result.Add(
                    "The Simultria viewer build artifact manifest is unavailable.");
                return result;
            }

            List<string> current = FindArtifacts(
                manifest.artifacts,
                CurrentFileName);
            List<string> legacy = FindArtifacts(
                manifest.artifacts,
                LegacyFileName);
            if (request.Environment == DeucarianBuildEnvironment.Production)
            {
                ValidateProduction(request, current, legacy, result);
            }
            else
            {
                ValidateDevelopment(request, current, legacy, result);
            }

            return result;
        }

        private static void ValidateProduction(
            DeucarianBuildRequest request,
            IReadOnlyCollection<string> current,
            IReadOnlyCollection<string> legacy,
            DeucarianBuildValidationResult result)
        {
            if (current.Count == 0 && legacy.Count == 0)
            {
                return;
            }

            DeucarianBuildValidationResult cleanup =
                DeucarianBuildOutputUtility.ValidatePreparation(
                    request.OutputPath,
                    UnityEditor.BuildOptions.None);
            if (!cleanup.IsValid)
            {
                result.Add(
                    "The production output contains a development context " +
                    "and could not be removed safely.");
                return;
            }

            try
            {
                DeucarianBuildOutputUtility.Prepare(
                    request.OutputPath,
                    UnityEditor.BuildOptions.None);
                result.Add(
                    "The production output contained a development context " +
                    "and was removed.");
            }
            catch (Exception)
            {
                result.Add(
                    "The production output contains a development context " +
                    "and could not be removed safely.");
            }
        }

        private static void ValidateDevelopment(
            DeucarianBuildRequest request,
            IReadOnlyList<string> current,
            IReadOnlyCollection<string> legacy,
            DeucarianBuildValidationResult result)
        {
            if (legacy.Count > 0)
            {
                result.Add(
                    "The development output contains the unsupported legacy " +
                    "viewer context file.");
            }

            if (current.Count != 1 ||
                !string.Equals(
                    current[0],
                    CurrentLoadableRelativePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                result.Add(
                    "The development output must contain exactly one current " +
                    "Simultria viewer context file at its WebGL " +
                    "StreamingAssets path.");
                return;
            }

            if (!TryResolveArtifactPath(
                    request.OutputPath,
                    current[0],
                    out string fullPath) ||
                !SimultriaViewerBuildContextValidator.TryValidateFile(
                    fullPath,
                    out _))
            {
                result.Add(
                    "The development output context is invalid or contains " +
                    "credential-like data.");
            }
        }

        private static List<string> FindArtifacts(
            IEnumerable<DeucarianBuildArtifact> artifacts,
            string exactFileName)
        {
            var matches = new List<string>();
            foreach (DeucarianBuildArtifact artifact in artifacts)
            {
                string relativePath = artifact?.relativePath;
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                string normalized = relativePath.Replace('\\', '/');
                if (string.Equals(
                        Path.GetFileName(normalized),
                        exactFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(normalized);
                }
            }

            return matches;
        }

        private static bool TryResolveArtifactPath(
            string outputPath,
            string relativePath,
            out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(outputPath) ||
                string.IsNullOrWhiteSpace(relativePath) ||
                Path.IsPathRooted(relativePath))
            {
                return false;
            }

            try
            {
                string projectRoot =
                    Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
                string outputRoot = Path.GetFullPath(
                    Path.IsPathRooted(outputPath)
                        ? outputPath
                        : Path.Combine(projectRoot, outputPath));
                string candidate = Path.GetFullPath(Path.Combine(
                    outputRoot,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));
                if (!SimultriaViewerProjectFileBoundary.TryNormalize(
                        candidate,
                        outputRoot,
                        out string normalized,
                        out _))
                {
                    return false;
                }

                fullPath = normalized;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

    }
}
