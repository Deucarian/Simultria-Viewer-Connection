using Deucarian.API.Configuration;

namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>
    /// Exposes the project-owned Simultria API connection selected by a
    /// viewer product feature. Editor validation can use this explicit
    /// contract without reflecting into product components.
    /// </summary>
    public interface ISimultriaViewerConnectionSettingsSource
    {
        ApiConnectionSettings SimultriaViewerConnectionSettings { get; }
    }
}
