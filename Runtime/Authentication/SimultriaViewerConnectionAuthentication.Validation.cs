using System;
using Deucarian.API.Configuration;
using Deucarian.API.Core;
using Deucarian.API.Models;
using Deucarian.Simultria.API.Authentication;
using Deucarian.Simultria.API.Configuration;
using Deucarian.Authentication;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration
{
    public static partial class SimultriaViewerConnectionAuthentication
    {
#if UNITY_EDITOR
        internal static bool TryValidateTarget(
            AuthenticationTarget target,
            ApiConnectionSettings expectedProfile,
            ApiEnvironmentId expectedEnvironmentId,
            out string error)
        {
            ApiComposition expectedComposition = null;
            if (expectedProfile != null &&
                !SimultriaApiConnectionSettingsAdapter.TryCreateComposition(
                    expectedProfile,
                    out expectedComposition,
                    out error))
            {
                return false;
            }

            return TryValidateTarget(
                target,
                expectedProfile,
                expectedComposition,
                expectedEnvironmentId,
                out error);
        }

        internal static bool TryValidateTarget(
            AuthenticationTarget target,
            SimultriaViewerDevelopmentContext expectedProfile,
            ApiEnvironmentId expectedEnvironmentId,
            out string error)
        {
            return TryValidateTarget(
                target,
                expectedProfile?.ConnectionSettingsReference,
                expectedEnvironmentId,
                out error);
        }
#endif

        private static bool TryValidateTarget(
            AuthenticationTarget target,
            ScriptableObject expectedProfile,
            ApiComposition expectedComposition,
            ApiEnvironmentId expectedEnvironmentId,
            out string error)
        {
            if (target == null ||
                !string.Equals(
                    target.Id,
                    DefaultTargetId,
                    StringComparison.Ordinal))
            {
                error =
                    "The stable Simultria viewer authentication target is not registered.";
                return false;
            }

            if (!(target.AcquisitionProvider is
                    SimultriaAuthenticationProvider provider) ||
                !(target.ValidationProvider is
                    SimultriaAuthenticationProvider validator) ||
                !ReferenceEquals(provider, validator))
            {
                error =
                    "The stable viewer target is not backed by one authoritative Simultria authentication provider.";
                return false;
            }

            AuthenticationBinding binding;
            lock (BindingGate)
            {
                Bindings.TryGetValue(provider, out binding);
            }

            if (binding == null ||
                !ReferenceEquals(binding.Composition, provider.Composition) ||
                binding.EnvironmentId != provider.EnvironmentId)
            {
                error =
                    "The Simultria authentication target has no trusted connection binding.";
                return false;
            }

            if (expectedProfile == null)
            {
                error = null;
                return true;
            }

            if (!(expectedProfile is ApiConnectionSettings expectedSettings) ||
                !ReferenceEquals(binding.Profile, expectedProfile) ||
                binding.EnvironmentId != expectedEnvironmentId ||
                provider.EnvironmentId != expectedEnvironmentId ||
                string.IsNullOrWhiteSpace(binding.CompositionFingerprint) ||
                !SimultriaViewerConnectionCompositionFingerprint.TryCreate(
                    expectedSettings,
                    expectedComposition,
                    expectedEnvironmentId,
                    out string expectedFingerprint) ||
                !string.Equals(
                    binding.CompositionFingerprint,
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                error =
                    "The registered Simultria authentication environment does not match the selected development context.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
