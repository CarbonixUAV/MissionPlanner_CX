using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using MissionPlanner;
using MissionPlanner.GCSViews;   // GMapMarkerKMLLabel
using MissionPlanner.Maps;       // GMapMarkerPlus
using MissionPlanner.Utilities;
using Carbonix.Planning;

// SharpKml
using SharpKml.Base;
using SharpKml.Dom;
using SharpKml.Engine;

namespace Carbonix
{
    // ─────────────────────────────────────────────────────────────────────────────
    // "Edit Path" tab: interactive editing of the loaded corridor geometry.
    //
    //   • Import / Save the raw path as KML.
    //   • "Number Legs" runs the same CorridorTourBuilder used by generation to split
    //     the features into legs (edges), then colours + numbers each leg on the map
    //     and reveals draggable vertex handles plus add-vertex "+" markers.
    //   • Dragging a vertex moves it (junction-shared vertices move together so the
    //     tour builder still detects the junction). Midpoint "+" inserts a vertex in a
    //     segment; endpoint "+" (only at true dead-ends, never at an intersection)
    //     extends the leg.
    //
    // Edits mutate the loaded features in place; any generated mission/profile is
    // invalidated so the next Generate uses the new geometry.
    // ─────────────────────────────────────────────────────────────────────────────
    public partial class CorridorPlanForm
    {
        // Overlay holding coloured legs, number labels, vertex handles and "+" markers.
        private GMapOverlay layer_edit;

        // Edit-tab controls (built in code to avoid hand-editing the Designer).
        private System.Windows.Forms.TabPage tabEdit;
        private MissionPlanner.Controls.MyButton BUT_edit_import;
        private MissionPlanner.Controls.MyButton BUT_edit_save;
        private MissionPlanner.Controls.MyButton BUT_edit_number;
        private MissionPlanner.Controls.MyButton BUT_edit_splithome;
        private System.Windows.Forms.ContextMenuStrip editMenu;
        private System.Windows.Forms.ToolStripMenuItem MI_split;
        private System.Windows.Forms.ToolStripMenuItem MI_join;
        private System.Windows.Forms.ToolStripMenuItem MI_delete;
        private EditHandle menuTargetHandle;   // vertex the context menu was opened on
        private System.Windows.Forms.CheckBox CHK_show_wp;
        private System.Windows.Forms.CheckBox CHK_show_plus;
        private System.Windows.Forms.CheckBox CHK_show_labels;
        private System.Windows.Forms.Label lbl_legs;
        private System.Windows.Forms.ListBox LST_legs;
        private MissionPlanner.Controls.MyButton BUT_leg_up;
        private MissionPlanner.Controls.MyButton BUT_leg_down;
        private System.Windows.Forms.Label lbl_edit_info;

        private bool editMode;        // Edit tab is the active tab
        private bool legsNumbered;    // "Number Legs" has been run since the last geometry change

        // Edge ids in current flight order; also the branch-visit-order hint fed back to the
        // tour builder. Reset to the tour's actual order on every DrawEditColored.
        private List<int> legOrder = new List<int>();

        // Leg drag-and-drop state. Computed once on pick-up: which drop rows are reachable
        // (landing position → the resulting leg order), so only valid swaps highlight/apply.
        private int legDragFrom = -1;
        private int legDragOver = -1;
        private Dictionary<int, List<int>> legDragTargets;

        // One row of the legs list: an edge with its flight-order number and colour.
        private sealed class LegItem
        {
            public int EdgeId;
            public int LegNo;
            public Color Color;
            public double LengthM;
            public override string ToString() => "Leg " + LegNo;
        }

        // Distinct per-leg colours, cycled by leg number.
        private static readonly Color[] LegPalette =
        {
            Color.Red, Color.DeepSkyBlue, Color.Lime, Color.Orange, Color.Magenta,
            Color.Gold, Color.Cyan, Color.HotPink, Color.SpringGreen, Color.OrangeRed,
            Color.MediumPurple, Color.YellowGreen,
        };

        // Marker identity tags.
        private sealed class EditHandle  { public int Feature; public int Index; }   // draggable vertex
        private sealed class EditMidPlus { public int Feature; public int Seg;   }   // insert mid-segment
        private sealed class EditEndPlus { public int Feature; public int Index; }   // extend at a dead-end

        // Rebuilt on every DrawEditColored: coordinate key -> route points / vertex markers there.
        // Lets a drag update just the touched routes/markers instead of re-splitting the whole tour.
        private Dictionary<(long, long), List<(GMapRoute route, int idx)>> editRoutePts;
        private Dictionary<(long, long), List<GMapMarker>> editVertexMarkers;

        // Live-drag state (references captured at MouseDown).
        private GMapMarker hoverEditMarker;
        private bool editDragging;
        private List<(GMapRoute route, int idx)> dragRoutePts;
        private List<GMapMarker> dragMarkers;

        // Snapped vertices are bit-identical doubles; round to ~0.1 mm — matches
        // CorridorTourBuilder's junction key so dead-end/junction detection lines up.
        private const double EditKeyScale = 1e9;
        private static (long, long) Key(PointLatLngAlt p)
            => ((long)Math.Round(p.Lng * EditKeyScale), (long)Math.Round(p.Lat * EditKeyScale));
        private static (long, long) Key(PointLatLng p)
            => ((long)Math.Round(p.Lng * EditKeyScale), (long)Math.Round(p.Lat * EditKeyScale));

        private List<PointLatLngAlt> FeatureAt(int f) => AllFeatures()[f];

        // ─── Setup (called from the constructor) ────────────────────────────────────
        private void BuildEditTab()
        {
            layer_edit = new GMapOverlay("edit");
            map.Overlays.Add(layer_edit);   // added last → renders on top

            tabEdit = new System.Windows.Forms.TabPage("Edit Path")
            {
                UseVisualStyleBackColor = true,
                Padding = new System.Windows.Forms.Padding(3),
            };

            var pnl = new System.Windows.Forms.Panel
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                Padding = new System.Windows.Forms.Padding(8),
            };

            BUT_edit_import = new MissionPlanner.Controls.MyButton
            {
                Text = "Import KML…", Location = new System.Drawing.Point(8, 12), Size = new Size(110, 26),
            };
            BUT_edit_save = new MissionPlanner.Controls.MyButton
            {
                Text = "Save KML…", Location = new System.Drawing.Point(126, 12), Size = new Size(110, 26),
            };
            BUT_edit_number = new MissionPlanner.Controls.MyButton
            {
                Text = "Number Legs", Location = new System.Drawing.Point(8, 44), Size = new Size(110, 26),
            };
            BUT_edit_splithome = new MissionPlanner.Controls.MyButton
            {
                Text = "Split at Home", Location = new System.Drawing.Point(126, 44), Size = new Size(110, 26),
            };

            System.Windows.Forms.CheckBox MakeChk(string text, int y) => new System.Windows.Forms.CheckBox
            {
                Text = text, Checked = true, AutoSize = true, Location = new System.Drawing.Point(8, y),
            };
            CHK_show_wp = MakeChk("Show waypoints", 80);
            CHK_show_plus = MakeChk("Show + markers", 102);
            CHK_show_labels = MakeChk("Show leg labels", 124);

            lbl_legs = new System.Windows.Forms.Label
            {
                Text = "Legs (flight order):", AutoSize = true, Location = new System.Drawing.Point(8, 152),
            };
            LST_legs = new System.Windows.Forms.ListBox
            {
                Location = new System.Drawing.Point(8, 170), Size = new Size(190, 240),
                DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed, ItemHeight = 20,
                IntegralHeight = false,
            };
            LST_legs.DrawItem += LST_legs_DrawItem;
            LST_legs.MouseDown += LST_legs_MouseDown;
            LST_legs.MouseMove += LST_legs_MouseMove;
            LST_legs.MouseUp += LST_legs_MouseUp;
            BUT_leg_up = new MissionPlanner.Controls.MyButton
            {
                Text = "▲", Location = new System.Drawing.Point(204, 170), Size = new Size(32, 26),
            };
            BUT_leg_down = new MissionPlanner.Controls.MyButton
            {
                Text = "▼", Location = new System.Drawing.Point(204, 202), Size = new Size(32, 26),
            };

            // Right-click menu for split/join on a vertex (built once; items enabled per target).
            editMenu = new System.Windows.Forms.ContextMenuStrip();
            MI_split = new System.Windows.Forms.ToolStripMenuItem("Split leg here");
            MI_join = new System.Windows.Forms.ToolStripMenuItem("Join legs");
            MI_delete = new System.Windows.Forms.ToolStripMenuItem("Delete point");
            MI_split.Click += (s, ev) => DoSplitLeg();
            MI_join.Click += (s, ev) => DoJoinLegs();
            MI_delete.Click += (s, ev) => DoDeletePoint();
            editMenu.Items.Add(MI_split);
            editMenu.Items.Add(MI_join);
            editMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            editMenu.Items.Add(MI_delete);

            lbl_edit_info = new System.Windows.Forms.Label
            {
                Location = new System.Drawing.Point(8, 420), Size = new Size(258, 200),
                ForeColor = System.Drawing.Color.DimGray,
                Font = new System.Drawing.Font("Microsoft Sans Serif", 7.5F),
                Text =
                    "Import a corridor KML, then Number Legs to split the path into\n" +
                    "numbered, colour-coded legs.\n\n" +
                    "• Split at Home splits the corridor at the point nearest the\n" +
                    "  Flight Planner home location.\n" +
                    "• Drag a vertex dot to move it (shared junctions move together).\n" +
                    "• Click a “+” midpoint to add a vertex; a “+” at a free leg end\n" +
                    "  extends that leg.\n" +
                    "• Right-click a vertex to Split leg here / Join legs.\n" +
                    "• Reorder legs with ▲/▼ or drag in the list (valid drops only).\n\n" +
                    "Edits change the geometry used by Generate.",
            };

            BUT_edit_import.Click += BUT_edit_import_Click;
            BUT_edit_save.Click += BUT_edit_save_Click;
            BUT_edit_number.Click += BUT_edit_number_Click;
            BUT_edit_splithome.Click += BUT_edit_splithome_Click;
            CHK_show_wp.CheckedChanged += EditVisibility_Changed;
            CHK_show_plus.CheckedChanged += EditVisibility_Changed;
            CHK_show_labels.CheckedChanged += EditVisibility_Changed;
            BUT_leg_up.Click += (s, ev) => MoveSelectedLeg(-1);
            BUT_leg_down.Click += (s, ev) => MoveSelectedLeg(+1);

            pnl.Controls.Add(lbl_edit_info);
            pnl.Controls.Add(BUT_edit_splithome);
            pnl.Controls.Add(BUT_leg_down);
            pnl.Controls.Add(BUT_leg_up);
            pnl.Controls.Add(LST_legs);
            pnl.Controls.Add(lbl_legs);
            pnl.Controls.Add(CHK_show_labels);
            pnl.Controls.Add(CHK_show_plus);
            pnl.Controls.Add(CHK_show_wp);
            pnl.Controls.Add(BUT_edit_number);
            pnl.Controls.Add(BUT_edit_save);
            pnl.Controls.Add(BUT_edit_import);
            tabEdit.Controls.Add(pnl);
            tabControl1.TabPages.Add(tabEdit);

            tabControl1.SelectedIndexChanged += tabControl1_SelectedIndexChanged;

            map.OnMarkerEnter += Map_OnMarkerEnter_Edit;
            map.OnMarkerLeave += Map_OnMarkerLeave_Edit;
            map.OnMarkerClick += Map_OnMarkerClick_Edit;
            map.MouseDown += Map_MouseDown_Edit;
            map.MouseMove += Map_MouseMove_Edit;
            map.MouseUp += Map_MouseUp_Edit;
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            editMode = tabControl1.SelectedTab == tabEdit;
            hoverEditMarker = null;
            editDragging = false;

            if (editMode)
            {
                // Hide the generated mission overlay; show the raw / edit view.
                _missionOverlay.overlay.Clear();
                if (legsNumbered) DrawEditColored();
                else { ClearEditOverlay(); map.Refresh(); }
            }
            else
            {
                ClearEditOverlay();
                DrawMap();   // restore the normal (dashed features + mission) view
            }
        }

        private void ClearEditOverlay()
        {
            layer_edit.Routes.Clear();
            layer_edit.Markers.Clear();
            editRoutePts = null;
            editVertexMarkers = null;

            // Drop references to now-removed markers. Otherwise a hover set just before a
            // rebuild (e.g. the vertex a split/join menu was opened on) lingers, and the next
            // MouseDown would start dragging it even though the cursor is over empty map.
            hoverEditMarker = null;
            editDragging = false;
            dragMarkers = null;
            dragRoutePts = null;
        }

        // Called from RebuildModel when the loaded feature set changes.
        private void ResetEditState()
        {
            legsNumbered = false;
            hoverEditMarker = null;
            editDragging = false;
            legOrder = new List<int>();
            LST_legs?.Items.Clear();
            if (layer_edit != null) ClearEditOverlay();
        }

        // Clear derived data after an in-place geometry edit, WITHOUT resetting legsNumbered
        // (we stay in the coloured edit view). Checkpoints/alt edits keyed on the old vertex
        // identities no longer line up, so drop them — same policy as RebuildModel.
        private void InvalidateAfterGeometryEdit()
        {
            generatedWps = null;
            elevationPoints = null;
            polylines = null;
            tour = null;
            checkpoints.Clear();
            loiterToAlts.Clear();
            cornerCutAlts.Clear();
            altOverrides.Clear();
            InvalidateCornerCutJobs();
            BUT_accept.Enabled = false;
        }

        // ─── Number Legs / draw the coloured, editable view ─────────────────────────
        private void BUT_edit_number_Click(object sender, EventArgs e)
        {
            if (AllFeatures().Count == 0)
            {
                CustomMessageBox.Show("Import a corridor file first.", "No Corridor");
                return;
            }
            legsNumbered = true;
            DrawEditColored();
        }

        private void DrawEditColored()
        {
            // Coloured legs live in layer_edit; suppress the plain dashed features + mission.
            layer_corridor.Routes.Clear();
            layer_corridor.Markers.Clear();
            _missionOverlay.overlay.Clear();
            ClearEditOverlay();

            var features = AllFeatures();
            if (features.Count == 0) { map.Refresh(); return; }

            editRoutePts = new Dictionary<(long, long), List<(GMapRoute, int)>>();
            editVertexMarkers = new Dictionary<(long, long), List<GMapMarker>>();

            // Split the features into legs (edges), steering the branch-visit order by the
            // user's current leg order. The tour's actual order IS the numbering; keep in sync.
            var actual = ComputeLegOrder(legOrder, out var edges);
            legOrder = actual;

            var legNo = new Dictionary<int, int>();
            for (int i = 0; i < actual.Count; i++) legNo[actual[i]] = i + 1;

            // Count edge-ends per coordinate: a dead-end has exactly one, a junction/intersection ≥2.
            var endCount = new Dictionary<(long, long), int>();
            void Bump((long, long) k) => endCount[k] = endCount.TryGetValue(k, out var c) ? c + 1 : 1;
            foreach (var edge in edges)
            {
                if (edge.Points.Count < 2) continue;
                Bump(Key(edge.Points[0]));
                Bump(Key(edge.Points[edge.Points.Count - 1]));
            }

            // Draw each leg coloured, with an always-on tooltip label at its midpoint.
            foreach (var edge in edges)
            {
                if (edge.Points.Count < 2) continue;
                var col = LegColor(legNo[edge.Id]);
                var route = new GMapRoute(
                    edge.Points.Select(p => new PointLatLng(p.Lat, p.Lng)).ToList(), "leg" + edge.Id)
                {
                    Stroke = new Pen(col, 3),
                };
                layer_edit.Routes.Add(route);
                for (int i = 0; i < edge.Points.Count; i++)
                {
                    var k = Key(edge.Points[i]);
                    if (!editRoutePts.TryGetValue(k, out var lst)) editRoutePts[k] = lst = new List<(GMapRoute, int)>();
                    lst.Add((route, i));
                }

                if (CHK_show_labels.Checked)
                {
                    var mid = EdgeMidpoint(edge.Points);
                    layer_edit.Markers.Add(MakeLegLabel(new PointLatLng(mid.Lat, mid.Lng), legNo[edge.Id], col));
                }
            }

            // Vertex handles + add-vertex "+" markers, from the raw editable features.
            for (int f = 0; f < features.Count; f++)
            {
                var pts = features[f];

                if (CHK_show_wp.Checked)
                    for (int i = 0; i < pts.Count; i++)
                    {
                        var vm = new GMarkerGoogle(new PointLatLng(pts[i].Lat, pts[i].Lng), GMarkerGoogleType.green_small)
                        {
                            Tag = new EditHandle { Feature = f, Index = i },
                        };
                        layer_edit.Markers.Add(vm);
                        var k = Key(pts[i]);
                        if (!editVertexMarkers.TryGetValue(k, out var lst)) editVertexMarkers[k] = lst = new List<GMapMarker>();
                        lst.Add(vm);
                    }

                if (CHK_show_plus.Checked)
                {
                    for (int i = 0; i + 1 < pts.Count; i++)
                    {
                        var mp = Midpoint(pts[i], pts[i + 1]);
                        layer_edit.Markers.Add(new GMapMarkerPlus(new PointLatLng(mp.Lat, mp.Lng))
                        {
                            Tag = new EditMidPlus { Feature = f, Seg = i },
                        });
                    }

                    // Endpoint "+": only at a true dead-end (edge-end count 1), never at an intersection.
                    foreach (int endIdx in new[] { 0, pts.Count - 1 })
                    {
                        if (endCount.TryGetValue(Key(pts[endIdx]), out var c) && c == 1)
                        {
                            var at = EndOffsetPoint(pts, endIdx);   // sit just past the end, along the leg
                            layer_edit.Markers.Add(new GMapMarkerPlus(new PointLatLng(at.Lat, at.Lng))
                            {
                                Tag = new EditEndPlus { Feature = f, Index = endIdx },
                            });
                        }
                    }
                }
            }

            PopulateLegList(edges, legNo);
            map.Refresh();
        }

        private static Color LegColor(int legNo) => LegPalette[(legNo - 1) % LegPalette.Length];

        // An always-on tooltip label, matching the GridUI segment-tooltip style, coloured per leg.
        private static GMapMarker MakeLegLabel(PointLatLng at, int legNo, Color col)
        {
            var m = new GMapMarkerRect(at)
            {
                IsHitTestVisible = false,   // don't steal hover/clicks from vertex + markers
                ToolTipMode = MarkerTooltipMode.Always,
                ToolTipText = "Leg " + legNo,
            };
            m.ToolTip = new GMapToolTip(m)
            {
                Offset = new System.Drawing.Point(0, -6),
                Fill = new SolidBrush(Color.FromArgb(220, col)),
                Foreground = new SolidBrush(col.GetBrightness() < 0.5 ? Color.White : Color.Black),
                Stroke = new Pen(Color.FromArgb(180, Color.Black), 1),
            };
            return m;
        }

        // ─── Legs list (colour + flight order) ──────────────────────────────────────
        private void PopulateLegList(List<Planning.Polyline> edges, Dictionary<int, int> legNo)
        {
            int keepSel = LST_legs.SelectedIndex;
            var byId = edges.ToDictionary(e => e.Id);

            LST_legs.BeginUpdate();
            LST_legs.Items.Clear();
            foreach (var id in legOrder)
            {
                if (!byId.TryGetValue(id, out var edge)) continue;
                double len = 0;
                for (int i = 0; i + 1 < edge.Points.Count; i++) len += edge.Points[i].GetDistance(edge.Points[i + 1]);
                LST_legs.Items.Add(new LegItem
                {
                    EdgeId = id, LegNo = legNo[id], Color = LegColor(legNo[id]), LengthM = len,
                });
            }
            if (keepSel >= 0 && keepSel < LST_legs.Items.Count) LST_legs.SelectedIndex = keepSel;
            LST_legs.EndUpdate();
        }

        private void LST_legs_DrawItem(object sender, System.Windows.Forms.DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= LST_legs.Items.Count) { e.DrawBackground(); return; }
            var item = (LegItem)LST_legs.Items[e.Index];

            // While dragging: mark the picked row, and highlight only reachable drop targets
            // (green), brightening the one under the cursor. Otherwise standard selection.
            bool dragging = legDragFrom >= 0;
            bool selected = (e.State & System.Windows.Forms.DrawItemState.Selected) != 0;
            Color back;
            if (dragging && e.Index == legDragFrom)
                back = Color.LightSteelBlue;
            else if (dragging && legDragTargets != null && legDragTargets.ContainsKey(e.Index))
                back = e.Index == legDragOver ? Color.LimeGreen : Color.Honeydew;
            else if (!dragging && selected)
                back = SystemColors.Highlight;
            else
                back = LST_legs.BackColor;

            using (var bb = new SolidBrush(back)) e.Graphics.FillRectangle(bb, e.Bounds);

            var swatch = new System.Drawing.Rectangle(e.Bounds.Left + 3, e.Bounds.Top + 3, 24, e.Bounds.Height - 6);
            using (var b = new SolidBrush(item.Color)) e.Graphics.FillRectangle(b, swatch);
            e.Graphics.DrawRectangle(System.Drawing.Pens.Black, swatch);

            double dmult = CurrentState.multiplierdist;
            string text = $"Leg {item.LegNo}   {item.LengthM * dmult:F0} {CurrentState.DistanceUnit}";
            Color fore = (!dragging && selected) ? SystemColors.HighlightText : LST_legs.ForeColor;
            using (var b = new SolidBrush(fore))
                e.Graphics.DrawString(text, e.Font, b,
                    new System.Drawing.PointF(swatch.Right + 6, e.Bounds.Top + 2));
        }

        // ─── Leg drag-and-drop (only valid swaps activate) ──────────────────────────
        private void LST_legs_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int idx = LST_legs.IndexFromPoint(e.Location);
            if (idx < 0 || idx >= legOrder.Count) return;
            LST_legs.SelectedIndex = idx;
            legDragFrom = idx;
            legDragOver = idx;
            legDragTargets = ReachableTargets(idx);   // probe reachable slots once, on pick-up
            LST_legs.Invalidate();
        }

        private void LST_legs_MouseMove(object sender, MouseEventArgs e)
        {
            if (legDragFrom < 0 || e.Button != MouseButtons.Left) return;
            int idx = LST_legs.IndexFromPoint(e.Location);
            if (idx != legDragOver) { legDragOver = idx; LST_legs.Invalidate(); }
        }

        private void LST_legs_MouseUp(object sender, MouseEventArgs e)
        {
            if (legDragFrom < 0) return;
            int from = legDragFrom;
            int target = LST_legs.IndexFromPoint(e.Location);
            var targets = legDragTargets;

            legDragFrom = -1;
            legDragOver = -1;
            legDragTargets = null;

            // Drop only lands if the row is a reachable target (a valid swap); else just clear.
            if (target >= 0 && target != from && targets != null && targets.TryGetValue(target, out var ord))
            {
                int movedId = legOrder[from];
                legOrder = ord;
                DrawEditColored();
                int at = legOrder.IndexOf(movedId);
                if (at >= 0 && at < LST_legs.Items.Count) LST_legs.SelectedIndex = at;
            }
            else
            {
                LST_legs.Invalidate();
            }
        }

        // Run the tour builder with the given branch-visit priority and return the resulting
        // leg order (edge ids in flight order). Pure — does not touch UI or the stored order.
        private List<int> ComputeLegOrder(List<int> priority, out List<Planning.Polyline> edges)
        {
            edges = new List<Planning.Polyline>();
            var features = AllFeatures();
            if (features.Count == 0) return new List<int>();

            var home = plugin.Host.cs.PlannedHomeLocation.Lat != 0
                ? plugin.Host.cs.PlannedHomeLocation : features[0].First();
            var built = CorridorTourBuilder.Build(
                features, home, (double)NUM_passoffset.Value, (int)NUM_numpasses.Value, CHK_reverse.Checked,
                priority != null && priority.Count > 0 ? priority : null);
            edges = built.polylines;

            var edgeById = built.polylines.ToDictionary(e => e.Id);
            var order = new List<int>();
            foreach (var step in built.tour)
                if (!order.Contains(step.PolylineId) && edgeById.ContainsKey(step.PolylineId))
                    order.Add(step.PolylineId);
            foreach (var edge in built.polylines)
                if (!order.Contains(edge.Id)) order.Add(edge.Id);
            return order;
        }

        // Which flight-order positions can the leg at fromIdx actually reach, and the resulting
        // leg order for each? The DFS re-sorts a requested priority, so we probe: try inserting
        // the leg at each rank, rebuild, and record where it truly lands. Keyed by landing
        // position → the order that achieves it (its own current position is always reachable).
        private Dictionary<int, List<int>> ReachableTargets(int fromIdx)
        {
            var result = new Dictionary<int, List<int>>();
            if (fromIdx < 0 || fromIdx >= legOrder.Count) return result;

            int movedId = legOrder[fromIdx];
            var without = new List<int>(legOrder);
            without.RemoveAt(fromIdx);

            for (int t = 0; t <= without.Count; t++)
            {
                var cand = new List<int>(without);
                cand.Insert(t, movedId);
                var actual = ComputeLegOrder(cand, out _);
                int p = actual.IndexOf(movedId);
                if (p >= 0 && !result.ContainsKey(p)) result[p] = actual;
            }
            return result;
        }

        // Move the selected leg to the NEXT reachable position in the given direction. Because
        // the tour constrains branch order, the nearest valid slot may be several rows away
        // (e.g. 12 → 3); the legs in between shift to fill the gap. No-op if none is reachable.
        private void MoveSelectedLeg(int dir)
        {
            int i = LST_legs.SelectedIndex;
            if (i < 0 || i >= legOrder.Count) return;
            int movedId = legOrder[i];

            var reachable = ReachableTargets(i);
            int bestP = dir < 0 ? -1 : int.MaxValue;
            List<int> best = null;
            foreach (var kv in reachable)
            {
                int p = kv.Key;
                if (dir < 0) { if (p < i && p > bestP) { bestP = p; best = kv.Value; } }
                else         { if (p > i && p < bestP) { bestP = p; best = kv.Value; } }
            }
            if (best == null) return;

            legOrder = best;
            DrawEditColored();
            int at = legOrder.IndexOf(movedId);
            if (at >= 0 && at < LST_legs.Items.Count) LST_legs.SelectedIndex = at;
        }

        private void EditVisibility_Changed(object sender, EventArgs e)
        {
            if (editMode && legsNumbered) DrawEditColored();
        }

        private static PointLatLngAlt Midpoint(PointLatLngAlt a, PointLatLngAlt b)
            => new PointLatLngAlt((a.Lat + b.Lat) / 2, (a.Lng + b.Lng) / 2, (a.Alt + b.Alt) / 2);

        // A point just past a leg's free end, continuing the direction of the final segment
        // leading up to it (a small fraction of that segment). Used for both the end "+" marker
        // and the vertex it adds, so the marker clears the endpoint dot and the extension is a
        // modest nudge the user then drags into place.
        private static PointLatLngAlt EndOffsetPoint(List<PointLatLngAlt> pts, int endIdx)
        {
            const double Frac = 0.2;
            PointLatLngAlt tip, prev;
            if (endIdx == 0) { tip = pts[0]; prev = pts[1]; }
            else { tip = pts[pts.Count - 1]; prev = pts[pts.Count - 2]; }
            return new PointLatLngAlt(
                tip.Lat + Frac * (tip.Lat - prev.Lat),
                tip.Lng + Frac * (tip.Lng - prev.Lng),
                tip.Alt);
        }

        // Midpoint by path distance along the edge (visually centred on the leg, not the
        // average of its endpoints — better label placement on bent legs).
        private static PointLatLngAlt EdgeMidpoint(List<PointLatLngAlt> pts)
        {
            double total = 0;
            for (int i = 0; i + 1 < pts.Count; i++) total += pts[i].GetDistance(pts[i + 1]);
            double half = total / 2, acc = 0;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                double d = pts[i].GetDistance(pts[i + 1]);
                if (acc + d >= half && d > 1e-9)
                {
                    double t = (half - acc) / d;
                    return new PointLatLngAlt(
                        pts[i].Lat + t * (pts[i + 1].Lat - pts[i].Lat),
                        pts[i].Lng + t * (pts[i + 1].Lng - pts[i].Lng), 0);
                }
                acc += d;
            }
            return pts[pts.Count / 2];
        }

        // ─── Vertex drag ────────────────────────────────────────────────────────────
        private void Map_OnMarkerEnter_Edit(GMapMarker item)
        {
            if (!editMode) return;
            if (item?.Tag is EditHandle || item?.Tag is EditMidPlus || item?.Tag is EditEndPlus)
                hoverEditMarker = item;
        }

        private void Map_OnMarkerLeave_Edit(GMapMarker item)
        {
            if (item == hoverEditMarker) hoverEditMarker = null;
        }

        private void Map_MouseDown_Edit(object sender, MouseEventArgs e)
        {
            if (!editMode || e.Button != MouseButtons.Left) return;
            if (!map.IsMouseOverMarker) return;                    // not actually over a marker → let the map pan
            if (!(hoverEditMarker?.Tag is EditHandle h)) return;   // only vertices drag

            var pts = FeatureAt(h.Feature);
            if (h.Index < 0 || h.Index >= pts.Count) return;

            // Capture every route point + vertex marker coincident with the grabbed vertex,
            // so a shared junction moves as one and stays snapped.
            var k = Key(pts[h.Index]);
            dragRoutePts = editRoutePts != null && editRoutePts.TryGetValue(k, out var rp)
                ? new List<(GMapRoute, int)>(rp) : new List<(GMapRoute, int)>();
            dragMarkers = editVertexMarkers != null && editVertexMarkers.TryGetValue(k, out var vm)
                ? new List<GMapMarker>(vm) : new List<GMapMarker> { hoverEditMarker };
            editDragging = true;
        }

        private void Map_MouseMove_Edit(object sender, MouseEventArgs e)
        {
            if (!editMode || !editDragging) return;
            var ll = map.FromLocalToLatLng(e.X, e.Y);
            var np = new PointLatLng(ll.Lat, ll.Lng);

            foreach (var m in dragMarkers)
            {
                m.Position = np;
                map.UpdateMarkerLocalPosition(m);
                if (m.Tag is EditHandle h)
                {
                    var pts = FeatureAt(h.Feature);
                    if (h.Index >= 0 && h.Index < pts.Count)
                        pts[h.Index] = new PointLatLngAlt(ll.Lat, ll.Lng, pts[h.Index].Alt);
                }
            }

            var seen = new HashSet<GMapRoute>();
            foreach (var (route, idx) in dragRoutePts)
            {
                if (idx >= 0 && idx < route.Points.Count) route.Points[idx] = np;
                seen.Add(route);
            }
            foreach (var route in seen) map.UpdateRouteLocalPosition(route);

            map.Invalidate();
        }

        private void Map_MouseUp_Edit(object sender, MouseEventArgs e)
        {
            if (!editMode || !editDragging) return;
            editDragging = false;
            dragRoutePts = null;
            dragMarkers = null;

            // Geometry moved: the split/junction topology and numbering may have changed.
            InvalidateAfterGeometryEdit();
            DrawEditColored();
        }

        // ─── Add-vertex "+" markers ─────────────────────────────────────────────────
        private void Map_OnMarkerClick_Edit(GMapMarker item, object ei)
        {
            if (!editMode || item == null) return;
            var me = ei as MouseEventArgs;

            // Right-click a vertex → Split leg here / Join legs (each enabled only when valid).
            if (me != null && me.Button == MouseButtons.Right && item.Tag is EditHandle rh)
            {
                menuTargetHandle = rh;
                var pts = FeatureAt(rh.Feature);
                if (rh.Index < 0 || rh.Index >= pts.Count) return;
                var key = Key(pts[rh.Index]);
                MI_split.Enabled = rh.Index > 0 && rh.Index < pts.Count - 1 && CountFeaturesAtKey(key) == 1;
                MI_join.Enabled = CanJoinAt(key, out _, out _);
                MI_delete.Enabled = pts.Count > 2;   // keep the feature a valid (≥2-point) line
                editMenu.Show(map, me.Location);
                return;
            }

            if (me != null && me.Button != MouseButtons.Left) return;   // adds are left-click only

            if (item.Tag is EditMidPlus mp)
            {
                var pts = FeatureAt(mp.Feature);
                if (mp.Seg < 0 || mp.Seg + 1 >= pts.Count) return;
                pts.Insert(mp.Seg + 1, Midpoint(pts[mp.Seg], pts[mp.Seg + 1]));
                InvalidateAfterGeometryEdit();
                DrawEditColored();
            }
            else if (item.Tag is EditEndPlus ep)
            {
                var pts = FeatureAt(ep.Feature);
                if (pts.Count < 2) return;

                // Extend a small amount beyond the tip, continuing the leg's direction.
                var np = EndOffsetPoint(pts, ep.Index);
                if (ep.Index == 0) pts.Insert(0, np); else pts.Add(np);
                InvalidateAfterGeometryEdit();
                DrawEditColored();
            }
        }

        // ─── Split / Join legs ──────────────────────────────────────────────────────

        // Map a global feature index (into AllFeatures()) back to its (file, local) slot in
        // featuresByFile so structural edits can mutate the stored geometry.
        private (int file, int local) LocateFeature(int globalIndex)
        {
            int acc = 0;
            for (int fi = 0; fi < featuresByFile.Count; fi++)
            {
                if (globalIndex < acc + featuresByFile[fi].Count) return (fi, globalIndex - acc);
                acc += featuresByFile[fi].Count;
            }
            return (-1, -1);
        }

        private int CountFeaturesAtKey((long, long) key)
        {
            int c = 0;
            foreach (var f in AllFeatures())
                if (f.Any(pt => Key(pt) == key)) c++;
            return c;
        }

        // A vertex is joinable when exactly two features meet there end-to-end (degree 2) and
        // no feature passes through it interior (that would be a higher-degree junction).
        private bool CanJoinAt((long, long) key, out int fa, out int fb)
        {
            fa = fb = -1;
            var feats = AllFeatures();
            var endpointFeatures = new List<int>();
            for (int f = 0; f < feats.Count; f++)
            {
                var pts = feats[f];
                bool endpoint = false;
                for (int i = 0; i < pts.Count; i++)
                {
                    if (Key(pts[i]) != key) continue;
                    if (i == 0 || i == pts.Count - 1) endpoint = true;
                    else return false;   // interior occurrence ⇒ not a clean degree-2 join
                }
                if (endpoint) endpointFeatures.Add(f);
            }
            if (endpointFeatures.Count != 2) return false;
            fa = endpointFeatures[0];
            fb = endpointFeatures[1];
            return true;
        }

        // Split the feature at the given vertex into two features sharing that vertex, so the
        // tour builder treats it as a junction (two legs). Vertex must be interior.
        private void DoSplitLeg()
        {
            var h = menuTargetHandle;
            if (h == null) return;
            var (file, local) = LocateFeature(h.Feature);
            if (file < 0) return;
            var pts = featuresByFile[file][local];
            if (h.Index <= 0 || h.Index >= pts.Count - 1) return;

            var first = pts.GetRange(0, h.Index + 1);              // 0..i  (ends at V)
            var second = pts.GetRange(h.Index, pts.Count - h.Index); // i..end (starts at V)
            featuresByFile[file][local] = first;
            featuresByFile[file].Insert(local + 1, second);

            AfterStructuralEdit();
        }

        // Delete the right-clicked vertex from its feature. Only the clicked feature is
        // affected (a coincident junction vertex in other features stays); the feature must
        // keep at least two points to remain a valid line.
        private void DoDeletePoint()
        {
            var h = menuTargetHandle;
            if (h == null) return;
            var (file, local) = LocateFeature(h.Feature);
            if (file < 0) return;
            var pts = featuresByFile[file][local];
            if (h.Index < 0 || h.Index >= pts.Count || pts.Count <= 2) return;

            pts.RemoveAt(h.Index);
            // Geometry-only edit (leg set usually unchanged) — keep the leg order like the
            // add-vertex paths; stale ids are ignored on the rebuild if a junction did change.
            InvalidateAfterGeometryEdit();
            DrawEditColored();
        }

        // Merge the two legs meeting at a degree-2 vertex back into one continuous feature.
        private void DoJoinLegs()
        {
            var h = menuTargetHandle;
            if (h == null) return;
            var feats = AllFeatures();
            if (h.Feature < 0 || h.Feature >= feats.Count) return;
            var v = feats[h.Feature][h.Index];
            var key = Key(v);
            if (!CanJoinAt(key, out int fa, out int fb)) return;

            var A = feats[fa];
            var B = feats[fb];
            // Orient A to END at V and B to START at V, then drop the duplicate shared vertex.
            var a = Key(A[0]) == key ? Enumerable.Reverse(A).ToList() : A.ToList();
            var b = Key(B[B.Count - 1]) == key ? Enumerable.Reverse(B).ToList() : B.ToList();
            var merged = new List<PointLatLngAlt>(a);
            merged.AddRange(b.Skip(1));

            var (fileA, localA) = LocateFeature(fa);
            var (fileB, localB) = LocateFeature(fb);
            if (fileA < 0 || fileB < 0) return;

            // Remove B, then replace A's slot with the merged feature (adjust A's index if the
            // removal happened earlier in the same file).
            featuresByFile[fileB].RemoveAt(localB);
            if (fileA == fileB && localA > localB) localA--;
            featuresByFile[fileA][localA] = merged;

            AfterStructuralEdit();
        }

        // Split the corridor at the point nearest the Flight Planner home, so a leg boundary
        // sits at the launch location. Inserts a vertex mid-segment if the nearest point isn't
        // already a vertex, then splits there.
        private void BUT_edit_splithome_Click(object sender, EventArgs e)
        {
            if (AllFeatures().Count == 0)
            {
                CustomMessageBox.Show("Import a corridor file first.", "No Corridor");
                return;
            }
            var home = plugin.Host.cs.PlannedHomeLocation;
            if (home == null || home.Lat == 0)
            {
                CustomMessageBox.Show("Set a home location in the Flight Planner first.", "No Home");
                return;
            }

            if (!FindNearestOnFeatures(home, out int f, out int seg, out double t, out var pt)) return;
            var (file, local) = LocateFeature(f);
            if (file < 0) return;
            var pts = featuresByFile[file][local];

            const double eps = 1e-4;
            int splitIdx;
            if (t <= eps) splitIdx = seg;
            else if (t >= 1 - eps) splitIdx = seg + 1;
            else { pts.Insert(seg + 1, pt); splitIdx = seg + 1; }

            if (splitIdx <= 0 || splitIdx >= pts.Count - 1)
            {
                CustomMessageBox.Show(
                    "The point nearest home is already at a leg end — nothing to split.", "Split at Home");
                return;
            }

            var first = pts.GetRange(0, splitIdx + 1);
            var second = pts.GetRange(splitIdx, pts.Count - splitIdx);
            featuresByFile[file][local] = first;
            featuresByFile[file].Insert(local + 1, second);

            legsNumbered = true;
            AfterStructuralEdit();
        }

        // Nearest point on any loaded feature to `home` (flat-earth projection onto each
        // segment). Returns the feature/segment, the fraction t along it, and the point.
        private bool FindNearestOnFeatures(PointLatLngAlt home, out int fBest, out int segBest,
                                           out double tBest, out PointLatLngAlt ptBest)
        {
            fBest = segBest = -1; tBest = 0; ptBest = null;
            var feats = AllFeatures();
            double cosLat = Math.Cos(home.Lat * Math.PI / 180.0);
            double MX(double lng) => lng * cosLat * 111319.5;
            double MY(double lat) => lat * 111319.5;
            double hx = MX(home.Lng), hy = MY(home.Lat);
            double best = double.MaxValue;

            for (int f = 0; f < feats.Count; f++)
            {
                var pts = feats[f];
                for (int s = 0; s + 1 < pts.Count; s++)
                {
                    double ax = MX(pts[s].Lng), ay = MY(pts[s].Lat);
                    double bx = MX(pts[s + 1].Lng), by = MY(pts[s + 1].Lat);
                    double dx = bx - ax, dy = by - ay;
                    double len2 = dx * dx + dy * dy;
                    double t = len2 > 1e-9 ? ((hx - ax) * dx + (hy - ay) * dy) / len2 : 0;
                    t = Math.Max(0, Math.Min(1, t));
                    double px = ax + t * dx, py = ay + t * dy;
                    double d2 = (px - hx) * (px - hx) + (py - hy) * (py - hy);
                    if (d2 < best)
                    {
                        best = d2; fBest = f; segBest = s; tBest = t;
                        ptBest = new PointLatLngAlt(
                            pts[s].Lat + t * (pts[s + 1].Lat - pts[s].Lat),
                            pts[s].Lng + t * (pts[s + 1].Lng - pts[s].Lng),
                            pts[s].Alt + t * (pts[s + 1].Alt - pts[s].Alt));
                    }
                }
            }
            return fBest >= 0;
        }

        // Common tail after a split/join: the edge set + numbering changed, so drop the stale
        // leg order, refresh the file-count display, invalidate the mission, and redraw.
        private void AfterStructuralEdit()
        {
            legOrder = new List<int>();
            RefreshFileList();
            InvalidateAfterGeometryEdit();
            if (legsNumbered) DrawEditColored();
            else { ClearEditOverlay(); DrawMap(); }
        }

        // Rewrite the Options-tab file list counts after features were split/joined.
        private void RefreshFileList()
        {
            LST_mainline.BeginUpdate();
            LST_mainline.Items.Clear();
            for (int i = 0; i < featureFiles.Count; i++)
            {
                int cnt = featuresByFile[i].Count;
                LST_mainline.Items.Add(
                    $"{Path.GetFileName(featureFiles[i])}  ({cnt} feature{(cnt == 1 ? "" : "s")})");
            }
            LST_mainline.EndUpdate();
        }

        // ─── Import / Save KML ──────────────────────────────────────────────────────
        private void BUT_edit_import_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Import Corridor File(s)";
                dlg.Filter = "All Supported|*.kml;*.kmz;*.shp|KML/KMZ|*.kml;*.kmz|Shapefile|*.shp";
                dlg.Multiselect = true;
                if (dlg.ShowDialog() != DialogResult.OK) return;

                foreach (var path in dlg.FileNames)
                {
                    List<List<PointLatLngAlt>> feats;
                    try
                    {
                        feats = LoadCorridorFeatures(path);
                    }
                    catch (Exception ex)
                    {
                        CustomMessageBox.Show("Error loading file:\n" + ex.Message, "Load Error");
                        continue;
                    }

                    feats = feats?.Where(f => f != null && f.Count >= 2).ToList();
                    if (feats == null || feats.Count == 0)
                    {
                        CustomMessageBox.Show(
                            "No line/polyline geometry found in " + Path.GetFileName(path) + ".",
                            "No Corridor Found");
                        continue;
                    }

                    featureFiles.Add(path);
                    featuresByFile.Add(feats);
                    LST_mainline.Items.Add(
                        $"{Path.GetFileName(path)}  ({feats.Count} feature{(feats.Count == 1 ? "" : "s")})");
                }
            }

            // New geometry: reset to the un-numbered raw view (matches RebuildModel).
            RebuildModel();
            DrawMap();
            ZoomToFitFeatures();
        }

        private void BUT_edit_save_Click(object sender, EventArgs e)
        {
            var features = AllFeatures();
            if (features.Count == 0)
            {
                CustomMessageBox.Show("Nothing to save — import a corridor first.", "No Corridor");
                return;
            }

            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Save Corridor Path";
                dlg.Filter = "KML|*.kml";
                dlg.DefaultExt = "kml";
                dlg.FileName = "corridor.kml";
                if (dlg.ShowDialog() != DialogResult.OK) return;

                try
                {
                    var doc = new Document();
                    for (int i = 0; i < features.Count; i++)
                    {
                        var coords = new CoordinateCollection();
                        foreach (var p in features[i])
                            coords.Add(new Vector(p.Lat, p.Lng, p.Alt));

                        var ls = new LineString { Coordinates = coords };
                        doc.AddFeature(new SharpKml.Dom.Placemark { Name = "Corridor " + (i + 1), Geometry = ls });
                    }

                    var kml = new Kml { Feature = doc };
                    var kmlFile = KmlFile.Create(kml, false);
                    using (var stream = File.Create(dlg.FileName))
                        kmlFile.Save(stream);
                }
                catch (Exception ex)
                {
                    log.Error("Corridor KML save failed", ex);
                    CustomMessageBox.Show("Error saving file:\n" + ex.Message, "Save Error");
                }
            }
        }
    }
}
