namespace Deucarian.SimultriaViewerIntegration.Editor
{
    /// <summary>Resolves one shared project default with an explicit local override.</summary>
    public static class SimultriaViewerDevelopmentContextSelector
    {
        public static bool TryResolve(
            out SimultriaViewerDevelopmentContext profile,
            out string source,
            out string error)
        {
            SimultriaViewerConnectionUserSettings user =
                SimultriaViewerConnectionUserSettings.instance;
            if (user.UseLocalProfileOverride)
            {
                profile = user.LocalProfile;
                source = "Local override";
                if (profile == null)
                {
                    error = "Local context override is enabled but no context is assigned.";
                    return false;
                }

                error = null;
                return true;
            }

            profile = SimultriaViewerConnectionProjectSettings.instance.DefaultProfile;
            source = "Project default";
            if (profile == null)
            {
                error = "No project-default Simultria viewer development context is assigned.";
                return false;
            }

            error = null;
            return true;
        }

        internal static SimultriaViewerDevelopmentContext Resolve(
            SimultriaViewerDevelopmentContext projectDefault,
            bool useLocalOverride,
            SimultriaViewerDevelopmentContext localOverride)
        {
            return useLocalOverride ? localOverride : projectDefault;
        }
    }
}
