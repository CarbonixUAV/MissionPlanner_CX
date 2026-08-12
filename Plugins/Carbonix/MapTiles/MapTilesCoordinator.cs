using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using log4net;
using MissionPlanner.Plugin;
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Carbonix.MapTiles
{
    /// <summary>
    /// Owns the map tileset feature end to end: the catalog of MBTiles files,
    /// the overlay layer stack, the menu entries that reach them, and the
    /// management window.
    ///
    /// Tilesets are single files, so a whole survey area is one copy rather
    /// than a directory of hundreds of thousands of images.
    ///
    /// The plugin constructs one of these and disposes it on unload; everything
    /// else about the feature stays in this namespace.
    /// </summary>
    public class MapTilesCoordinator : IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        const string KeyExtra = "cbx_tilesets_extra";
        const string KeyDisabled = "cbx_tilesets_disabled";

        readonly PluginHost _host;
        readonly TileSetCatalog _tilesets;
        readonly OverlayStack _stack = new OverlayStack();
        readonly List<string> _base_layers;
        readonly List<ToolStripMenuItem> _layer_menus = new List<ToolStripMenuItem>();

        MapTilesForm _form;

        public MapTilesCoordinator(PluginHost host, IEnumerable<string> baseLayers)
        {
            _host = host;
            _base_layers = (baseLayers ?? Enumerable.Empty<string>()).ToList();

            var folder = Path.Combine(Settings.GetUserDataDirectory(), "mbtiles");
            _tilesets = new TileSetCatalog(folder);
            _tilesets.Restore(
                host.config.GetList(KeyExtra),
                host.config.GetList(KeyDisabled));

            _tilesets.Changed += SaveSettings;
            _tilesets.Reloaded += DropCachedTiles;
            _tilesets.Rescan();

            _stack.Attach(_tilesets);
            _stack.Changed += ApplyOverlays;
            ApplyOverlays();

            AddMapLayersMenu(host.FDMenuMap);
            AddMapLayersMenu(host.FPMenuMap);

            // The menu follows the catalog, which only changes when a file is
            // added, removed, or reloaded, or a tileset is switched on or off.
            // Subscribed after the initial build so loading does not rebuild
            // what was just built.
            _tilesets.Changed += RebuildLayerMenus;
        }

        void SaveSettings()
        {
            SaveList(KeyExtra, _tilesets.ExtraPaths);
            SaveList(KeyDisabled, _tilesets.DisabledKeys);
        }

        void SaveList(string key, IEnumerable<string> values)
        {
            var list = values.ToList();
            if (list.Count > 0)
            {
                _host.config.SetList(key, list);
            }
            else
            {
                // SetList ignores an empty list, so clearing the last entry has
                // to drop the key outright or the old value survives.
                _host.config.Remove(key);
            }
        }

        /// <summary>
        /// Push the current layer stack onto both maps. Additive, so the base
        /// map each control is showing is left exactly as the user set it.
        /// </summary>
        void ApplyOverlays()
        {
            var layers = _stack.Layers;

            Apply(_host.FPGMapControl, layers);
            Apply(_host.FDGMapControl, layers);
        }

        static void Apply(GMapControl map, GMapProvider[] layers)
        {
            if (map == null)
            {
                return;
            }

            try
            {
                map.ExtraOverlays = layers;
            }
            catch (Exception ex)
            {
                log.Error("could not apply map overlays", ex);
            }
        }

        /// <summary>
        /// Tiles are cached per provider DbId, and each tileset keeps a stable
        /// id across a rescan -- so a file replaced in place would otherwise keep
        /// serving the tiles it held before.
        /// </summary>
        void DropCachedTiles()
        {
            try
            {
                GMap.NET.GMaps.Instance.MemoryCache.Clear();
            }
            catch (Exception ex)
            {
                log.Error("could not clear the tile memory cache", ex);
            }
        }

        /// <summary>
        /// One submenu covering what the map is showing: which base map, which
        /// overlays, and a way into the window for everything else.
        /// </summary>
        void AddMapLayersMenu(ContextMenuStrip menu)
        {
            if (menu == null)
            {
                return;
            }

            var parent = new ToolStripMenuItem("Map Layers");
            _layer_menus.Add(parent);
            menu.Items.Add(parent);

            FillMapLayersMenu(parent);

            // Only the tick moves here. Which base map is selected can change
            // from the Plan screen dropdown, which the catalog knows nothing
            // about, and this is a handful of boolean assignments rather than a
            // rebuild -- nothing is created, removed or disposed while the
            // dropdown is in the middle of opening.
            parent.DropDownOpening += (s, e) => MarkCurrentBaseMap(parent);
        }

        void RebuildLayerMenus()
        {
            // Raised from whoever changed the catalog -- the management window,
            // usually. A menu that cannot be rebuilt is not worth taking that
            // caller down for, and leaving the old items in place is a better
            // failure than an empty submenu.
            try
            {
                foreach (var parent in _layer_menus)
                {
                    FillMapLayersMenu(parent);
                }
            }
            catch (Exception ex)
            {
                log.Error("could not rebuild the map layers menu", ex);
            }
        }

        void FillMapLayersMenu(ToolStripMenuItem parent)
        {
            // Detach first, then dispose. Disposing a ToolStripItem takes it out
            // of its parent collection, so disposing while enumerating that
            // collection modifies what is being walked.
            var previous = parent.DropDownItems.Cast<ToolStripItem>().ToArray();
            parent.DropDownItems.Clear();
            foreach (var item in previous)
            {
                item.Dispose();
            }

            AddBaseMapItems(parent);
            AddOverlayItems(parent);

            parent.DropDownItems.Add(new ToolStripSeparator());

            var manage = new ToolStripMenuItem("Manage Tilesets...");
            manage.Click += (s, e) => ShowWindow();
            parent.DropDownItems.Add(manage);
        }

        void MarkCurrentBaseMap(ToolStripMenuItem parent)
        {
            var current = _host.FDGMapControl?.MapProvider;

            foreach (var item in parent.DropDownItems.OfType<ToolStripMenuItem>())
            {
                var provider = item.Tag as GMapProvider;
                if (provider != null)
                {
                    item.Checked = ReferenceEquals(provider, current);
                }
            }
        }

        /// <summary>
        /// A short list of base maps, for switching in the field without going
        /// to the Plan screen.
        /// </summary>
        void AddBaseMapItems(ToolStripMenuItem parent)
        {
            var combo = _host.MainForm.FlightPlanner?.comboBoxMapType;
            if (combo == null)
            {
                log.Warn("no map type dropdown - skipping the base map list");
                return;
            }

            var current = _host.FDGMapControl?.MapProvider;

            foreach (var typeName in _base_layers)
            {
                // Matched on type name, not Name -- provider names come from
                // localisable resource strings.
                var provider = GMapProviders.List
                    .FirstOrDefault(p => p.GetType().Name == typeName);

                if (provider == null)
                {
                    log.WarnFormat("base layer '{0}' is not a known map provider", typeName);
                    continue;
                }

                // Drive the Plan screen's dropdown rather than setting the map
                // directly. Mission Planner's own handler then updates both maps
                // and persists Settings["MapType"], and the two selectors stay in
                // agreement -- this menu is a shortcut into the existing one, not
                // a second source of truth.
                var captured = provider;
                var item = new ToolStripMenuItem(provider.Name)
                {
                    // Tag marks this as a base map row, so the tick can be
                    // refreshed on open without rebuilding anything
                    Tag = captured,
                    Checked = ReferenceEquals(provider, current),
                };
                item.Click += (s, e) => combo.SelectedItem = captured;

                parent.DropDownItems.Add(item);
            }
        }

        /// <summary>
        /// The overlays worth offering here: the newest revision of each, and
        /// nothing that has lapsed.
        ///
        /// Choosing an older revision, or overriding an expiry, both stay in the
        /// management window. They are deliberate acts with consequences worth
        /// reading, and a context menu is where things get clicked by accident.
        /// </summary>
        void AddOverlayItems(ToolStripMenuItem parent)
        {
            // Catalog order, matching the window, which is draw order.
            //
            // Expiry is judged when this is built rather than watched. A tileset
            // that lapses while Mission Planner happens to be open keeps its
            // entry until something else changes the catalog, which is a fair
            // trade against polling the clock to keep a menu honest.
            var groups = _tilesets.Groups()
                .Where(g => !g.IsExpired || g.ForcedExpired)
                .ToList();

            if (groups.Count == 0)
            {
                return;
            }

            if (parent.DropDownItems.Count > 0)
            {
                parent.DropDownItems.Add(new ToolStripSeparator());
            }

            foreach (var group in groups)
            {
                var label = group.Name;

                // Never let the menu imply the newest is what is drawing when it
                // is not -- both of these states are chosen in the window and
                // would otherwise be invisible from here.
                if (group.IsPeeking)
                {
                    label += "  (" + (group.Selected.Revision ?? "older revision") + ")";
                }
                if (group.ForcedExpired)
                {
                    label += "  (expired)";
                }

                var key = group.Key;
                var wanted = !group.IsDrawn;
                var item = new ToolStripMenuItem(label) { Checked = group.IsDrawn };
                item.Click += (s, e) => _tilesets.SetEnabled(key, wanted);

                parent.DropDownItems.Add(item);
            }
        }

        void ShowWindow()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new MapTilesForm(_tilesets,
                    () => _host.FDGMapControl?.MapProvider?.Name);
                // Passing an owner keeps it off the back of the main window
                _form.Show(_host.MainForm);
            }
            else
            {
                _form.WindowState = FormWindowState.Normal;
                _form.BringToFront();
            }
        }

        public void Dispose()
        {
            _tilesets.Changed -= SaveSettings;
            _tilesets.Changed -= RebuildLayerMenus;
            _tilesets.Reloaded -= DropCachedTiles;
            _stack.Changed -= ApplyOverlays;

            _stack.Attach(null);

            // Take the layers back off both maps before the sources close
            ApplyOverlays();

            if (_form != null && !_form.IsDisposed)
            {
                _form.Close();
            }

            _tilesets.Dispose();
        }
    }
}
