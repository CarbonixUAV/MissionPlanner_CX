using System.Collections.Generic;
using System.Linq;

namespace Carbonix.MapTiles
{
    /// <summary>
    /// One dataset and every revision of it that is currently loaded.
    ///
    /// This is the unit the operator thinks in and the unit the management
    /// window shows a row for. A corridor corrected four times is one entry
    /// with four revisions, not four entries that all look equally live.
    /// </summary>
    public class TileSetGroup
    {
        public TileSetGroup(string key, IEnumerable<ITileSource> revisions, ITileSource selected,
            bool enabled, bool forcedExpired = false)
        {
            ForcedExpired = forcedExpired;
            Key = key;

            // Newest first. Generated is the ordering authority; a file that
            // declares none sorts oldest, so an undated stray can never
            // supersede a dated bake.
            Revisions = revisions
                .OrderByDescending(s => s.Generated.HasValue)
                .ThenByDescending(s => s.Generated)
                .ToList();

            Selected = selected;
            Enabled = enabled;
        }

        /// <summary>Groups on this -- carbonix:reference, or a lone file's id.</summary>
        public string Key { get; private set; }

        /// <summary>Every loaded revision, newest first.</summary>
        public IReadOnlyList<ITileSource> Revisions { get; private set; }

        /// <summary>The newest revision loaded.</summary>
        public ITileSource Latest
        {
            get { return Revisions.Count > 0 ? Revisions[0] : null; }
        }

        /// <summary>
        /// The revision that will actually draw. Latest unless the operator is
        /// peeking at an older one.
        /// </summary>
        public ITileSource Selected { get; private set; }

        public bool Enabled { get; private set; }

        /// <summary>True when the selected revision is past its expiry.</summary>
        public bool IsExpired
        {
            get { return Selected != null && Selected.IsExpired; }
        }

        /// <summary>
        /// True when the operator has deliberately overridden an expiry. Loud in
        /// the UI for as long as it lasts, and never persisted -- see
        /// TileSetCatalog.ForceExpired.
        /// </summary>
        public bool ForcedExpired { get; private set; }

        /// <summary>
        /// Whether this actually contributes tiles. Switched on, and either not
        /// expired or explicitly overridden.
        ///
        /// This rather than <see cref="Enabled"/> is what the checkbox should
        /// show: a tick against something that is not being drawn claims
        /// otherwise.
        /// </summary>
        public bool IsDrawn
        {
            get { return Enabled && Selected != null && (!IsExpired || ForcedExpired); }
        }

        /// <summary>
        /// True while an older revision is being shown in place of the newest.
        /// Deliberately loud in the UI and deliberately never persisted -- see
        /// TileSetCatalog.Peek.
        /// </summary>
        public bool IsPeeking
        {
            get { return Selected != null && Latest != null && !ReferenceEquals(Selected, Latest); }
        }

        /// <summary>Superseded revisions, for showing what was replaced.</summary>
        public IEnumerable<ITileSource> Superseded
        {
            get { return Revisions.Skip(1); }
        }

        /// <summary>
        /// False when no revision in this group declared a reference, which
        /// means it is a dataset of one only because the bake forgot to say
        /// otherwise.
        /// </summary>
        public bool HasDeclaredDataset
        {
            get { return Latest != null && Latest.HasDeclaredDataset; }
        }

        public string Name
        {
            get { return Latest?.Name ?? Key; }
        }
    }
}
