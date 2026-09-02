using System;
using System.IO;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    /// <summary>
    /// Validates the narrow, caller-supplied file boundary used by temporary
    /// viewer context and generated-artifact inspection. It deliberately does
    /// not own general build-output preparation policy.
    /// </summary>
    internal static class SimultriaViewerProjectFileBoundary
    {
        internal static string ProjectRoot => Path.GetFullPath(
            Path.GetDirectoryName(Application.dataPath) ?? string.Empty)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        internal static bool TryNormalize(
            string path,
            string projectRoot,
            out string normalized,
            out string issue)
        {
            normalized = string.Empty;
            issue = string.Empty;
            if (string.IsNullOrWhiteSpace(path) ||
                string.IsNullOrWhiteSpace(projectRoot))
            {
                issue = "A project-contained context file path is required.";
                return false;
            }

            try
            {
                string root = Path.GetFullPath(projectRoot).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                string candidate = Path.GetFullPath(path);
                string prefix = root + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, PathComparison) ||
                    string.Equals(candidate, root, PathComparison) ||
                    !HasNoFileSystemLinks(root, candidate))
                {
                    issue = "The viewer context path is not a safe project " +
                            "file path.";
                    return false;
                }

                normalized = candidate;
                return true;
            }
            catch (Exception)
            {
                issue = "The viewer context path could not be inspected safely.";
                return false;
            }
        }

        internal static void RequireSafe(
            string path,
            string projectRoot)
        {
            if (!TryNormalize(
                    path,
                    projectRoot,
                    out _,
                    out string issue))
            {
                throw new InvalidOperationException(issue);
            }
        }

        private static bool HasNoFileSystemLinks(
            string projectRoot,
            string candidate)
        {
            string current = candidate;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return false;
                    }
                }

                if (string.Equals(current, projectRoot, PathComparison))
                {
                    return true;
                }

                string parent = Path.GetDirectoryName(current);
                if (string.IsNullOrWhiteSpace(parent) ||
                    string.Equals(parent, current, PathComparison))
                {
                    return false;
                }

                current = parent;
            }

            return false;
        }

        private static StringComparison PathComparison
        {
            get
            {
#if UNITY_EDITOR_WIN
                return StringComparison.OrdinalIgnoreCase;
#else
                return StringComparison.Ordinal;
#endif
            }
        }
    }
}
