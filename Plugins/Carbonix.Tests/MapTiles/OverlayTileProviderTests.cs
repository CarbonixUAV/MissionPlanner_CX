using Carbonix.MapTiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace Carbonix.Tests.MapTiles
{
    /// <summary>
    /// Covers how one tileset presents itself to GMap as a tile layer.
    ///
    /// The interesting behaviour here is all about what a miss means. A sparse
    /// overlay has most of its tiles pruned at export, so "no tile" usually
    /// means "nothing here" -- but past the depth it was built at it means
    /// "I do not go that deep", and those two have to render differently.
    /// </summary>
    [TestClass]
    public class OverlayTileProviderTests
    {
        static string Fixture(string name)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name);
        }

        [TestMethod]
        public void OptsOutOfParentTileSubstitution()
        {
            // Without this GMap answers every miss by magnifying a parent tile
            // of the same layer, painting overlay content across ground the
            // export says has none -- and blitting a full transparent tile per
            // position per repaint to do it.
            using (var src = new MbTilesTileSource(Fixture("sample.mbtiles")))
            {
                var provider = OverlayTileProvider.Create(Guid.NewGuid(), src);

                Assert.IsFalse(provider.FillEmptyTiles);
                Assert.IsTrue(provider.BypassCache, "the tileset is its own store");
            }
        }

        [TestMethod]
        public void ReportsTheTilesetsMaxZoomSoOverzoomStillWorks()
        {
            // Opting out of empty-tile filling must not also drop the layer when
            // you zoom past what it was exported at. Core magnifies a parent for
            // any layer asked above its own MaxZoom, so this has to be the
            // tileset's real maximum rather than a blanket ceiling -- otherwise a
            // z16 overlay silently vanishes at z17 instead of going blurry.
            using (var src = new MbTilesTileSource(Fixture("sample.mbtiles")))
            {
                var provider = OverlayTileProvider.Create(Guid.NewGuid(), src);

                Assert.AreEqual(4, src.MaxZoom);
                Assert.AreEqual(4, provider.MaxZoom);
            }
        }

        [TestMethod]
        public void MaxZoomFollowsAReplacedTileset()
        {
            // A rescan swaps the source on the existing provider instead of
            // building a new one, so the zoom ceiling has to move with it.
            using (var shallow = new MbTilesTileSource(Fixture("sample.mbtiles")))
            using (var deep = new MbTilesTileSource(Fixture("surat.mbtiles")))
            {
                Assert.AreNotEqual(shallow.MaxZoom, deep.MaxZoom,
                    "fixtures must differ for this to prove anything");

                var provider = OverlayTileProvider.Create(Guid.NewGuid(), shallow);
                Assert.AreEqual(shallow.MaxZoom, provider.MaxZoom);

                provider.Source = deep;
                Assert.AreEqual(deep.MaxZoom, provider.MaxZoom);
            }
        }

        [TestMethod]
        public void RemovedTilesetLeavesTheProviderInert()
        {
            // The instance outlives the tileset -- GMap's registry never releases
            // it -- so it has to stop serving once the source is taken away.
            using (var src = new MbTilesTileSource(Fixture("sample.mbtiles")))
            {
                var provider = OverlayTileProvider.Create(Guid.NewGuid(), src);

                provider.Source = null;

                Assert.IsNull(provider.GetTileImage(new GMap.NET.GPoint(9, 6), 4));
                Assert.AreEqual("(removed)", provider.Name);
            }
        }
    }
}
