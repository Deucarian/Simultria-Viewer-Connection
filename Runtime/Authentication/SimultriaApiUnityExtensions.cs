using System;
using Deucarian.Simultria.API;
using UnityEngine;

namespace Deucarian.SimultriaViewerIntegration
{
    /// <summary>Unity lifetime binding over an injected API. No global lookup or new connection is performed.</summary>
    public static class SimultriaApiUnityExtensions
    {
        #region Methods
        /// <summary>
        /// Share the injected connection and additionally cancel pending calls when this component is destroyed.
        /// Call on Unity's main thread. Disabling the component does not end its lifetime.
        /// </summary>
        public static SimultriaApi For(this SimultriaApi api, MonoBehaviour owner)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            return api.WithLifetime(owner.destroyCancellationToken);
        }
        #endregion
    }
}
