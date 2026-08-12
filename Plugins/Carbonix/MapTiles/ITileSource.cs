using System;

namespace Carbonix.MapTiles
{
    /// <summary>
    /// Geographic footprint of a tileset, in degrees.
    /// </summary>
    public struct GeoBounds
    {
        public double West;
        public double South;
        public double East;
        public double North;

        public override string ToString()
        {
            return $"{West:0.####},{South:0.####},{East:0.####},{North:0.####}";
        }
    }

    /// <summary>
    /// One tileset that an overlay layer can pull tiles from.
    ///
    /// Deliberately free of GMap types and of any assumption about where the
    /// tiles physically live. MbTilesTileSource reads a local file today; an
    /// HTTP/PMTiles source hosted on S3 or MapTiler implements the same
    /// interface and slots into the same probe list without the provider,
    /// catalog or UI changing.
    ///
    /// Coordinates are XYZ convention (y increasing southward). Sources that
    /// store TMS rows do the flip internally.
    /// </summary>
    public interface ITileSource : IDisposable
    {
        /// <summary>
        /// Identity of this particular file. Comes from the carbonix:id metadata
        /// row when present, else is derived deterministically from the path.
        ///
        /// Per revision, not per dataset -- two revisions of the same corridor
        /// are two sources with two ids. Group on <see cref="DatasetKey"/>.
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// What this tileset is a revision *of*. Revisions of the same area
        /// share it, which is what lets a newer bake supersede an older one
        /// rather than sitting beside it as a second corridor.
        ///
        /// carbonix:reference when declared. A file that declares none is its
        /// own dataset, so it behaves exactly as an unmanaged file always has --
        /// see <see cref="HasDeclaredDataset"/>, which the UI surfaces, because
        /// silently doubling a corridor is worse than any revision problem.
        /// </summary>
        string DatasetKey { get; }

        /// <summary>False when the file declared no carbonix:reference.</summary>
        bool HasDeclaredDataset { get; }

        /// <summary>
        /// carbonix:generated -- when the file was baked, and the authority on
        /// which revision is newest.
        ///
        /// Deliberately not the revision label: a label can be forgotten on a
        /// manual bake, and forgetting it should be a cosmetic bug rather than
        /// a safety one. Null sorts oldest.
        /// </summary>
        DateTime? Generated { get; }

        /// <summary>
        /// carbonix:revision -- the human label ("r3.0"). Display only; ordering
        /// is <see cref="Generated"/>.
        /// </summary>
        string Revision { get; }

        /// <summary>Display name, from the MBTiles name metadata row.</summary>
        string Name { get; }

        /// <summary>Where it came from -- a file path, or later a URL.</summary>
        string Location { get; }

        int MinZoom { get; }
        int MaxZoom { get; }

        GeoBounds? Bounds { get; }

        /// <summary>
        /// The standard MBTiles description row.
        ///
        /// Shown to the operator verbatim, so it is where a bake says what it
        /// contains and what it does not -- which parts were checked by hand,
        /// what is best-effort. A tileset covering a whole basin is not uniformly
        /// authoritative, and nothing else in this metadata can express that.
        /// </summary>
        string Description { get; }

        /// <summary>carbonix:reference -- approval or ticket number, if any.</summary>
        string Reference { get; }

        /// <summary>
        /// carbonix:expires, if present. An expired source is skipped by the
        /// provider -- an out of date approval volume that still renders as
        /// though it were current is the actively dangerous outcome.
        /// </summary>
        DateTime? Expires { get; }

        bool IsExpired { get; }

        /// <summary>Number of tiles stored, for the management UI.</summary>
        long TileCount { get; }

        /// <summary>Bytes on disk, for the management UI.</summary>
        long SizeBytes { get; }

        /// <summary>
        /// Returns the encoded tile bytes (PNG or JPEG as stored), or null if
        /// this source has nothing at that position. Must be safe to call from
        /// several tile threads at once.
        /// </summary>
        byte[] GetTile(long x, long y, int zoom);
    }
}
