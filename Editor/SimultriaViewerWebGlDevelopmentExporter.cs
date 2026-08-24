using System;
using System.IO;
using Deucarian.API.Models;
using Deucarian.CommandRouting;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerConnection.Editor
{
    /// <summary>Explicit secret-free export for local browser harnesses.</summary>
    public static class SimultriaViewerWebGlDevelopmentExporter
    {
        public const string ExportAssetPath =
            "Assets/StreamingAssets/simultria-viewer-context.json";

        public static bool TryExport(
            SimultriaViewerDevelopmentProfile profile,
            out string message)
        {
            if (!SimultriaViewerDevelopmentCommandService.TryCreateCommand(
                    profile,
                    out CommandEnvelope command,
                    out message))
            {
                return false;
            }

            return TryExport(command, out message);
        }

        /// <summary>
        /// Exports a credential-free command for an environment that was
        /// already selected by the owning development workflow. This keeps a
        /// local development build independent from automatic runtime routing.
        /// </summary>
        public static bool TryExport(
            SimultriaViewerDevelopmentProfile profile,
            ApiEnvironmentId effectiveEnvironmentId,
            out string message)
        {
            if (!SimultriaViewerDevelopmentCommandService.TryCreateCommand(
                    profile,
                    effectiveEnvironmentId,
                    out CommandEnvelope command,
                    out message))
            {
                return false;
            }

            return TryExport(command, out message);
        }

        internal static bool TryExport(
            CommandEnvelope command,
            out string message)
        {
            if (command == null)
            {
                message = "A resolved initialization command is required.";
                return false;
            }

            try
            {
                string fullPath = ToProjectPath(ExportAssetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllText(
                    fullPath,
                    SimultriaViewerInitializationCommand.Serialize(command));
                AssetDatabase.ImportAsset(
                    ExportAssetPath,
                    ImportAssetOptions.ForceUpdate);
                message = "Exported a credential-free local development command to " +
                          ExportAssetPath + ".";
                return true;
            }
            catch (Exception exception)
            {
                message = "Could not export the local development command: " +
                          exception.Message;
                return false;
            }
        }

        public static bool TryClear(out string message)
        {
            string fullPath = ToProjectPath(ExportAssetPath);
            bool removed = false;
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                removed = true;
            }

            if (File.Exists(fullPath + ".meta"))
            {
                File.Delete(fullPath + ".meta");
                removed = true;
            }

            AssetDatabase.Refresh();
            message = removed
                ? "Cleared the local Simultria viewer development export."
                : "No local Simultria viewer development export was present.";
            return true;
        }

        private static string ToProjectPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot ?? string.Empty, assetPath));
        }
    }
}
