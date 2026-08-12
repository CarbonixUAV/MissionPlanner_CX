using GMap.NET.MapProviders;
using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Carbonix.MapTiles
{
    /// <summary>
    /// Turns the enabled tilesets into the list of tile layers a map draws over
    /// its selected base map.
    ///
    /// Nothing here touches a control's MapProvider. The user's map type stays
    /// exactly what they picked, and Mission Planner's provider list, its map
    /// type dropdown and its persisted MapType setting are all left alone --
    /// these layers ride on GMapControl.ExtraOverlays, which is additive and per
    /// control.
    /// </summary>
    public class OverlayStack
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        static readonly GMapProvider[] None = new GMapProvider[0];

        // Keyed by tileset identity and never pruned: GMapProvider forbids two
        // providers sharing an Id and its static registry never releases
        // anything, so the process only ever gets one chance to create each of
        // these. They have to outlive catalog rescans, and any OverlayStack.
        static readonly Dictionary<Guid, OverlayTileProvider> providers =
            new Dictionary<Guid, OverlayTileProvider>();

        static readonly object providers_lock = new object();

        TileSetCatalog catalog;
        volatile GMapProvider[] layers = None;

        /// <summary>Raised after the layer list changes.</summary>
        public event Action Changed;

        /// <summary>
        /// The layers to draw, in catalog order -- later entries draw over
        /// earlier ones. Empty when no tileset is switched on.
        /// </summary>
        public GMapProvider[] Layers
        {
            get { return layers; }
        }

        public void Attach(TileSetCatalog value)
        {
            if (catalog != null)
            {
                catalog.Changed -= Rebuild;
            }

            catalog = value;

            if (catalog != null)
            {
                catalog.Changed += Rebuild;
            }

            Rebuild();
        }

        void Rebuild()
        {
            lock (providers_lock)
            {
                var active = catalog?.ActiveSources() ?? new List<ITileSource>();
                var live = new HashSet<Guid>();
                var stack = new List<GMapProvider>();

                foreach (var source in active)
                {
                    live.Add(source.Id);

                    OverlayTileProvider provider;
                    if (providers.TryGetValue(source.Id, out provider))
                    {
                        // Same tileset identity, possibly a replaced file
                        provider.Source = source;
                    }
                    else
                    {
                        provider = OverlayTileProvider.Create(source.Id, source);
                        providers[source.Id] = provider;
                    }

                    stack.Add(provider);
                }

                // Detach providers whose tileset is gone or switched off, so they
                // cannot serve tiles from a source that is about to be disposed.
                foreach (var kv in providers.Where(p => !live.Contains(p.Key)))
                {
                    kv.Value.Source = null;
                }

                layers = stack.Count > 0 ? stack.ToArray() : None;

                if (log.IsInfoEnabled)
                {
                    log.InfoFormat("overlays: {0}", stack.Count == 0
                        ? "(none)"
                        : string.Join(" + ", stack.Select(o => o.Name)));
                }
            }

            Changed?.Invoke();
        }
    }
}
