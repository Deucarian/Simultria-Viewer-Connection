using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    /// <summary>
    /// Temporarily owns both current and legacy StreamingAssets context files
    /// and restores their exact prior byte content and existence on disposal.
    /// </summary>
    internal sealed class SimultriaViewerBuildContextFileScope : IDisposable
    {
        private readonly FileSnapshot[] snapshots;
        private readonly DirectorySnapshot[] directorySnapshots;
        private readonly Action refresh;
        private readonly string projectRoot;
        private bool disposed;

        internal SimultriaViewerBuildContextFileScope()
            : this(
                ToProjectFullPath(
                    SimultriaViewerWebGlDevelopmentExporter.ExportAssetPath),
                ToProjectFullPath(
                    SimultriaViewerWebGlDevelopmentExporter.ExportAssetPath) +
                ".meta",
                ToProjectFullPath(
                    SimultriaViewerWebGlDevelopmentExporter
                        .LegacyExportAssetPath),
                ToProjectFullPath(
                    SimultriaViewerWebGlDevelopmentExporter
                        .LegacyExportAssetPath) + ".meta",
                AssetDatabase.Refresh,
                SimultriaViewerProjectFileBoundary.ProjectRoot)
        {
        }

        internal SimultriaViewerBuildContextFileScope(
            string currentPath,
            string currentMetaPath,
            string legacyPath,
            string legacyMetaPath,
            Action refreshAction)
            : this(
                currentPath,
                currentMetaPath,
                legacyPath,
                legacyMetaPath,
                refreshAction,
                SimultriaViewerProjectFileBoundary.ProjectRoot)
        {
        }

        internal SimultriaViewerBuildContextFileScope(
            string currentPath,
            string currentMetaPath,
            string legacyPath,
            string legacyMetaPath,
            Action refreshAction,
            string containingProjectRoot)
        {
            if (string.IsNullOrWhiteSpace(containingProjectRoot))
            {
                throw new ArgumentException(
                    "A containing project root is required.",
                    nameof(containingProjectRoot));
            }

            projectRoot = Path.GetFullPath(containingProjectRoot)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            CurrentPath = RequirePath(
                currentPath,
                nameof(currentPath),
                projectRoot);
            CurrentMetaPath = RequirePath(
                currentMetaPath,
                nameof(currentMetaPath),
                projectRoot);
            LegacyPath = RequirePath(
                legacyPath,
                nameof(legacyPath),
                projectRoot);
            LegacyMetaPath = RequirePath(
                legacyMetaPath,
                nameof(legacyMetaPath),
                projectRoot);
            refresh = refreshAction ?? delegate { };
            snapshots = new[]
            {
                new FileSnapshot(CurrentPath, projectRoot),
                new FileSnapshot(CurrentMetaPath, projectRoot),
                new FileSnapshot(LegacyPath, projectRoot),
                new FileSnapshot(LegacyMetaPath, projectRoot)
            };
            directorySnapshots = CreateDirectorySnapshots(
                CurrentPath,
                LegacyPath,
                projectRoot);
        }

        internal string CurrentPath { get; }
        internal string CurrentMetaPath { get; }
        internal string LegacyPath { get; }
        internal string LegacyMetaPath { get; }

        internal void RemoveAll()
        {
            ThrowIfDisposed();
            DeleteIfPresent(CurrentPath, projectRoot);
            DeleteIfPresent(CurrentMetaPath, projectRoot);
            DeleteIfPresent(LegacyPath, projectRoot);
            DeleteIfPresent(LegacyMetaPath, projectRoot);
            refresh();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Exception firstFailure = null;
            for (int index = 0; index < snapshots.Length; index++)
            {
                try
                {
                    snapshots[index].Restore();
                }
                catch (Exception exception)
                {
                    firstFailure = firstFailure ?? exception;
                }
            }

            for (int index = 0; index < directorySnapshots.Length; index++)
            {
                try
                {
                    directorySnapshots[index].Restore();
                }
                catch (Exception exception)
                {
                    firstFailure = firstFailure ?? exception;
                }
            }

            try
            {
                refresh();
            }
            catch (Exception exception)
            {
                firstFailure = firstFailure ?? exception;
            }

            for (int index = 0; index < snapshots.Length; index++)
            {
                try
                {
                    if (!snapshots[index].Matches())
                    {
                        snapshots[index].Restore();
                        if (!snapshots[index].Matches())
                        {
                            throw new IOException(
                                "A viewer context snapshot did not restore " +
                                "to its exact prior state.");
                        }
                    }
                }
                catch (Exception exception)
                {
                    firstFailure = firstFailure ?? exception;
                }
            }


            for (int index = 0; index < directorySnapshots.Length; index++)
            {
                try
                {
                    if (!directorySnapshots[index].Matches())
                    {
                        directorySnapshots[index].Restore();
                        if (!directorySnapshots[index].Matches())
                        {
                            throw new IOException(
                                "A viewer context directory snapshot did " +
                                "not restore to its exact prior state.");
                        }
                    }
                }
                catch (Exception exception)
                {
                    firstFailure = firstFailure ?? exception;
                }
            }

            if (firstFailure != null)
            {
                throw new InvalidOperationException(
                    "The Simultria viewer build context could not be restored.",
                    firstFailure);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }

        private static string RequirePath(
            string path,
            string parameterName,
            string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "A context file path is required.",
                    parameterName);
            }

            if (!SimultriaViewerProjectFileBoundary.TryNormalize(
                    path,
                    projectRoot,
                    out string normalized,
                    out string issue))
            {
                throw new ArgumentException(issue, parameterName);
            }

            return normalized;
        }

        private static string ToProjectFullPath(string assetPath)
        {
            string projectRoot =
                Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static DirectorySnapshot[] CreateDirectorySnapshots(
            string currentPath,
            string legacyPath,
            string projectRoot)
        {
            string currentDirectory = Path.GetDirectoryName(currentPath);
            string legacyDirectory = Path.GetDirectoryName(legacyPath);
            bool currentIsProjectRoot = PathsEqual(
                currentDirectory,
                projectRoot);
            bool legacyIsProjectRoot = PathsEqual(
                legacyDirectory,
                projectRoot);

            if (currentIsProjectRoot && legacyIsProjectRoot)
            {
                return Array.Empty<DirectorySnapshot>();
            }

            if (currentIsProjectRoot)
            {
                return new[]
                {
                    new DirectorySnapshot(legacyDirectory, projectRoot)
                };
            }

            if (legacyIsProjectRoot ||
                PathsEqual(currentDirectory, legacyDirectory))
            {
                return new[]
                {
                    new DirectorySnapshot(currentDirectory, projectRoot)
                };
            }

            return currentDirectory.Length >= legacyDirectory.Length
                ? new[]
                {
                    new DirectorySnapshot(currentDirectory, projectRoot),
                    new DirectorySnapshot(legacyDirectory, projectRoot)
                }
                : new[]
                {
                    new DirectorySnapshot(legacyDirectory, projectRoot),
                    new DirectorySnapshot(currentDirectory, projectRoot)
                };
        }

        private static bool PathsEqual(string left, string right)
        {
#if UNITY_EDITOR_WIN
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
#else
            return string.Equals(left, right, StringComparison.Ordinal);
#endif
        }

        private static void DeleteIfPresent(
            string path,
            string projectRoot)
        {
            SimultriaViewerProjectFileBoundary.RequireSafe(path, projectRoot);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private sealed class FileSnapshot
        {
            private readonly string path;
            private readonly bool existed;
            private readonly byte[] contents;
            private readonly string projectRoot;

            internal FileSnapshot(
                string filePath,
                string containingProjectRoot)
            {
                path = filePath;
                projectRoot = containingProjectRoot;
                SimultriaViewerProjectFileBoundary.RequireSafe(
                    path,
                    projectRoot);
                existed = File.Exists(filePath);
                contents = existed ? File.ReadAllBytes(filePath) : null;
            }

            internal void Restore()
            {
                if (!existed)
                {
                    DeleteIfPresent(path, projectRoot);
                    return;
                }

                SimultriaViewerProjectFileBoundary.RequireSafe(
                    path,
                    projectRoot);
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(path, contents);
            }

            internal bool Matches()
            {
                SimultriaViewerProjectFileBoundary.RequireSafe(
                    path,
                    projectRoot);
                if (File.Exists(path) != existed)
                {
                    return false;
                }

                if (!existed)
                {
                    return true;
                }

                byte[] current = File.ReadAllBytes(path);
                if (current.Length != contents.Length)
                {
                    return false;
                }

                for (int index = 0; index < contents.Length; index++)
                {
                    if (current[index] != contents[index])
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private sealed class DirectorySnapshot
        {
            private readonly string path;
            private readonly bool existed;
            private readonly FileSnapshot meta;
            private readonly string projectRoot;

            internal DirectorySnapshot(
                string directoryPath,
                string containingProjectRoot)
            {
                path = directoryPath;
                projectRoot = containingProjectRoot;
                SimultriaViewerProjectFileBoundary.RequireSafe(
                    path,
                    projectRoot);
                existed = Directory.Exists(path);
                meta = new FileSnapshot(path + ".meta", projectRoot);
            }

            internal void Restore()
            {
                SimultriaViewerProjectFileBoundary.RequireSafe(
                    path,
                    projectRoot);
                if (existed)
                {
                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }

                    meta.Restore();
                    return;
                }

                if (File.Exists(path))
                {
                    throw new IOException(
                        "An initially absent viewer context directory path " +
                        "now contains a file and was left untouched.");
                }

                if (Directory.Exists(path))
                {
                    if (Directory.GetFileSystemEntries(path).Length != 0)
                    {
                        throw new IOException(
                            "An initially absent viewer context directory " +
                            "contains unexpected content and was left " +
                            "untouched.");
                    }

                    Directory.Delete(path, false);
                }

                meta.Restore();
            }

            internal bool Matches()
            {
                SimultriaViewerProjectFileBoundary.RequireSafe(
                    path,
                    projectRoot);
                return !File.Exists(path) &&
                       Directory.Exists(path) == existed &&
                       meta.Matches();
            }
        }
    }
}
