using System;
using System.Collections.Generic;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Configuration;

namespace Deucarian.SimultriaViewerIntegration.Editor
{
    // Pure option construction belongs outside the stateful development window.
    // The legacy directory option adapter is retained for compatibility only;
    // it does not select or configure the fixed Production lookup endpoint.
    internal static class SimultriaViewerEnvironmentOptions
    {
        internal static void BuildEnvironmentOptions(
            ApiEnvironmentId current,
            out string[] options,
            out ApiEnvironmentId[] values,
            out int selectedIndex)
        {
            ApiEnvironmentId fallbackCurrent = current.IsEmpty
                ? SimultriaEnvironmentIds.Development
                : current;
            var optionLabels = new List<string>();
            var optionValues = new List<ApiEnvironmentId>();
            foreach (var descriptor in SimultriaEnvironmentDescriptors.All)
            {
                ApiEnvironmentId environmentId = descriptor.EnvironmentId;
                if (environmentId.IsEmpty)
                {
                    continue;
                }

                optionLabels.Add(descriptor.DisplayName);
                optionValues.Add(environmentId);
            }

            selectedIndex = FindOptionIndex(optionValues, fallbackCurrent);
            if (selectedIndex < 0 && !current.IsEmpty)
            {
                selectedIndex = optionLabels.Count;
                optionLabels.Add($"Custom ({current.Value})");
                optionValues.Add(current);
            }

            options = optionLabels.ToArray();
            values = optionValues.ToArray();
        }

        internal static void BuildDirectoryEnvironmentOptions(
            ApiEnvironmentId current,
            out string[] options,
            out ApiEnvironmentId[] values,
            out int selectedIndex)
        {
            BuildEnvironmentOptions(
                current,
                out string[] canonicalLabels,
                out ApiEnvironmentId[] canonicalValues,
                out int canonicalIndex);
            options = new string[canonicalLabels.Length + 1];
            values = new ApiEnvironmentId[canonicalValues.Length + 1];
            options[0] = "Choose configured environment...";
            Array.Copy(canonicalLabels, 0, options, 1, canonicalLabels.Length);
            Array.Copy(canonicalValues, 0, values, 1, canonicalValues.Length);
            selectedIndex = current.IsEmpty ? 0 : canonicalIndex + 1;
        }

        private static int FindOptionIndex(
            List<ApiEnvironmentId> optionValues,
            ApiEnvironmentId selected)
        {
            string selectedValue = selected.Value;
            for (int i = 0; i < optionValues.Count; i++)
            {
                if (string.Equals(
                        optionValues[i].Value,
                        selectedValue,
                        StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
