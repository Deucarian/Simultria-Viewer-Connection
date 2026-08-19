namespace Deucarian.SimultriaViewerConnection.Editor
{
    /// <summary>Resolves one shared project default with an explicit local override.</summary>
    public static class SimultriaViewerDevelopmentProfileSelector
    {
        public static bool TryResolve(
            out SimultriaViewerDevelopmentProfile profile,
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
                    error = "Local profile override is enabled but no profile is assigned.";
                    return false;
                }

                error = null;
                return true;
            }

            profile = SimultriaViewerConnectionProjectSettings.instance.DefaultProfile;
            source = "Project default";
            if (profile == null)
            {
                error = "No project-default Simultria viewer development profile is assigned.";
                return false;
            }

            error = null;
            return true;
        }

        internal static SimultriaViewerDevelopmentProfile Resolve(
            SimultriaViewerDevelopmentProfile projectDefault,
            bool useLocalOverride,
            SimultriaViewerDevelopmentProfile localOverride)
        {
            return useLocalOverride ? localOverride : projectDefault;
        }
    }
}
