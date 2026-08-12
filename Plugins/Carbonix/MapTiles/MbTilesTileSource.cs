using log4net;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Carbonix.MapTiles
{
    /// <summary>
    /// A tileset backed by a local MBTiles file (a SQLite database holding one
    /// row per tile). Read-only.
    ///
    /// MBTiles numbers rows bottom-up (TMS) while GMap works top-down (XYZ), so
    /// the flip happens here and nowhere else.
    /// </summary>
    public class MbTilesTileSource : ITileSource
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        const double MERCATOR_LAT_LIMIT = 85.05112878;

        readonly object _lock = new object();

        // Per-zoom tile bounds, indexed by (zoom - MinZoom), each {minX, maxX,
        // minY, maxY}. Precomputed so the rejection test below is a few integer
        // compares off the hot path, with no lock and no dictionary lookup.
        // Null when the tileset declares no footprint.
        long[][] _tile_range;

        SQLiteConnection _db;
        SQLiteCommand _select;
        bool _disposed;

        public Guid Id { get; private set; }
        public string Name { get; private set; }
        public string Location { get; private set; }
        public int MinZoom { get; private set; }
        public int MaxZoom { get; private set; }
        public GeoBounds? Bounds { get; private set; }
        public string Reference { get; private set; }
        public DateTime? Expires { get; private set; }
        public DateTime? Generated { get; private set; }
        public string Revision { get; private set; }
        public long SizeBytes { get; private set; }

        public bool HasDeclaredDataset
        {
            get { return !string.IsNullOrWhiteSpace(Reference); }
        }

        /// <summary>
        /// The reference when one was declared, else this file's own id -- an
        /// undeclared file is a dataset of one, which is how unmanaged files
        /// keep behaving as they always did.
        /// </summary>
        public string DatasetKey
        {
            get { return HasDeclaredDataset ? Reference.Trim() : Id.ToString(); }
        }

        long _tile_count = -1;

        /// <summary>
        /// Number of tiles stored. Counted on first read rather than at load:
        /// it is a covering-index scan that only ever feeds the management
        /// window, and paying it per tileset during plugin load delays startup
        /// for a number nothing has asked for yet.
        /// </summary>
        public long TileCount
        {
            get
            {
                lock (_lock)
                {
                    if (_tile_count < 0)
                    {
                        _tile_count = _disposed ? 0 : (Scalar("SELECT COUNT(*) FROM tiles") ?? 0);
                    }
                    return _tile_count;
                }
            }
        }

        /// <summary>Image format as declared by the metadata table (png/jpg).</summary>
        public string Format { get; private set; }

        /// <summary>MBTiles standard type row: baselayer or overlay.</summary>
        public string LayerType { get; private set; }

        public string Description { get; private set; }

        public bool IsExpired
        {
            get { return Expires.HasValue && DateTime.UtcNow > Expires.Value; }
        }

        /// <summary>
        /// Opens the file and reads its metadata. Throws if the file is missing,
        /// unreadable, or is not an MBTiles database.
        /// </summary>
        public MbTilesTileSource(string path)
        {
            Location = Path.GetFullPath(path);

            if (!File.Exists(Location))
            {
                throw new FileNotFoundException("tileset not found", Location);
            }

            SizeBytes = new FileInfo(Location).Length;

            try
            {
                // Pooling off matters: it is what makes Dispose actually release
                // the file handle, so the management UI can delete a tileset
                // that has been loaded.
                _db = new SQLiteConnection(new SQLiteConnectionStringBuilder
                {
                    DataSource = Location,
                    Version = 3,
                    ReadOnly = true,
                    FailIfMissing = true,
                    Pooling = false,
                }.ToString());
                _db.Open();

                ApplyMetadata(ReadMetadata());
                BuildTileRanges();

                // Built once and reused: the parameters are rebound per lookup
                // under _lock rather than a new command being created per tile.
                _select = new SQLiteCommand(
                    "SELECT tile_data FROM tiles " +
                    "WHERE zoom_level = @z AND tile_column = @x AND tile_row = @y", _db);
                _select.Parameters.Add("@z", DbType.Int64);
                _select.Parameters.Add("@x", DbType.Int64);
                _select.Parameters.Add("@y", DbType.Int64);
            }
            catch
            {
                Dispose();
                throw;
            }

            log.InfoFormat("tileset '{0}' z{1}-{2} {3:N1} MB from {4}",
                Name, MinZoom, MaxZoom, SizeBytes / 1024.0 / 1024.0, Location);
        }

        Dictionary<string, string> ReadMetadata()
        {
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var cmd = new SQLiteCommand("SELECT name, value FROM metadata", _db))
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    var key = rd.IsDBNull(0) ? null : rd.GetValue(0) as string;
                    if (!string.IsNullOrEmpty(key))
                    {
                        meta[key] = rd.IsDBNull(1)
                            ? null
                            : Convert.ToString(rd.GetValue(1), CultureInfo.InvariantCulture);
                    }
                }
            }

            return meta;
        }

        /// <summary>Single-value query, or null when there is no row.</summary>
        long? Scalar(string sql)
        {
            using (var cmd = new SQLiteCommand(sql, _db))
            {
                var v = cmd.ExecuteScalar();
                return v == null || v == DBNull.Value ? (long?)null : Convert.ToInt64(v);
            }
        }

        void ApplyMetadata(Dictionary<string, string> meta)
        {
            Name = Get(meta, "name");
            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = Path.GetFileNameWithoutExtension(Location);
            }

            Format = Get(meta, "format");
            LayerType = Get(meta, "type");
            Description = Get(meta, "description");

            Reference = Get(meta, "carbonix:reference");
            Expires = ParseDate(Get(meta, "carbonix:expires"), endOfDay: true);
            Generated = ParseDate(Get(meta, "carbonix:generated"), endOfDay: false);

            // "version" is the MBTiles standard row and is what the exporter
            // already writes the revision into, so accept either.
            Revision = Get(meta, "carbonix:revision") ?? Get(meta, "version");

            Guid id;
            if (Guid.TryParse(Get(meta, "carbonix:id") ?? "", out id))
            {
                Id = id;
            }
            else
            {
                // No declared identity, so derive one from the path. Stable for
                // as long as the file stays put, which is enough to remember an
                // enabled/disabled choice.
                Id = DeriveId(Location);
            }

            Bounds = ParseBounds(Get(meta, "bounds"));

            int zmin, zmax;
            if (!int.TryParse(Get(meta, "minzoom"), out zmin) ||
                !int.TryParse(Get(meta, "maxzoom"), out zmax))
            {
                // tile_index has zoom_level as its leading column, so these are
                // cheap even on a large file.
                var lo = Scalar("SELECT MIN(zoom_level) FROM tiles");
                var hi = Scalar("SELECT MAX(zoom_level) FROM tiles");
                if (!lo.HasValue || !hi.HasValue)
                {
                    throw new IOException("no tiles and no zoom metadata - not a usable MBTiles file");
                }
                zmin = (int)lo.Value;
                zmax = (int)hi.Value;
            }
            MinZoom = Math.Max(0, zmin);
            MaxZoom = Math.Max(MinZoom, zmax);
        }

        static string Get(Dictionary<string, string> meta, string key)
        {
            string v;
            return meta.TryGetValue(key, out v) ? v : null;
        }

        /// <summary>
        /// Parses ISO 8601. A bare date used as an expiry means "valid through
        /// that day", so it resolves to the end of it rather than midnight.
        /// </summary>
        internal static DateTime? ParseDate(string s, bool endOfDay)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return null;
            }

            DateTime parsed;
            if (!DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
            {
                log.WarnFormat("could not parse date '{0}'", s);
                return null;
            }

            if (endOfDay && parsed.TimeOfDay == TimeSpan.Zero && s.Trim().Length <= 10)
            {
                parsed = parsed.AddDays(1).AddTicks(-1);
            }
            return parsed;
        }

        internal static GeoBounds? ParseBounds(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return null;
            }

            var parts = s.Split(',');
            if (parts.Length != 4)
            {
                return null;
            }

            var v = new double[4];
            for (int i = 0; i < 4; i++)
            {
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out v[i]))
                {
                    return null;
                }
            }

            return new GeoBounds { West = v[0], South = v[1], East = v[2], North = v[3] };
        }

        internal static Guid DeriveId(string path)
        {
            using (var md5 = MD5.Create())
            {
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(path.ToLowerInvariant())));
            }
        }

        /// <summary>
        /// A miss returns null, and OverlayTileProvider clears FillEmptyTiles so
        /// GMap reads that as "nothing here" rather than "not loaded yet".
        ///
        /// That distinction is the whole point. By default GMap answers a null by
        /// walking this same provider up the zoom levels, marking whatever it
        /// finds IsParent and drawing it magnified (Core.cs, "check for parent
        /// tiles if not found"). For a sparse overlay whose empty tiles were
        /// pruned at export, that paints overlay content across ground the export
        /// says has none -- so the layer opts out of the substitution entirely.
        /// </summary>
        public byte[] GetTile(long x, long y, int zoom)
        {
            // Off this tileset's zoom range or off its footprint. Answered
            // without touching SQLite and without taking the lock, which is the
            // common case: on a sparse overlay most positions on screen are
            // misses, and they must not queue behind an in-flight read.
            if (zoom < MinZoom || zoom > MaxZoom || !InBounds(x, y, zoom))
            {
                return null;
            }

            lock (_lock)
            {
                if (_disposed)
                {
                    return null;
                }

                // MBTiles rows count up from the south, GMap counts down from
                // the north.
                long row = ((1L << zoom) - 1) - y;

                try
                {
                    _select.Parameters["@z"].Value = (long)zoom;
                    _select.Parameters["@x"].Value = x;
                    _select.Parameters["@y"].Value = row;

                    using (var rd = _select.ExecuteReader())
                    {
                        return rd.Read() ? rd.GetValue(0) as byte[] : null;
                    }
                }
                catch (Exception ex)
                {
                    log.Error("tile read failed for " + Name, ex);
                    return null;
                }
            }
        }

        /// <summary>
        /// Precomputes the tile bounds of the declared footprint at every zoom
        /// this tileset serves, so the rejection test on the tile path is an
        /// array index and four compares.
        /// </summary>
        void BuildTileRanges()
        {
            if (!Bounds.HasValue)
            {
                return;
            }

            var b = Bounds.Value;
            _tile_range = new long[MaxZoom - MinZoom + 1][];

            for (int zoom = MinZoom; zoom <= MaxZoom; zoom++)
            {
                long n = 1L << zoom;
                _tile_range[zoom - MinZoom] = new long[]
                {
                    Clamp(LonToTileX(b.West, zoom), n),
                    Clamp(LonToTileX(b.East, zoom), n),
                    Clamp(LatToTileY(b.North, zoom), n),
                    Clamp(LatToTileY(b.South, zoom), n),
                };
            }
        }

        /// <summary>
        /// Rejects positions the declared bounds cannot cover. Lock-free -- the
        /// ranges are built once during construction and never mutated.
        /// </summary>
        bool InBounds(long x, long y, int zoom)
        {
            if (_tile_range == null)
            {
                return true;
            }

            var r = _tile_range[zoom - MinZoom];
            return x >= r[0] && x <= r[1] && y >= r[2] && y <= r[3];
        }

        static long Clamp(long v, long n)
        {
            return v < 0 ? 0 : (v > n - 1 ? n - 1 : v);
        }

        static long LonToTileX(double lon, int zoom)
        {
            return (long)Math.Floor((lon + 180.0) / 360.0 * (1L << zoom));
        }

        static long LatToTileY(double lat, int zoom)
        {
            if (lat > MERCATOR_LAT_LIMIT) lat = MERCATOR_LAT_LIMIT;
            if (lat < -MERCATOR_LAT_LIMIT) lat = -MERCATOR_LAT_LIMIT;

            var rad = lat * Math.PI / 180.0;
            return (long)Math.Floor(
                (1.0 - Math.Log(Math.Tan(rad) + 1.0 / Math.Cos(rad)) / Math.PI) / 2.0 * (1L << zoom));
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;

                // Order matters: the command goes before the connection, and
                // the connection string disables pooling, so the file handle is
                // really released here -- which is what lets the UI delete a
                // tileset that has been loaded.
                if (_select != null)
                {
                    _select.Dispose();
                    _select = null;
                }
                if (_db != null)
                {
                    _db.Dispose();
                    _db = null;
                }
            }
        }
    }
}
