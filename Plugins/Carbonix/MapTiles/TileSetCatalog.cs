using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Carbonix.MapTiles
{
    /// <summary>
    /// The set of tilesets Mission Planner knows about, and which of them are
    /// switched on.
    ///
    /// Tilesets are found two ways: every *.mbtiles in the managed folder, plus
    /// any explicitly added file elsewhere (these files get large, so keeping
    /// them on another drive has to stay possible). Each file is
    /// self-describing via its metadata table, so there is no parallel registry
    /// to drift out of sync -- add is a file appearing, remove is it going away.
    ///
    /// Only per-machine choices live in settings: which tilesets are disabled,
    /// and where the out-of-folder ones are.
    /// </summary>
    public class TileSetCatalog : IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        readonly object _lock = new object();
        readonly List<ITileSource> _sources = new List<ITileSource>();
        readonly List<string> _extra_paths = new List<string>();
        // Keyed on the dataset, not the file. Keying on the file would mean a
        // new revision landing in the folder silently switches a corridor back
        // on that the operator had deliberately switched off, because the new
        // file's id is not in this set.
        readonly HashSet<string> _disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Dataset key -> the revision being peeked at. Never persisted: the need
        // is transient ("what did the old one say here"), and a pin that
        // survives a restart is the same hazard as flying a lapsed approval.
        readonly Dictionary<string, Guid> _peeking =
            new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        // Datasets an operator has deliberately chosen to draw despite being
        // past their expiry. Session only, for the same reason as _peeking and
        // more so: the case it exists for is "it lapsed by a day and we are
        // stood on an airfield", which is a decision about right now. Carrying
        // it to the next start would turn a considered override into a setting
        // nobody remembers making.
        readonly HashSet<string> _forced =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        readonly List<string> _errors = new List<string>();

        /// <summary>Raised after any change to the set or to enabled state.</summary>
        public event Action Changed;

        /// <summary>
        /// Raised when tilesets have been closed and reopened from disk, so a
        /// file may have been replaced under an id that is already in use.
        /// Separate from <see cref="Changed"/> because that is the signal to
        /// discard cached tiles, and merely switching a tileset on or off does
        /// not invalidate anything.
        /// </summary>
        public event Action Reloaded;

        public string Folder { get; private set; }

        public TileSetCatalog(string folder)
        {
            Folder = folder;
        }

        /// <summary>Files added from outside the managed folder, for persisting.</summary>
        public IEnumerable<string> ExtraPaths
        {
            get { lock (_lock) { return _extra_paths.ToList(); } }
        }

        /// <summary>Datasets the operator has switched off, for persisting.</summary>
        public IEnumerable<string> DisabledKeys
        {
            get { lock (_lock) { return _disabled.ToList(); } }
        }

        /// <summary>Files that failed to open, for surfacing in the UI.</summary>
        public IEnumerable<string> Errors
        {
            get { lock (_lock) { return _errors.ToList(); } }
        }

        /// <summary>Everything loaded, in probe order.</summary>
        public IReadOnlyList<ITileSource> Sources
        {
            get { lock (_lock) { return _sources.ToList(); } }
        }

        public bool IsEnabled(ITileSource source)
        {
            lock (_lock) { return !_disabled.Contains(source.DatasetKey); }
        }

        /// <summary>
        /// Restores persisted state. Call before Rescan so the first load
        /// already reflects the operator's choices.
        /// </summary>
        public void Restore(IEnumerable<string> extraPaths, IEnumerable<string> disabledKeys)
        {
            lock (_lock)
            {
                _extra_paths.Clear();
                if (extraPaths != null)
                {
                    _extra_paths.AddRange(extraPaths.Where(p => !string.IsNullOrWhiteSpace(p)));
                }

                _disabled.Clear();
                if (disabledKeys != null)
                {
                    foreach (var key in disabledKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
                    {
                        _disabled.Add(key.Trim());
                    }
                }
            }
        }

        /// <summary>
        /// Closes everything and reloads from disk. Also the way to pick up a
        /// tileset that has been replaced in place.
        /// </summary>
        public void Rescan()
        {
            lock (_lock)
            {
                CloseAll();
                _errors.Clear();

                var paths = new List<string>();

                try
                {
                    if (!Directory.Exists(Folder))
                    {
                        Directory.CreateDirectory(Folder);
                    }
                    paths.AddRange(Directory.GetFiles(Folder, "*.mbtiles", SearchOption.TopDirectoryOnly)
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
                }
                catch (Exception ex)
                {
                    log.Error("could not scan tileset folder " + Folder, ex);
                    _errors.Add($"{Folder}: {ex.Message}");
                }

                // Explicit additions come after the managed folder, and a file
                // is never loaded twice if it happens to be in both.
                foreach (var p in _extra_paths)
                {
                    if (!paths.Any(existing => PathsEqual(existing, p)))
                    {
                        paths.Add(p);
                    }
                }

                foreach (var path in paths)
                {
                    Open(path);
                }
            }

            Reloaded?.Invoke();
            OnChanged();
        }

        /// <summary>
        /// Registers a tileset from anywhere on disk. Returns the loaded source,
        /// or throws if it cannot be opened.
        /// </summary>
        public ITileSource Add(string path)
        {
            ITileSource added;

            lock (_lock)
            {
                var full = Path.GetFullPath(path);

                if (_sources.Any(s => PathsEqual(s.Location, full)))
                {
                    throw new InvalidOperationException("that tileset is already loaded");
                }

                added = new MbTilesTileSource(full);
                _sources.Add(added);

                // Files inside the managed folder are found by the scan, so only
                // outside ones need remembering.
                if (!PathsEqual(Path.GetDirectoryName(full), Folder) &&
                    !_extra_paths.Any(p => PathsEqual(p, full)))
                {
                    _extra_paths.Add(full);
                }
            }

            OnChanged();
            return added;
        }

        /// <summary>
        /// Drops a tileset, optionally deleting the file. The handle is closed
        /// first -- Windows will not let the file go while it is open, which is
        /// exactly the case when clearing out an expired approval.
        /// </summary>
        public void Remove(ITileSource source, bool deleteFile)
        {
            string path;

            lock (_lock)
            {
                path = source.Location;
                var key = source.DatasetKey;

                _sources.Remove(source);
                _extra_paths.RemoveAll(p => PathsEqual(p, path));

                // Only forget the dataset's on/off state once its last revision
                // is gone. Clearing it while other revisions remain would switch
                // a corridor back on just because a superseded file was tidied
                // away.
                if (!_sources.Any(s => string.Equals(s.DatasetKey, key, StringComparison.OrdinalIgnoreCase)))
                {
                    _disabled.Remove(key);
                    _peeking.Remove(key);
                }

                source.Dispose();
            }

            if (deleteFile)
            {
                try
                {
                    File.Delete(path);
                    log.InfoFormat("deleted tileset {0}", path);
                }
                catch (Exception ex)
                {
                    log.Error("could not delete " + path, ex);
                    OnChanged();
                    throw;
                }
            }

            OnChanged();
        }

        public void SetEnabled(ITileSource source, bool enabled)
        {
            SetEnabled(source.DatasetKey, enabled);
        }

        public void SetEnabled(string datasetKey, bool enabled)
        {
            lock (_lock)
            {
                if (enabled)
                {
                    _disabled.Remove(datasetKey);
                }
                else
                {
                    _disabled.Add(datasetKey);
                }
            }

            OnChanged();
        }

        /// <summary>
        /// Show an older revision of a dataset in place of the newest, or pass
        /// null to go back to the newest.
        ///
        /// Session only, by design. The need this serves is diagnostic -- a new
        /// bake dropped something and you want to read it off the old one -- so
        /// it lasts as long as you are looking and no longer. It is never
        /// written to settings and never survives a restart.
        /// </summary>
        public void Peek(string datasetKey, ITileSource revision)
        {
            lock (_lock)
            {
                if (revision == null)
                {
                    _peeking.Remove(datasetKey);
                }
                else
                {
                    _peeking[datasetKey] = revision.Id;
                }
            }

            OnChanged();
        }

        /// <summary>
        /// Draw a dataset even though it has expired, or stop doing so.
        ///
        /// The escape hatch for an approval that lapsed by a day on the morning
        /// of a deployment. Expiry stays the default answer and this stays
        /// deliberate: the caller is expected to make the operator say yes to it,
        /// and the window keeps saying so for as long as it is on.
        ///
        /// Session only. Never written to settings, never survives a restart.
        /// </summary>
        public void ForceExpired(string datasetKey, bool forced)
        {
            lock (_lock)
            {
                if (forced)
                {
                    _forced.Add(datasetKey);
                }
                else
                {
                    _forced.Remove(datasetKey);
                }
            }

            OnChanged();
        }

        /// <summary>
        /// Every dataset, each with its revisions and whichever one is showing.
        /// This is what the management window lists.
        /// </summary>
        public List<TileSetGroup> Groups()
        {
            lock (_lock)
            {
                return BuildGroups();
            }
        }

        // Called with _lock held.
        List<TileSetGroup> BuildGroups()
        {
            var groups = new List<TileSetGroup>();

            foreach (var by_key in _sources.GroupBy(s => s.DatasetKey, StringComparer.OrdinalIgnoreCase))
            {
                var revisions = by_key.ToList();

                // Newest first, matching TileSetGroup's own ordering
                var latest = revisions
                    .OrderByDescending(s => s.Generated.HasValue)
                    .ThenByDescending(s => s.Generated)
                    .First();

                Guid peeked;
                var selected = _peeking.TryGetValue(by_key.Key, out peeked)
                    ? revisions.FirstOrDefault(s => s.Id == peeked) ?? latest
                    : latest;

                groups.Add(new TileSetGroup(
                    by_key.Key, revisions, selected,
                    !_disabled.Contains(by_key.Key),
                    _forced.Contains(by_key.Key)));
            }

            return groups;
        }

        /// <summary>
        /// The sources the overlay stack should draw: one revision per dataset,
        /// switched on, and not past its expiry.
        ///
        /// The order matters. The revision is chosen first and its expiry is
        /// checked second, so a lapsed current approval reads as lapsed. Picking
        /// the newest *unexpired* revision instead would quietly resurrect last
        /// year's approval the moment this year's ran out, which is exactly what
        /// carbonix:expires exists to prevent.
        ///
        /// An expired dataset draws only where the operator has explicitly
        /// overridden it -- see <see cref="ForceExpired"/>.
        /// </summary>
        public List<ITileSource> ActiveSources()
        {
            lock (_lock)
            {
                return BuildGroups()
                    .Where(g => g.IsDrawn)
                    .Select(g => g.Selected)
                    .ToList();
            }
        }

        void Open(string path)
        {
            try
            {
                _sources.Add(new MbTilesTileSource(path));
            }
            catch (Exception ex)
            {
                log.Error("could not load tileset " + path, ex);
                _errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        void CloseAll()
        {
            foreach (var s in _sources)
            {
                try
                {
                    s.Dispose();
                }
                catch (Exception ex)
                {
                    log.Error("error closing tileset " + s.Location, ex);
                }
            }
            _sources.Clear();
        }

        static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        void OnChanged()
        {
            var handler = Changed;
            if (handler != null)
            {
                handler();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                CloseAll();
            }
        }
    }
}
