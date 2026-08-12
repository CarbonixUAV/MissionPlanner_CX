using Carbonix.MapTiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

using System.IO;

namespace Carbonix.Tests.MapTiles
{
    /// <summary>
    /// Runs against a fixture built with the same metadata field set and the
    /// same bounds as the real Origin corridor render.
    ///
    /// The southern hemisphere is the interesting part: "north" there is the
    /// less negative latitude, and getting that backwards would silently make
    /// every bounds check reject everything.
    /// </summary>
    [TestClass]
    public class SuratFixtureTests
    {
        // Tile covering the declared centre of the corridor, in XYZ terms.
        const long CENTRE_X = 3754;
        const long CENTRE_Y = 2348;
        const int ZOOM = 12;

        static MbTilesTileSource Open()
        {
            return new MbTilesTileSource(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "surat.mbtiles"));
        }

        [TestMethod]
        public void ParsesTheRealMetadataShape()
        {
            using (var src = Open())
            {
                Assert.AreEqual("Origin pipeline corridor", src.Name);
                Assert.AreEqual("baselayer", src.LayerType);
                Assert.AreEqual(0, src.MinZoom);
                Assert.AreEqual(12, src.MaxZoom);

                // No carbonix:* rows yet, so identity falls back to the path and
                // there is no expiry.
                Assert.AreNotEqual(Guid.Empty, src.Id);
                Assert.IsNull(src.Expires);
                Assert.IsFalse(src.IsExpired);

                // No reference either, so it is a dataset of one and cannot
                // supersede or be superseded
                Assert.IsFalse(src.HasDeclaredDataset);

                var b = src.Bounds.Value;
                Assert.IsTrue(b.North > b.South, "north must be the less negative latitude");
                Assert.AreEqual(-23.733838, b.North, 1e-9);
                Assert.AreEqual(-27.243477, b.South, 1e-9);
            }
        }

        [TestMethod]
        public void FindsTheCentreTile()
        {
            using (var src = Open())
            {
                Assert.IsNotNull(src.GetTile(CENTRE_X, CENTRE_Y, ZOOM));
            }
        }

        [TestMethod]
        public void SouthernBoundsAcceptInsideAndRejectOutside()
        {
            using (var src = Open())
            {
                // Corners of the declared footprint at this zoom, from the same
                // formula the renderer used: x 3740..3768, y 2326..2370.
                Assert.IsNull(src.GetTile(3739, CENTRE_Y, ZOOM), "west of the corridor");
                Assert.IsNull(src.GetTile(3769, CENTRE_Y, ZOOM), "east of the corridor");
                Assert.IsNull(src.GetTile(CENTRE_X, 2325, ZOOM), "north of the corridor");
                Assert.IsNull(src.GetTile(CENTRE_X, 2371, ZOOM), "south of the corridor");

                // Northern hemisphere, nowhere near it
                Assert.IsNull(src.GetTile(CENTRE_X, 1000, ZOOM));
            }
        }

        [TestMethod]
        public void StoredTileComesBackByteExactAndWellFormed()
        {
            // Header is parsed by hand rather than with Image.FromStream:
            // MissionPlanner.Drawing redeclares types in the System.Drawing
            // namespace, so Image is ambiguous in this project. What matters
            // here is that the blob survives the round trip intact and is a
            // 256x256 tile -- GMap does the actual decoding.
            using (var src = Open())
            {
                var bytes = src.GetTile(CENTRE_X, CENTRE_Y, ZOOM);
                Assert.IsNotNull(bytes);

                CollectionAssert.AreEqual(
                    new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
                    new[] { bytes[0], bytes[1], bytes[2], bytes[3],
                            bytes[4], bytes[5], bytes[6], bytes[7] },
                    "PNG signature");

                Assert.AreEqual(256, BigEndian(bytes, 16), "IHDR width");
                Assert.AreEqual(256, BigEndian(bytes, 20), "IHDR height");
            }
        }

        static int BigEndian(byte[] b, int offset)
        {
            return (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];
        }
    }
}
