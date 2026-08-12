using Carbonix.MapTiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Carbonix.Tests.MapTiles
{
    /// <summary>
    /// The fixtures store plain ASCII payloads rather than real images, because
    /// MbTilesTileSource hands back the stored bytes untouched. That lets these
    /// assert exact round-trips -- most importantly that the MBTiles TMS row
    /// flip goes the right way, which is silent and invisible when wrong (the
    /// map just shows the wrong place).
    /// </summary>
    [TestClass]
    public class MbTilesTileSourceTests
    {
        static string Fixture(string name)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name);
        }

        static string Text(byte[] bytes)
        {
            return bytes == null ? null : Encoding.ASCII.GetString(bytes);
        }

        [TestMethod]
        public void ReadsMetadata()
        {
            using (var src = new MbTilesTileSource(Fixture("sample.mbtiles")))
            {
                Assert.AreEqual("Test Corridor", src.Name);
                Assert.AreEqual("png", src.Format);
                Assert.AreEqual("baselayer", src.LayerType);
                Assert.AreEqual("TEST-001", src.Reference);
                Assert.AreEqual(new Guid("11111111-2222-3333-4444-555555555555"), src.Id);
                Assert.AreEqual(2, src.MinZoom);
                Assert.AreEqual(4, src.MaxZoom);
                Assert.AreEqual(3, src.TileCount);
                Assert.IsFalse(src.IsExpired);

                Assert.IsTrue(src.Bounds.HasValue);
                Assert.AreEqual(0.0, src.Bounds.Value.West, 1e-9);
                Assert.AreEqual(45.0, src.Bounds.Value.North, 1e-9);
            }
        }

        [TestMethod]
        public void GetTile_FlipsTmsRow()
        {
            using (var src = new MbTilesTileSource(Fixture("sample.mbtiles")))
            {
                // Stored at TMS row 9 for zoom 4 (16 - 1 - 6), so asking in XYZ
                // terms for y=6 must find it.
                Assert.AreEqual("z4-x9-y6", Text(src.GetTile(9, 6, 4)));
                Assert.AreEqual("z2-x2-y1", Text(src.GetTile(2, 1, 2)));
            }
        }

        [TestMethod]
        public void GetTile_WrongRowIsAMiss()
        {
            using (var src = new MbTilesTileSource(Fixture("sample.mbtiles")))
            {
                // Guards against the flip being dropped or applied twice: the
                // unflipped row is inside the declared bounds, so getting the
                // stored payload here would mean tiles rendering at the wrong
                // latitude.
                Assert.IsNull(src.GetTile(9, 9, 4));
            }
        }

        [TestMethod]
        public void GetTile_EmptyInsideRangeIsNull()
        {
            using (var src = new MbTilesTileSource(Fixture("sample.mbtiles")))
            {
                // Empty tiles are pruned at export, so within the declared range
                // absent means empty. OverlayTileProvider clears FillEmptyTiles,
                // so GMap reads the null as "nothing here" and does not magnify a
                // lower zoom of ours over ground that has no content.
                Assert.IsNull(src.GetTile(10, 7, 4));
            }
        }

        [TestMethod]
        public void GetTile_OutsideDeclaredBoundsIsNull()
        {
            using (var src = new MbTilesTileSource(Fixture("sample.mbtiles")))
            {
                // This tile really is in the file, but the bounds say the
                // tileset does not cover it, so it is never served.
                Assert.IsNull(src.GetTile(0, 0, 4));
            }
        }

        [TestMethod]
        public void GetTile_OutsideZoomRangeIsNull()
        {
            using (var src = new MbTilesTileSource(Fixture("sample.mbtiles")))
            {
                Assert.IsNull(src.GetTile(9, 6, 1));
                Assert.IsNull(src.GetTile(9, 6, 5));
            }
        }

        [TestMethod]
        public void GetTile_AfterDisposeReturnsNull()
        {
            var src = new MbTilesTileSource(Fixture("sample.mbtiles"));
            src.Dispose();
            Assert.IsNull(src.GetTile(9, 6, 4));
        }

        [TestMethod]
        public void DisposeReleasesTheFile()
        {
            // The management UI deletes tilesets, which Windows refuses while a
            // handle is open.
            var copy = Path.Combine(Path.GetTempPath(), "cbx_" + Path.GetRandomFileName() + ".mbtiles");
            File.Copy(Fixture("sample.mbtiles"), copy);

            var src = new MbTilesTileSource(copy);
            src.Dispose();

            File.Delete(copy);
            Assert.IsFalse(File.Exists(copy));
        }

        [TestMethod]
        public void ExpiredTilesetIsFlagged()
        {
            using (var src = new MbTilesTileSource(Fixture("expired.mbtiles")))
            {
                Assert.IsTrue(src.IsExpired);

                // Still readable -- the provider decides to skip it, the source
                // does not pretend to be empty.
                Assert.AreEqual("expired-tile", Text(src.GetTile(9, 6, 4)));
            }
        }

        [TestMethod]
        public void MissingFileThrows()
        {
            Assert.ThrowsException<FileNotFoundException>(
                () => new MbTilesTileSource(Fixture("does-not-exist.mbtiles")));
        }

        [TestMethod]
        public void ParseDate_BareExpiryDateCoversWholeDay()
        {
            // "expires 2027-03-31" reads as valid through that day, not up to
            // midnight at its start.
            var expiry = MbTilesTileSource.ParseDate("2027-03-31", endOfDay: true);
            Assert.IsTrue(expiry.HasValue);
            Assert.AreEqual(new DateTime(2027, 3, 31, 23, 59, 59, DateTimeKind.Utc),
                new DateTime(expiry.Value.Year, expiry.Value.Month, expiry.Value.Day,
                    expiry.Value.Hour, expiry.Value.Minute, expiry.Value.Second, DateTimeKind.Utc));

            var issued = MbTilesTileSource.ParseDate("2027-03-31", endOfDay: false);
            Assert.AreEqual(TimeSpan.Zero, issued.Value.TimeOfDay);
        }

        [TestMethod]
        public void ParseDate_HandlesJunkAndBlanks()
        {
            Assert.IsNull(MbTilesTileSource.ParseDate(null, true));
            Assert.IsNull(MbTilesTileSource.ParseDate("", true));
            Assert.IsNull(MbTilesTileSource.ParseDate("whenever", true));
        }

        [TestMethod]
        public void ParseBounds_ReadsWestSouthEastNorth()
        {
            var b = MbTilesTileSource.ParseBounds("148.782862,-27.243477,151.209787,-23.733838");
            Assert.IsTrue(b.HasValue);
            Assert.AreEqual(148.782862, b.Value.West, 1e-9);
            Assert.AreEqual(-27.243477, b.Value.South, 1e-9);
            Assert.AreEqual(151.209787, b.Value.East, 1e-9);
            Assert.AreEqual(-23.733838, b.Value.North, 1e-9);
        }

        [TestMethod]
        public void ParseBounds_RejectsMalformed()
        {
            Assert.IsNull(MbTilesTileSource.ParseBounds(null));
            Assert.IsNull(MbTilesTileSource.ParseBounds("1,2,3"));
            Assert.IsNull(MbTilesTileSource.ParseBounds("a,b,c,d"));
        }

        [TestMethod]
        public void DeriveId_IsStableAndCaseInsensitive()
        {
            var a = MbTilesTileSource.DeriveId(@"C:\maps\Origin.mbtiles");
            var b = MbTilesTileSource.DeriveId(@"c:\MAPS\origin.mbtiles");
            var c = MbTilesTileSource.DeriveId(@"C:\maps\Other.mbtiles");

            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }
    }
}
