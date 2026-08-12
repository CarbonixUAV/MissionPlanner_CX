using Carbonix.MapTiles;
using log4net;
using MissionPlanner.Controls;
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

// There are two CustomMessageBox types in scope. The one in the System
// namespace is a shim with its own DialogResult enum, and unqualified
// references bind to it. Alias the real one so results compare against
// System.Windows.Forms.DialogResult.
using MsgBox = MissionPlanner.MsgBox.CustomMessageBox;

namespace Carbonix
{
    /// <summary>
    /// Lists the map tilesets Mission Planner has loaded, with their validity,
    /// and lets the operator switch them on and off, add one from elsewhere on
    /// disk, or clear one out.
    ///
    /// Built in code rather than with the designer so there is no
    /// .Designer.cs/.resx triplet to keep in step.
    /// </summary>
    public class MapTilesForm : Form
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        const int COL_ENABLED = 0;
        const int COL_NOTES = 2;
        const int COL_REVISION = 3;

        readonly TileSetCatalog _catalog;
        readonly Func<string> _basemap;
        readonly DataGridView _grid;
        readonly Label _status;
        readonly TextBox _notes;
        bool _filling;

        public MapTilesForm(TileSetCatalog catalog, Func<string> basemap)
        {
            _catalog = catalog;
            _basemap = basemap;

            Text = "Map Tilesets";
            Size = new Size(1000, 440);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(700, 300);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EditMode = DataGridViewEditMode.EditOnEnter,
            };

            AddColumn(new DataGridViewCheckBoxColumn { HeaderText = "On", FillWeight = 24 });
            AddColumn(new DataGridViewTextBoxColumn { HeaderText = "Name", FillWeight = 150 });
            AddColumn(new DataGridViewTextBoxColumn { HeaderText = "Notes", FillWeight = 150 });
            AddColumn(new DataGridViewComboBoxColumn
            {
                HeaderText = "Revision",
                FillWeight = 110,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Flat,
            });
            AddColumn(new DataGridViewTextBoxColumn { HeaderText = "Zoom", FillWeight = 45 });
            AddColumn(new DataGridViewTextBoxColumn { HeaderText = "Tiles", FillWeight = 55 });
            AddColumn(new DataGridViewTextBoxColumn { HeaderText = "Size", FillWeight = 55 });
            AddColumn(new DataGridViewTextBoxColumn { HeaderText = "Valid until", FillWeight = 75 });
            AddColumn(new DataGridViewTextBoxColumn { HeaderText = "Status", FillWeight = 85 });
            AddColumn(new DataGridViewTextBoxColumn { HeaderText = "Location", FillWeight = 200 });

            // Commit the checkbox and the revision picker on click rather than
            // when focus leaves the cell
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty &&
                    (_grid.CurrentCell is DataGridViewCheckBoxCell ||
                     _grid.CurrentCell is DataGridViewComboBoxCell))
                {
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            _grid.CellValueChanged += Grid_CellValueChanged;

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.LeftToRight,
                Height = 40,
                Padding = new Padding(4),
            };
            buttons.Controls.Add(MakeButton("Add File...", Add_Click));
            buttons.Controls.Add(MakeButton("Remove...", Remove_Click));
            buttons.Controls.Add(MakeButton("Reload", (s, e) => _catalog.Rescan()));
            buttons.Controls.Add(MakeButton("Open Folder", OpenFolder_Click));

            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 34,
                Padding = new Padding(6, 4, 6, 4),
                AutoEllipsis = true,
            };

            // The notes say what a tileset does and does not claim -- which
            // layers were checked by hand, which came from a bulk query, what
            // the inclusion rule was. That is read carefully once, not scanned,
            // so it gets a pane rather than a truncated grid cell.
            _notes = new TextBox
            {
                Dock = DockStyle.Bottom,
                Height = 110,
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
            };

            _grid.SelectionChanged += (s, e) => ShowNotes();

            // Docking resolves last-added outermost, so this is bottom upwards:
            // buttons, status, notes, and the grid takes what is left.
            Controls.Add(_grid);
            Controls.Add(_notes);
            Controls.Add(_status);
            Controls.Add(buttons);

            // The catalog is shared with the plugin and can change from either
            // side, so redraw off its own event rather than after each action
            // here -- otherwise the list silently goes stale.
            _catalog.Changed += CatalogChanged;
            FormClosed += (s, e) => _catalog.Changed -= CatalogChanged;

            Fill();

            try
            {
                ThemeManager.ApplyThemeTo(this);
            }
            catch (Exception ex)
            {
                log.Debug("theme apply failed", ex);
            }
        }

        void AddColumn(DataGridViewColumn col)
        {
            col.SortMode = DataGridViewColumnSortMode.NotSortable;

            // The On checkbox is the only thing the operator edits here
            col.ReadOnly = !(col is DataGridViewCheckBoxColumn);

            _grid.Columns.Add(col);
        }

        static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new MyButton { Text = text, Width = 110, Height = 26 };
            b.Click += onClick;
            return b;
        }

        void CatalogChanged()
        {
            this.BeginInvokeIfRequired(() => Fill());
        }

        /// <summary>
        /// Notes for whichever dataset is selected, of the revision actually
        /// showing -- an older bake may well describe different contents, which
        /// is half the reason for looking at one.
        /// </summary>
        void ShowNotes()
        {
            var group = _grid.CurrentRow?.Tag as TileSetGroup;
            var source = group?.Selected;

            if (source == null)
            {
                _notes.Text = "";
                return;
            }

            var text = string.IsNullOrWhiteSpace(source.Description)
                ? "This tileset carries no notes, so what it contains and how it was " +
                  "checked is not recorded in the file."
                : source.Description.Trim();

            // Normalised so a bake that wrote bare newlines still lays out
            _notes.Text = $"{group.Name} -- {RevisionLabel(source, group)}" +
                Environment.NewLine + Environment.NewLine +
                text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

            _notes.Select(0, 0);
        }

        void Fill()
        {
            _filling = true;
            try
            {
                _grid.Rows.Clear();

                var active = 0;
                var peeking = 0;
                var undeclared = 0;
                var undated = 0;
                var forced = new List<string>();

                // One row per dataset, not per file. A corridor corrected four
                // times is one entry with four revisions behind it.
                foreach (var group in _catalog.Groups())
                {
                    var source = group.Selected;
                    if (source == null)
                    {
                        continue;
                    }

                    var expired = source.IsExpired;

                    if (group.IsDrawn)
                    {
                        active++;
                    }
                    if (group.ForcedExpired && expired)
                    {
                        forced.Add(group.Name);
                    }
                    if (group.IsPeeking)
                    {
                        peeking++;
                    }
                    if (!group.HasDeclaredDataset)
                    {
                        undeclared++;
                    }
                    if (!source.Expires.HasValue)
                    {
                        undated++;
                    }

                    var index = _grid.Rows.Add(
                        // What is actually contributing, not what was asked for.
                        // An expired tileset does not draw unless it has been
                        // explicitly overridden, and a tick against something not
                        // being drawn would claim otherwise.
                        group.IsDrawn,
                        group.Name,
                        source.Description ?? "",
                        RevisionLabel(source, group),
                        $"{source.MinZoom}-{source.MaxZoom}",
                        source.TileCount.ToString("N0"),
                        FormatSize(source.SizeBytes),
                        // Blank would read as "fine". It is not fine -- a tileset
                        // with no expiry never lapses, however old the bake is.
                        source.Expires.HasValue ? source.Expires.Value.ToString("yyyy-MM-dd") : "not set",
                        RowStatus(group, expired),
                        source.Location);

                    var row = _grid.Rows[index];
                    row.Tag = group;

                    // The notes are the one free-text field and will not fit
                    row.Cells[COL_NOTES].ToolTipText = source.Description ?? "";

                    if (expired)
                    {
                        row.Cells[COL_ENABLED].ToolTipText =
                            "Expired on " + source.Expires.Value.ToString("yyyy-MM-dd") +
                            ". It can be shown anyway, but you will be asked to confirm " +
                            "and it reverts to hidden when Mission Planner restarts.";
                    }

                    var picker = (DataGridViewComboBoxCell)row.Cells[COL_REVISION];
                    foreach (var revision in group.Revisions)
                    {
                        picker.Items.Add(RevisionLabel(revision, group));
                    }
                    picker.Value = RevisionLabel(source, group);
                    picker.ReadOnly = group.Revisions.Count < 2;

                    // Picked off the theme rather than the system palette, which
                    // is a light-theme grey over Mission Planner's dark grid.
                    if (expired)
                    {
                        row.DefaultCellStyle.ForeColor = Color.Firebrick;
                    }
                    else if (group.IsPeeking)
                    {
                        // Showing something knowingly out of date is worth being
                        // as loud about as an expiry.
                        row.DefaultCellStyle.ForeColor = Color.DarkOrange;
                    }
                    else if (!group.Enabled)
                    {
                        row.DefaultCellStyle.ForeColor = ThemeManager.ButtonTextColorNotEnabled;
                    }
                }

                var basemap = _basemap?.Invoke() ?? "the base map";
                var text = $"{_grid.Rows.Count} dataset(s), {active} drawn over {basemap}. " +
                           "Listed in draw order -- lower rows draw over higher ones. " +
                           "Change the base map from the dropdown on the Plan screen, or the " +
                           "Base Layer menu on the Flight Data map.";

                if (forced.Count > 0)
                {
                    // The loudest line in the window. Everything else here is a
                    // note about the data; this one says expired data is on the
                    // map right now.
                    text += Environment.NewLine +
                        "EXPIRED DATA IS BEING DRAWN: " + string.Join(", ", forced) +
                        ". Overridden by hand and not saved -- it hides again on restart.";
                }

                if (peeking > 0)
                {
                    text += Environment.NewLine +
                        $"SHOWING AN OLD REVISION of {peeking} dataset(s). This is for checking " +
                        "what a previous bake contained -- it is not saved, and reverts to the " +
                        "newest on restart.";
                }

                if (undated > 0)
                {
                    // Deliberately advisory, not enforced. Inventing an expiry
                    // here would be a policy buried in the consumer, and would
                    // silently hide a tileset the operator still wants. Saying
                    // so plainly puts the judgement where it belongs.
                    text += Environment.NewLine +
                        $"{undated} tileset(s) have no expiry, so they will never lapse on their " +
                        "own -- check the generation date in the Revision column against how old " +
                        "you are willing to fly.";
                }

                if (undeclared > 0)
                {
                    // The failure a hand-baked file actually produces: without a
                    // reference it cannot supersede anything, so a corrected
                    // corridor sits beside the old one and both draw.
                    text += Environment.NewLine +
                        $"{undeclared} tileset(s) declare no carbonix:reference, so they cannot " +
                        "supersede or be superseded -- a new bake of one will appear as a second " +
                        "dataset rather than a new revision.";
                }

                // The provider bypasses the tile cache, and CacheOnly tells
                // GMaps not to call providers at all -- so that combination
                // silently shows nothing. It is also exactly what someone would
                // pick to go offline, so say so rather than let it puzzle them.
                if (GMap.NET.GMaps.Instance.Mode == GMap.NET.AccessMode.CacheOnly)
                {
                    text += Environment.NewLine +
                        "WARNING: map access is set to CacheOnly, which stops tilesets being read. " +
                        "Set it to ServerAndCache in Config > Planner. Tilesets work offline regardless.";
                }

                foreach (var err in _catalog.Errors)
                {
                    text += Environment.NewLine + "Failed to load " + err;
                }
                _status.Text = text;
            }
            finally
            {
                _filling = false;
            }

            ShowNotes();
        }

        /// <summary>
        /// How a revision reads in the picker. The label is whatever the bake
        /// wrote; the generation date is what actually ordered them, so it is
        /// shown too -- if a label was forgotten and two read "r3.0", the dates
        /// still say which is which.
        /// </summary>
        static string RevisionLabel(ITileSource source, TileSetGroup group)
        {
            var label = string.IsNullOrWhiteSpace(source.Revision)
                ? "(no revision)"
                : source.Revision.Trim();

            if (source.Generated.HasValue)
            {
                label += "  " + source.Generated.Value.ToString("yyyy-MM-dd HH:mm");
            }

            if (ReferenceEquals(source, group.Latest))
            {
                label += group.Revisions.Count > 1 ? "  (latest)" : "";
            }
            else
            {
                label += "  (superseded)";
            }

            return label;
        }

        static string RowStatus(TileSetGroup group, bool expired)
        {
            if (expired)
            {
                return group.ForcedExpired ? "EXPIRED - SHOWN" : "EXPIRED";
            }
            if (!group.Enabled)
            {
                return "Off";
            }
            return group.IsPeeking ? "OLD REVISION" : "Active";
        }

        static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
            {
                return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.0") + " GB";
            }
            return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
        }

        void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_filling || e.RowIndex < 0)
            {
                return;
            }

            var group = _grid.Rows[e.RowIndex].Tag as TileSetGroup;
            if (group == null)
            {
                return;
            }

            if (e.ColumnIndex == COL_ENABLED)
            {
                var on = Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells[COL_ENABLED].Value);

                if (group.IsExpired)
                {
                    ToggleExpired(group, on);
                    return;
                }

                _catalog.SetEnabled(group.Key, on);
                return;
            }

            if (e.ColumnIndex == COL_REVISION)
            {
                var label = Convert.ToString(_grid.Rows[e.RowIndex].Cells[COL_REVISION].Value);
                var chosen = group.Revisions
                    .FirstOrDefault(r => RevisionLabel(r, group) == label);

                // Selecting the newest is not a peek, it is going back to normal
                _catalog.Peek(group.Key,
                    chosen == null || ReferenceEquals(chosen, group.Latest) ? null : chosen);
            }
        }

        /// <summary>
        /// Switching an expired dataset on is allowed, but never by accident.
        /// The operator is told what lapsed and when, and has to say yes.
        /// </summary>
        void ToggleExpired(TileSetGroup group, bool on)
        {
            if (!on)
            {
                _catalog.ForceExpired(group.Key, false);
                return;
            }

            var expiry = group.Selected.Expires;
            var days = expiry.HasValue
                ? (int)(DateTime.UtcNow - expiry.Value).TotalDays
                : 0;

            var answer = MsgBox.Show(
                $"\"{group.Name}\" expired on " +
                (expiry.HasValue ? expiry.Value.ToString("yyyy-MM-dd") : "an unknown date") +
                (days > 0 ? $", {days} day(s) ago." : ".") + Environment.NewLine + Environment.NewLine +
                "Show it anyway? It will revert to hidden when Mission Planner restarts.",
                "Show Expired Tileset", MessageBoxButtons.YesNo);

            if (answer != DialogResult.Yes)
            {
                // Put the checkbox back; the catalog never changed
                Fill();
                return;
            }

            // Overriding the expiry implies wanting it on at all
            _catalog.SetEnabled(group.Key, true);
            _catalog.ForceExpired(group.Key, true);
        }

        void Add_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "MBTiles tilesets (*.mbtiles)|*.mbtiles|All files (*.*)|*.*";
                dlg.Title = "Add a map tileset";

                if (dlg.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    _catalog.Add(dlg.FileName);
                }
                catch (Exception ex)
                {
                    log.Error("could not add tileset", ex);
                    MsgBox.Show("Could not add that tileset:" + Environment.NewLine +
                        ex.Message, "Map Tilesets");
                }
            }
        }

        void Remove_Click(object sender, EventArgs e)
        {
            var group = SelectedGroup();
            if (group == null)
            {
                return;
            }

            // The row is a dataset, so this removes the dataset -- every
            // revision of it. The count is spelled out because removing three
            // files when you selected one row would otherwise be a surprise.
            var files = group.Revisions.Select(r => r.Location).ToList();
            var what = files.Count == 1
                ? files[0]
                : $"{files.Count} revisions:" + Environment.NewLine +
                  string.Join(Environment.NewLine, files);

            // Two prompts rather than one three-way: CustomMessageBox only
            // implements OK, YesNo and OKCancel, and splitting the destructive
            // part out is clearer anyway.
            var remove = MsgBox.Show(
                $"Stop using \"{group.Name}\"?" + Environment.NewLine + Environment.NewLine + what,
                "Remove Tileset", MessageBoxButtons.YesNo);

            if (remove != DialogResult.Yes)
            {
                return;
            }

            var delete = MsgBox.Show(
                (files.Count == 1 ? "Delete the file from disk as well?"
                                  : $"Delete all {files.Count} files from disk as well?") +
                Environment.NewLine + Environment.NewLine + what + Environment.NewLine +
                Environment.NewLine + "Choose No to keep them and just stop using them.",
                "Delete File", MessageBoxButtons.YesNo);

            var failures = new List<string>();

            foreach (var revision in group.Revisions.ToList())
            {
                try
                {
                    _catalog.Remove(revision, deleteFile: delete == DialogResult.Yes);
                }
                catch (Exception ex)
                {
                    failures.Add(revision.Location + ": " + ex.Message);
                }
            }

            if (failures.Count > 0)
            {
                MsgBox.Show("Removed from the list, but could not be deleted:" +
                    Environment.NewLine + string.Join(Environment.NewLine, failures),
                    "Map Tilesets");
            }
        }

        TileSetGroup SelectedGroup()
        {
            if (_grid.CurrentRow == null)
            {
                MsgBox.Show("Select a tileset first.", "Map Tilesets");
                return null;
            }
            return _grid.CurrentRow.Tag as TileSetGroup;
        }

        void OpenFolder_Click(object sender, EventArgs e)
        {
            try
            {
                if (!Directory.Exists(_catalog.Folder))
                {
                    Directory.CreateDirectory(_catalog.Folder);
                }
                System.Diagnostics.Process.Start("explorer.exe", "\"" + _catalog.Folder + "\"");
            }
            catch (Exception ex)
            {
                log.Error("could not open tileset folder", ex);
                MsgBox.Show("Could not open " + _catalog.Folder, "Map Tilesets");
            }
        }
    }
}
