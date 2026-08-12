using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.Projections;
using log4net;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Carbonix.MapTiles
{
    /// <summary>
    /// Presents one tileset to GMap as its own provider, so the compositor can
    /// stack several of them over a base map.
    ///
    /// One instance per tileset identity, created once and reused: GMapProvider's
    /// constructor throws if a provider with the same Id or DbId already exists,
    /// and its static registry never releases anything. Constructing a fresh
    /// provider on every catalog rescan would therefore blow up the second time
    /// a tileset was loaded. Instead the Source is swapped on the existing
    /// instance -- which is also what makes replacing a tileset file in place
    /// work.
    /// </summary>
    public class OverlayTileProvider : GMapProvider
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // GMapProvider's base constructor reads Id to derive DbId, and that runs
        // before this class's constructor body. Field initialisers are the only
        // per-instance code that runs earlier, so Create() parks the guid in a
        // static for the initialiser below to pick up. ThreadStatic rather than
        // a lock: two threads constructing at once then cannot see each other's
        // handoff at all, instead of taking turns at one.
        [ThreadStatic]
        static Guid pending_id;

        readonly Guid id = pending_id;

        volatile ITileSource source;

        public static OverlayTileProvider Create(Guid id, ITileSource source)
        {
            pending_id = id;
            return new OverlayTileProvider(source);
        }

        OverlayTileProvider(ITileSource source)
        {
            // The tileset is its own store, so never copy tiles into Mission
            // Planner's per-tile folder cache.
            BypassCache = true;

            // A miss on a sparse overlay means "nothing here", not "not loaded
            // yet" -- so no magnified parent tile in its place. See
            // MbTilesTileSource.GetTile.
            FillEmptyTiles = false;

            Source = source;

            overlays = new GMapProvider[] { this };
            Register();
        }

        /// <summary>
        /// The tileset behind this provider. Null when the tileset has been
        /// removed -- the instance stays alive but contributes nothing.
        /// </summary>
        public ITileSource Source
        {
            get { return source; }
            set
            {
                source = value;

                // Zooming past the deepest level a tileset was exported at must
                // magnify what it does have rather than drop the layer, so this
                // has to track the tileset -- it is what Core tests to tell
                // "does not go that deep" apart from "nothing here".
                MaxZoom = value?.MaxZoom ?? 24;
            }
        }

        void Register()
        {
            // The folder cache resolves provider names by DbId. Nothing should
            // reach that path while BypassCache is set, but registering keeps a
            // stray lookup from dereferencing null.
            var field = typeof(GMapProviders).GetField("DbHash", BindingFlags.Static | BindingFlags.NonPublic);
            var hash = (Dictionary<int, GMapProvider>)field?.GetValue(null);
            if (hash != null && !hash.ContainsKey(DbId))
            {
                hash.Add(DbId, this);
            }
        }

        readonly GMapProvider[] overlays;

        public override Guid Id => id;

        public override string Name => source?.Name ?? "(removed)";

        public override PureProjection Projection => MercatorProjection.Instance;

        public override GMapProvider[] Overlays => overlays;

        public override PureImage GetTileImage(GPoint pos, int zoom)
        {
            var s = source;
            if (s == null || TileImageProxy == null)
            {
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = s.GetTile(pos.X, pos.Y, zoom);
            }
            catch (Exception ex)
            {
                log.Error("tileset " + s.Name + " failed", ex);
                return null;
            }

            // A miss is normal and cheap: overlays are sparse by design, and
            // empty tiles are pruned at export. Returning null just leaves the
            // layers underneath showing through.
            return bytes != null && bytes.Length > 0 ? TileImageProxy.FromArray(bytes) : null;
        }
    }
}
