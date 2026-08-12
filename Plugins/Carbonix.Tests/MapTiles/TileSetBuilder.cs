using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text;

namespace Carbonix.Tests.MapTiles
{
    /// <summary>
    /// Builds small MBTiles files in a temp folder.
    ///
    /// Revision behaviour needs several files that differ only in their
    /// metadata, which is tedious and opaque as checked-in binaries -- the
    /// interesting part of the fixture would be invisible in the repo. Built
    /// here, each test states the metadata it depends on.
    /// </summary>
    public class TileSetBuilder
    {
        readonly string _folder;
        readonly Dictionary<string, string> _meta = new Dictionary<string, string>();
        readonly List<Tuple<int, long, long, byte[]>> _tiles =
            new List<Tuple<int, long, long, byte[]>>();

        string _file;

        public TileSetBuilder(string folder, string fileName)
        {
            _folder = folder;
            _file = fileName;

            _meta["name"] = Path.GetFileNameWithoutExtension(fileName);
            _meta["format"] = "png";
            _meta["type"] = "overlay";
            _meta["minzoom"] = "2";
            _meta["maxzoom"] = "4";
        }

        public TileSetBuilder Named(string name)
        {
            _meta["name"] = name;
            return this;
        }

        public TileSetBuilder Reference(string reference)
        {
            _meta["carbonix:reference"] = reference;
            return this;
        }

        /// <summary>Drops the reference row, as a bake that forgot it would.</summary>
        public TileSetBuilder NoReference()
        {
            _meta.Remove("carbonix:reference");
            return this;
        }

        public TileSetBuilder Generated(string iso8601)
        {
            _meta["carbonix:generated"] = iso8601;
            return this;
        }

        public TileSetBuilder Revision(string label)
        {
            _meta["carbonix:revision"] = label;
            return this;
        }

        public TileSetBuilder Describes(string description)
        {
            _meta["description"] = description;
            return this;
        }

        public TileSetBuilder Expires(string iso8601)
        {
            _meta["carbonix:expires"] = iso8601;
            return this;
        }

        public TileSetBuilder Zooms(int min, int max)
        {
            _meta["minzoom"] = min.ToString(CultureInfo.InvariantCulture);
            _meta["maxzoom"] = max.ToString(CultureInfo.InvariantCulture);
            return this;
        }

        public TileSetBuilder Bounds(string wsen)
        {
            _meta["bounds"] = wsen;
            return this;
        }

        /// <summary>Adds a tile carrying an ASCII payload, addressed in XYZ.</summary>
        public TileSetBuilder Tile(int zoom, long x, long y, string payload)
        {
            var row = ((1L << zoom) - 1) - y;
            _tiles.Add(Tuple.Create(zoom, x, row, Encoding.ASCII.GetBytes(payload)));
            return this;
        }

        public string Build()
        {
            Directory.CreateDirectory(_folder);
            var path = Path.Combine(_folder, _file);

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = path,
                Version = 3,
                Pooling = false,
            };

            using (var db = new SQLiteConnection(builder.ToString()))
            {
                db.Open();

                using (var cmd = db.CreateCommand())
                {
                    cmd.CommandText =
                        "CREATE TABLE metadata (name TEXT, value TEXT);" +
                        "CREATE TABLE tiles (zoom_level INTEGER, tile_column INTEGER, " +
                        "tile_row INTEGER, tile_data BLOB);" +
                        "CREATE UNIQUE INDEX tile_index ON tiles " +
                        "(zoom_level, tile_column, tile_row);";
                    cmd.ExecuteNonQuery();
                }

                using (var tx = db.BeginTransaction())
                {
                    foreach (var kv in _meta)
                    {
                        using (var cmd = db.CreateCommand())
                        {
                            cmd.CommandText = "INSERT INTO metadata (name, value) VALUES (@n, @v)";
                            cmd.Parameters.AddWithValue("@n", kv.Key);
                            cmd.Parameters.AddWithValue("@v", kv.Value);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    foreach (var tile in _tiles)
                    {
                        using (var cmd = db.CreateCommand())
                        {
                            cmd.CommandText =
                                "INSERT INTO tiles (zoom_level, tile_column, tile_row, tile_data) " +
                                "VALUES (@z, @x, @y, @d)";
                            cmd.Parameters.AddWithValue("@z", tile.Item1);
                            cmd.Parameters.AddWithValue("@x", tile.Item2);
                            cmd.Parameters.AddWithValue("@y", tile.Item3);
                            cmd.Parameters.AddWithValue("@d", tile.Item4);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    tx.Commit();
                }
            }

            return path;
        }
    }
}
