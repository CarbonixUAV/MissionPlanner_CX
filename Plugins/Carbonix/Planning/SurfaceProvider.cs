using System;
using System.IO;
using MissionPlanner.Utilities;

namespace Carbonix.Planning
{
    /// <summary>
    /// Wraps a single GeoTIFF surface (e.g. the floor_surface / ceiling_surface max-filtered
    /// SRTM products) loaded independently of MissionPlanner's shared terrain index, exposing
    /// an absolute-AMSL sample at a lat/lng. These are continent-scale tiled COGs, read
    /// scanline-by-scanline on demand by <see cref="GeoTiff"/>, so loading costs disk, not RAM.
    ///
    /// Loaded with addToIndex:false so sampling these does not leak into the app's general
    /// terrain lookups. <see cref="SampleAmsl"/> returns null outside coverage or on NODATA,
    /// so callers simply skip drawing the reference line there.
    /// </summary>
    public class SurfaceProvider
    {
        private GeoTiff.geotiffdata data;

        public string FilePath { get; private set; }
        public bool Loaded => data != null;

        /// <summary>Load (or clear, if path is null/missing) the surface. Returns true on success.</summary>
        public bool Load(string path)
        {
            data = null;
            FilePath = path;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            try
            {
                var d = new GeoTiff.geotiffdata();
                if (d.LoadFile(path, addToIndex: false))
                    data = d;
            }
            catch
            {
                data = null;
            }

            return data != null;
        }

        /// <summary>Absolute terrain altitude (m AMSL) at lat/lng, or null outside coverage.</summary>
        public double? SampleAmsl(double lat, double lng)
        {
            if (data == null) return null;
            var r = GeoTiff.sampleTiff(data, lat, lng);
            return r.currenttype == srtm.tiletype.valid ? (double?)r.alt : null;
        }
    }
}
