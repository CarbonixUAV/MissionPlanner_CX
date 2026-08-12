using Carbonix.MapTiles;
using GMap.NET.MapProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace Carbonix.Tests.MapTiles
{
    /// <summary>
    /// Covers the overlay layer stack.
    ///
    /// The stack itself is an ordinary object, but the OverlayTileProvider
    /// instances it hands out are cached process-wide: GMapProvider keeps a
    /// static registry and refuses two providers with the same Id, so each
    /// tileset identity only ever gets one provider for the life of the
    /// process. That is itself part of what is under test.
    /// </summary>
    [TestClass]
    public class OverlayStackTests
    {
        string _folder;
        TileSetCatalog _catalog;
        OverlayStack _stack;

        static string Fixture(string name)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name);
        }

        [TestInitialize]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), "CarbonixStack_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_folder);
            _stack = new OverlayStack();
        }

        [TestCleanup]
        public void Teardown()
        {
            // Detach before disposing, or the stack keeps pointing at sources
            // that are about to be closed.
            _stack.Attach(null);
            _catalog?.Dispose();

            try
            {
                if (Directory.Exists(_folder))
                {
                    Directory.Delete(_folder, true);
                }
            }
            catch (IOException)
            {
            }
        }

        void Place(string fixture)
        {
            File.Copy(Fixture(fixture), Path.Combine(_folder, fixture), true);
        }

        TileSetCatalog Load()
        {
            _catalog = new TileSetCatalog(_folder);
            _catalog.Rescan();
            _stack.Attach(_catalog);
            return _catalog;
        }

        [TestMethod]
        public void StackIsTheEnabledOverlaysOnly()
        {
            // No base map entry: the layers ride on GMapControl.ExtraOverlays,
            // which is additive over whatever provider is selected. Nothing here
            // needs to know or reproduce what that provider is.
            Place("sample.mbtiles");
            Place("surat.mbtiles");
            Load();

            var layers = _stack.Layers;

            Assert.AreEqual(2, layers.Length);
            Assert.IsTrue(layers.All(p => p is OverlayTileProvider));
        }

        [TestMethod]
        public void NoTilesetsMeansNoLayers()
        {
            Load();

            Assert.AreEqual(0, _stack.Layers.Length);
        }

        [TestMethod]
        public void RescanReusesProvidersRatherThanRecreatingThem()
        {
            Place("sample.mbtiles");
            var catalog = Load();

            var first = _stack.Layers[0];

            // GMapProvider's constructor throws on a duplicate Id and its static
            // registry never releases anything, so a rescan must swap the source
            // on the existing provider instead of building a new one. Without
            // that, this second scan takes the plugin down.
            catalog.Rescan();

            Assert.AreSame(first, _stack.Layers[0]);
            Assert.AreEqual(1, _stack.Layers.Length);
        }

        [TestMethod]
        public void DisabledAndExpiredTilesetsLeaveTheStack()
        {
            Place("sample.mbtiles");
            Place("expired.mbtiles");
            var catalog = Load();

            // expired.mbtiles is filtered out by the catalog
            Assert.AreEqual(1, _stack.Layers.Length);

            catalog.SetEnabled(catalog.ActiveSources().Single(), false);
            Assert.AreEqual(0, _stack.Layers.Length);
        }

        [TestMethod]
        public void ReEnablingPutsTheOverlayBack()
        {
            Place("sample.mbtiles");
            var catalog = Load();

            var source = catalog.Sources.Single();
            catalog.SetEnabled(source, false);
            Assert.AreEqual(0, _stack.Layers.Length);

            catalog.SetEnabled(source, true);
            Assert.AreEqual(1, _stack.Layers.Length);
        }

        [TestMethod]
        public void DetachedProvidersStopServingTheirSource()
        {
            // The provider instance outlives the tileset, so it has to let go of
            // a source the catalog is about to dispose.
            Place("sample.mbtiles");
            var catalog = Load();

            var provider = (OverlayTileProvider)_stack.Layers[0];
            Assert.IsNotNull(provider.Source);

            catalog.SetEnabled(catalog.Sources.Single(), false);

            Assert.IsNull(provider.Source);
        }

        [TestMethod]
        public void ChangedFiresWhenTheLayerListChanges()
        {
            Place("sample.mbtiles");
            var catalog = Load();

            var fired = 0;
            _stack.Changed += () => fired++;

            catalog.SetEnabled(catalog.Sources.Single(), false);

            Assert.AreEqual(1, fired);
        }

        [TestMethod]
        public void StaysOutOfTheMapTypeDropdown()
        {
            // Mission Planner's provider list and map type dropdown are left
            // alone -- these layers are never a selectable map type.
            Place("sample.mbtiles");
            Load();

            Assert.IsFalse(GMapProviders.List.OfType<OverlayTileProvider>().Any());
        }
    }
}
