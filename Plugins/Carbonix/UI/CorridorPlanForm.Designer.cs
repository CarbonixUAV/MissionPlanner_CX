namespace Carbonix
{
    partial class CorridorPlanForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.map = new MissionPlanner.Controls.myGMAP();
            this.splitter_bottom = new System.Windows.Forms.Splitter();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabOptions = new System.Windows.Forms.TabPage();
            this.pnl_scroll = new System.Windows.Forms.Panel();
            this.grp_mainline = new System.Windows.Forms.GroupBox();
            this.LST_mainline = new System.Windows.Forms.ListBox();
            this.BUT_mainline_add = new MissionPlanner.Controls.MyButton();
            this.BUT_mainline_remove = new MissionPlanner.Controls.MyButton();
            this.grp_branches = new System.Windows.Forms.GroupBox();
            this.LST_branches = new System.Windows.Forms.ListBox();
            this.BUT_branches_add = new MissionPlanner.Controls.MyButton();
            this.BUT_branches_remove = new MissionPlanner.Controls.MyButton();
            this.grp_altitude = new System.Windows.Forms.GroupBox();
            this.tbl_altitude = new System.Windows.Forms.TableLayoutPanel();
            this.lbl_minalgl = new System.Windows.Forms.Label();
            this.NUM_minalgl = new System.Windows.Forms.NumericUpDown();
            this.lbl_unit1 = new System.Windows.Forms.Label();
            this.lbl_maxagl = new System.Windows.Forms.Label();
            this.NUM_maxagl = new System.Windows.Forms.NumericUpDown();
            this.lbl_unit2 = new System.Windows.Forms.Label();
            this.lbl_defagl = new System.Windows.Forms.Label();
            this.NUM_defagl = new System.Windows.Forms.NumericUpDown();
            this.lbl_unit3 = new System.Windows.Forms.Label();
            this.grp_speed = new System.Windows.Forms.GroupBox();
            this.tbl_speed = new System.Windows.Forms.TableLayoutPanel();
            this.lbl_speed = new System.Windows.Forms.Label();
            this.NUM_speed = new System.Windows.Forms.NumericUpDown();
            this.lbl_speed_unit = new System.Windows.Forms.Label();
            this.grp_corridor = new System.Windows.Forms.GroupBox();
            this.lbl_coverage = new System.Windows.Forms.Label();
            this.tbl_corridor = new System.Windows.Forms.TableLayoutPanel();
            this.lbl_numpasses = new System.Windows.Forms.Label();
            this.NUM_numpasses = new System.Windows.Forms.NumericUpDown();
            this.lbl_passes_unit = new System.Windows.Forms.Label();
            this.lbl_passoffset = new System.Windows.Forms.Label();
            this.NUM_passoffset = new System.Windows.Forms.NumericUpDown();
            this.lbl_offset_unit = new System.Windows.Forms.Label();
            this.grp_options = new System.Windows.Forms.GroupBox();
            this.CHK_reverse = new System.Windows.Forms.CheckBox();
            this.CHK_returnpath = new System.Windows.Forms.CheckBox();
            this.grp_turns = new System.Windows.Forms.GroupBox();
            this.lbl_turninfo = new System.Windows.Forms.Label();
            this.tbl_turns = new System.Windows.Forms.TableLayoutPanel();
            this.lbl_low_thresh = new System.Windows.Forms.Label();
            this.NUM_low_thresh = new System.Windows.Forms.NumericUpDown();
            this.lbl_deg_low = new System.Windows.Forms.Label();
            this.lbl_high_thresh = new System.Windows.Forms.Label();
            this.NUM_high_thresh = new System.Windows.Forms.NumericUpDown();
            this.lbl_deg_high = new System.Windows.Forms.Label();
            this.lbl_extension = new System.Windows.Forms.Label();
            this.NUM_extension = new System.Windows.Forms.NumericUpDown();
            this.lbl_ext_unit = new System.Windows.Forms.Label();
            this.lbl_turnradius = new System.Windows.Forms.Label();
            this.NUM_turnradius = new System.Windows.Forms.NumericUpDown();
            this.lbl_tr_unit = new System.Windows.Forms.Label();
            this.lbl_cornerradius = new System.Windows.Forms.Label();
            this.NUM_cornerradius = new System.Windows.Forms.NumericUpDown();
            this.lbl_cr_unit = new System.Windows.Forms.Label();
            this.grp_stats = new System.Windows.Forms.GroupBox();
            this.lbl_stats = new System.Windows.Forms.Label();
            this.pnl_buttons = new System.Windows.Forms.Panel();
            this.BUT_generate = new MissionPlanner.Controls.MyButton();
            this.BUT_accept = new MissionPlanner.Controls.MyButton();
            this.pnl_elevation = new System.Windows.Forms.Panel();
            this.elev_profile = new Carbonix.UI.ElevationProfileControl();
            this.lbl_elev_title = new System.Windows.Forms.Label();
            this.tabControl1.SuspendLayout();
            this.tabOptions.SuspendLayout();
            this.pnl_scroll.SuspendLayout();
            this.grp_mainline.SuspendLayout();
            this.grp_branches.SuspendLayout();
            this.grp_altitude.SuspendLayout();
            this.tbl_altitude.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_minalgl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_maxagl)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_defagl)).BeginInit();
            this.grp_speed.SuspendLayout();
            this.tbl_speed.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_speed)).BeginInit();
            this.grp_corridor.SuspendLayout();
            this.tbl_corridor.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_numpasses)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_passoffset)).BeginInit();
            this.grp_options.SuspendLayout();
            this.grp_turns.SuspendLayout();
            this.tbl_turns.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_low_thresh)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_high_thresh)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_extension)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_turnradius)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_cornerradius)).BeginInit();
            this.grp_stats.SuspendLayout();
            this.pnl_buttons.SuspendLayout();
            this.pnl_elevation.SuspendLayout();
            this.SuspendLayout();
            // 
            // map
            // 
            this.map.Bearing = 0F;
            this.map.CanDragMap = true;
            this.map.Dock = System.Windows.Forms.DockStyle.Fill;
            this.map.EmptyTileColor = System.Drawing.Color.Gray;
            this.map.GrayScaleMode = false;
            this.map.HelperLineOption = GMap.NET.WindowsForms.HelperLineOptions.DontShow;
            this.map.HoldInvalidation = false;
            this.map.LevelsKeepInMemmory = 5;
            this.map.Location = new System.Drawing.Point(0, 0);
            this.map.MarkersEnabled = true;
            this.map.MaxZoom = 24;
            this.map.MinZoom = 2;
            this.map.MouseWheelZoomType = GMap.NET.MouseWheelZoomType.MousePositionWithoutCenter;
            this.map.Name = "map";
            this.map.NegativeMode = false;
            this.map.PolygonsEnabled = true;
            this.map.RetryLoadTile = 0;
            this.map.RoutesEnabled = true;
            this.map.ScaleMode = GMap.NET.WindowsForms.ScaleModes.Fractional;
            this.map.SelectedAreaFillColor = System.Drawing.Color.FromArgb(((int)(((byte)(33)))), ((int)(((byte)(65)))), ((int)(((byte)(105)))), ((int)(((byte)(225)))));
            this.map.ShowTileGridLines = false;
            this.map.Size = new System.Drawing.Size(905, 515);
            this.map.TabIndex = 0;
            this.map.Zoom = 3D;
            // 
            // splitter_bottom
            // 
            this.splitter_bottom.BackColor = System.Drawing.SystemColors.ControlDark;
            this.splitter_bottom.Cursor = System.Windows.Forms.Cursors.HSplit;
            this.splitter_bottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.splitter_bottom.Location = new System.Drawing.Point(0, 515);
            this.splitter_bottom.Name = "splitter_bottom";
            this.splitter_bottom.Size = new System.Drawing.Size(905, 5);
            this.splitter_bottom.TabIndex = 1;
            this.splitter_bottom.TabStop = false;
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabOptions);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Right;
            this.tabControl1.Location = new System.Drawing.Point(905, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(295, 750);
            this.tabControl1.TabIndex = 2;
            // 
            // tabOptions
            // 
            this.tabOptions.Controls.Add(this.pnl_scroll);
            this.tabOptions.Location = new System.Drawing.Point(4, 22);
            this.tabOptions.Name = "tabOptions";
            this.tabOptions.Padding = new System.Windows.Forms.Padding(3);
            this.tabOptions.Size = new System.Drawing.Size(287, 724);
            this.tabOptions.TabIndex = 0;
            this.tabOptions.Text = "Options";
            this.tabOptions.UseVisualStyleBackColor = true;
            // 
            // pnl_scroll
            // 
            this.pnl_scroll.AutoScroll = true;
            this.pnl_scroll.Controls.Add(this.grp_mainline);
            this.pnl_scroll.Controls.Add(this.grp_branches);
            this.pnl_scroll.Controls.Add(this.grp_altitude);
            this.pnl_scroll.Controls.Add(this.grp_speed);
            this.pnl_scroll.Controls.Add(this.grp_corridor);
            this.pnl_scroll.Controls.Add(this.grp_options);
            this.pnl_scroll.Controls.Add(this.grp_turns);
            this.pnl_scroll.Controls.Add(this.grp_stats);
            this.pnl_scroll.Controls.Add(this.pnl_buttons);
            this.pnl_scroll.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnl_scroll.Location = new System.Drawing.Point(3, 3);
            this.pnl_scroll.Name = "pnl_scroll";
            this.pnl_scroll.Padding = new System.Windows.Forms.Padding(4);
            this.pnl_scroll.Size = new System.Drawing.Size(281, 718);
            this.pnl_scroll.TabIndex = 0;
            //
            // grp_mainline
            //
            this.grp_mainline.Controls.Add(this.LST_mainline);
            this.grp_mainline.Controls.Add(this.BUT_mainline_add);
            this.grp_mainline.Controls.Add(this.BUT_mainline_remove);
            this.grp_mainline.Dock = System.Windows.Forms.DockStyle.Top;
            this.grp_mainline.Location = new System.Drawing.Point(4, 828);
            this.grp_mainline.Name = "grp_mainline";
            this.grp_mainline.Padding = new System.Windows.Forms.Padding(4);
            this.grp_mainline.Size = new System.Drawing.Size(273, 110);
            this.grp_mainline.TabIndex = 0;
            this.grp_mainline.TabStop = false;
            this.grp_mainline.Text = "Main Line Segments";
            //
            // LST_mainline
            //
            this.LST_mainline.FormattingEnabled = true;
            this.LST_mainline.Location = new System.Drawing.Point(6, 18);
            this.LST_mainline.Name = "LST_mainline";
            this.LST_mainline.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
            this.LST_mainline.Size = new System.Drawing.Size(261, 56);
            this.LST_mainline.TabIndex = 0;
            //
            // BUT_mainline_add
            //
            this.BUT_mainline_add.Location = new System.Drawing.Point(6, 80);
            this.BUT_mainline_add.Name = "BUT_mainline_add";
            this.BUT_mainline_add.Size = new System.Drawing.Size(80, 23);
            this.BUT_mainline_add.TabIndex = 1;
            this.BUT_mainline_add.Text = "Add…";
            this.BUT_mainline_add.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.BUT_mainline_add.Click += new System.EventHandler(this.BUT_mainline_add_Click);
            //
            // BUT_mainline_remove
            //
            this.BUT_mainline_remove.Location = new System.Drawing.Point(92, 80);
            this.BUT_mainline_remove.Name = "BUT_mainline_remove";
            this.BUT_mainline_remove.Size = new System.Drawing.Size(80, 23);
            this.BUT_mainline_remove.TabIndex = 2;
            this.BUT_mainline_remove.Text = "Remove";
            this.BUT_mainline_remove.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.BUT_mainline_remove.Click += new System.EventHandler(this.BUT_mainline_remove_Click);
            //
            // grp_branches
            //
            this.grp_branches.Controls.Add(this.LST_branches);
            this.grp_branches.Controls.Add(this.BUT_branches_add);
            this.grp_branches.Controls.Add(this.BUT_branches_remove);
            this.grp_branches.Dock = System.Windows.Forms.DockStyle.Top;
            this.grp_branches.Location = new System.Drawing.Point(4, 718);
            this.grp_branches.Name = "grp_branches";
            this.grp_branches.Padding = new System.Windows.Forms.Padding(4);
            this.grp_branches.Size = new System.Drawing.Size(273, 110);
            this.grp_branches.TabIndex = 1;
            this.grp_branches.TabStop = false;
            this.grp_branches.Text = "Branches";
            //
            // LST_branches
            //
            this.LST_branches.FormattingEnabled = true;
            this.LST_branches.Location = new System.Drawing.Point(6, 18);
            this.LST_branches.Name = "LST_branches";
            this.LST_branches.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
            this.LST_branches.Size = new System.Drawing.Size(261, 56);
            this.LST_branches.TabIndex = 0;
            //
            // BUT_branches_add
            //
            this.BUT_branches_add.Location = new System.Drawing.Point(6, 80);
            this.BUT_branches_add.Name = "BUT_branches_add";
            this.BUT_branches_add.Size = new System.Drawing.Size(80, 23);
            this.BUT_branches_add.TabIndex = 1;
            this.BUT_branches_add.Text = "Add…";
            this.BUT_branches_add.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.BUT_branches_add.Click += new System.EventHandler(this.BUT_branches_add_Click);
            //
            // BUT_branches_remove
            //
            this.BUT_branches_remove.Location = new System.Drawing.Point(92, 80);
            this.BUT_branches_remove.Name = "BUT_branches_remove";
            this.BUT_branches_remove.Size = new System.Drawing.Size(80, 23);
            this.BUT_branches_remove.TabIndex = 2;
            this.BUT_branches_remove.Text = "Remove";
            this.BUT_branches_remove.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.BUT_branches_remove.Click += new System.EventHandler(this.BUT_branches_remove_Click);
            //
            // grp_altitude
            // 
            this.grp_altitude.Controls.Add(this.tbl_altitude);
            this.grp_altitude.Dock = System.Windows.Forms.DockStyle.Top;
            this.grp_altitude.Location = new System.Drawing.Point(4, 508);
            this.grp_altitude.Name = "grp_altitude";
            this.grp_altitude.Padding = new System.Windows.Forms.Padding(4, 2, 4, 4);
            this.grp_altitude.Size = new System.Drawing.Size(273, 88);
            this.grp_altitude.TabIndex = 1;
            this.grp_altitude.TabStop = false;
            this.grp_altitude.Text = "Altitude (AGL)";
            // 
            // tbl_altitude
            // 
            this.tbl_altitude.ColumnCount = 3;
            this.tbl_altitude.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 85F));
            this.tbl_altitude.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 65F));
            this.tbl_altitude.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tbl_altitude.Controls.Add(this.lbl_minalgl, 0, 0);
            this.tbl_altitude.Controls.Add(this.NUM_minalgl, 1, 0);
            this.tbl_altitude.Controls.Add(this.lbl_unit1, 2, 0);
            this.tbl_altitude.Controls.Add(this.lbl_maxagl, 0, 1);
            this.tbl_altitude.Controls.Add(this.NUM_maxagl, 1, 1);
            this.tbl_altitude.Controls.Add(this.lbl_unit2, 2, 1);
            this.tbl_altitude.Controls.Add(this.lbl_defagl, 0, 2);
            this.tbl_altitude.Controls.Add(this.NUM_defagl, 1, 2);
            this.tbl_altitude.Controls.Add(this.lbl_unit3, 2, 2);
            this.tbl_altitude.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tbl_altitude.Location = new System.Drawing.Point(4, 15);
            this.tbl_altitude.Name = "tbl_altitude";
            this.tbl_altitude.RowCount = 3;
            this.tbl_altitude.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tbl_altitude.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tbl_altitude.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tbl_altitude.Size = new System.Drawing.Size(265, 69);
            this.tbl_altitude.TabIndex = 0;
            // 
            // lbl_minalgl
            // 
            this.lbl_minalgl.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.lbl_minalgl.AutoSize = true;
            this.lbl_minalgl.Location = new System.Drawing.Point(31, 5);
            this.lbl_minalgl.Name = "lbl_minalgl";
            this.lbl_minalgl.Size = new System.Drawing.Size(51, 13);
            this.lbl_minalgl.TabIndex = 0;
            this.lbl_minalgl.Text = "Min AGL:";
            // 
            // NUM_minalgl
            // 
            this.NUM_minalgl.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.NUM_minalgl.Location = new System.Drawing.Point(88, 3);
            this.NUM_minalgl.Maximum = new decimal(new int[] {
            5000,
            0,
            0,
            0});
            this.NUM_minalgl.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.NUM_minalgl.Name = "NUM_minalgl";
            this.NUM_minalgl.Size = new System.Drawing.Size(59, 20);
            this.NUM_minalgl.TabIndex = 1;
            this.NUM_minalgl.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.NUM_minalgl.Value = new decimal(new int[] {
            50,
            0,
            0,
            0});
            this.NUM_minalgl.ValueChanged += new System.EventHandler(this.AltParams_ValueChanged);
            // 
            // lbl_unit1
            // 
            this.lbl_unit1.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lbl_unit1.AutoSize = true;
            this.lbl_unit1.Location = new System.Drawing.Point(153, 5);
            this.lbl_unit1.Name = "lbl_unit1";
            this.lbl_unit1.Size = new System.Drawing.Size(15, 13);
            this.lbl_unit1.TabIndex = 2;
            this.lbl_unit1.Text = "m";
            // 
            // lbl_maxagl
            // 
            this.lbl_maxagl.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.lbl_maxagl.AutoSize = true;
            this.lbl_maxagl.Location = new System.Drawing.Point(28, 29);
            this.lbl_maxagl.Name = "lbl_maxagl";
            this.lbl_maxagl.Size = new System.Drawing.Size(54, 13);
            this.lbl_maxagl.TabIndex = 3;
            this.lbl_maxagl.Text = "Max AGL:";
            // 
            // NUM_maxagl
            // 
            this.NUM_maxagl.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.NUM_maxagl.Location = new System.Drawing.Point(88, 27);
            this.NUM_maxagl.Maximum = new decimal(new int[] {
            5000,
            0,
            0,
            0});
            this.NUM_maxagl.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.NUM_maxagl.Name = "NUM_maxagl";
            this.NUM_maxagl.Size = new System.Drawing.Size(59, 20);
            this.NUM_maxagl.TabIndex = 4;
            this.NUM_maxagl.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.NUM_maxagl.Value = new decimal(new int[] {
            120,
            0,
            0,
            0});
            this.NUM_maxagl.ValueChanged += new System.EventHandler(this.AltParams_ValueChanged);
            // 
            // lbl_unit2
            // 
            this.lbl_unit2.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lbl_unit2.AutoSize = true;
            this.lbl_unit2.Location = new System.Drawing.Point(153, 29);
            this.lbl_unit2.Name = "lbl_unit2";
            this.lbl_unit2.Size = new System.Drawing.Size(15, 13);
            this.lbl_unit2.TabIndex = 5;
            this.lbl_unit2.Text = "m";
            // 
            // lbl_defagl
            // 
            this.lbl_defagl.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.lbl_defagl.AutoSize = true;
            this.lbl_defagl.Location = new System.Drawing.Point(14, 53);
            this.lbl_defagl.Name = "lbl_defagl";
            this.lbl_defagl.Size = new System.Drawing.Size(68, 13);
            this.lbl_defagl.TabIndex = 6;
            this.lbl_defagl.Text = "Default AGL:";
            // 
            // NUM_defagl
            // 
            this.NUM_defagl.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.NUM_defagl.Location = new System.Drawing.Point(88, 51);
            this.NUM_defagl.Maximum = new decimal(new int[] {
            5000,
            0,
            0,
            0});
            this.NUM_defagl.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.NUM_defagl.Name = "NUM_defagl";
            this.NUM_defagl.Size = new System.Drawing.Size(59, 20);
            this.NUM_defagl.TabIndex = 7;
            this.NUM_defagl.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.NUM_defagl.Value = new decimal(new int[] {
            80,
            0,
            0,
            0});
            this.NUM_defagl.ValueChanged += new System.EventHandler(this.AltParams_ValueChanged);
            // 
            // lbl_unit3
            // 
            this.lbl_unit3.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lbl_unit3.AutoSize = true;
            this.lbl_unit3.Location = new System.Drawing.Point(153, 53);
            this.lbl_unit3.Name = "lbl_unit3";
            this.lbl_unit3.Size = new System.Drawing.Size(15, 13);
            this.lbl_unit3.TabIndex = 8;
            this.lbl_unit3.Text = "m";
            // 
            // grp_speed
            // 
            this.grp_speed.Controls.Add(this.tbl_speed);
            this.grp_speed.Dock = System.Windows.Forms.DockStyle.Top;
            this.grp_speed.Location = new System.Drawing.Point(4, 460);
            this.grp_speed.Name = "grp_speed";
            this.grp_speed.Padding = new System.Windows.Forms.Padding(4, 2, 4, 4);
            this.grp_speed.Size = new System.Drawing.Size(273, 48);
            this.grp_speed.TabIndex = 2;
            this.grp_speed.TabStop = false;
            this.grp_speed.Text = "Speed";
            // 
            // tbl_speed
            // 
            this.tbl_speed.ColumnCount = 3;
            this.tbl_speed.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 85F));
            this.tbl_speed.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 65F));
            this.tbl_speed.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tbl_speed.Controls.Add(this.lbl_speed, 0, 0);
            this.tbl_speed.Controls.Add(this.NUM_speed, 1, 0);
            this.tbl_speed.Controls.Add(this.lbl_speed_unit, 2, 0);
            this.tbl_speed.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tbl_speed.Location = new System.Drawing.Point(4, 15);
            this.tbl_speed.Name = "tbl_speed";
            this.tbl_speed.RowCount = 1;
            this.tbl_speed.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tbl_speed.Size = new System.Drawing.Size(265, 29);
            this.tbl_speed.TabIndex = 0;
            // 
            // lbl_speed
            // 
            this.lbl_speed.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.lbl_speed.AutoSize = true;
            this.lbl_speed.Location = new System.Drawing.Point(41, 8);
            this.lbl_speed.Name = "lbl_speed";
            this.lbl_speed.Size = new System.Drawing.Size(41, 13);
            this.lbl_speed.TabIndex = 0;
            this.lbl_speed.Text = "Speed:";
            // 
            // NUM_speed
            // 
            this.NUM_speed.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.NUM_speed.Location = new System.Drawing.Point(88, 4);
            this.NUM_speed.Minimum = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.NUM_speed.Name = "NUM_speed";
            this.NUM_speed.Size = new System.Drawing.Size(59, 20);
            this.NUM_speed.TabIndex = 1;
            this.NUM_speed.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.NUM_speed.Value = new decimal(new int[] {
            25,
            0,
            0,
            0});
            this.NUM_speed.ValueChanged += new System.EventHandler(this.TurnParams_ValueChanged);
            // 
            // lbl_speed_unit
            // 
            this.lbl_speed_unit.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lbl_speed_unit.AutoSize = true;
            this.lbl_speed_unit.Location = new System.Drawing.Point(153, 8);
            this.lbl_speed_unit.Name = "lbl_speed_unit";
            this.lbl_speed_unit.Size = new System.Drawing.Size(25, 13);
            this.lbl_speed_unit.TabIndex = 2;
            this.lbl_speed_unit.Text = "m/s";
            // 
            // grp_corridor
            // 
            this.grp_corridor.Controls.Add(this.lbl_coverage);
            this.grp_corridor.Controls.Add(this.tbl_corridor);
            this.grp_corridor.Dock = System.Windows.Forms.DockStyle.Top;
            this.grp_corridor.Location = new System.Drawing.Point(4, 364);
            this.grp_corridor.Name = "grp_corridor";
            this.grp_corridor.Padding = new System.Windows.Forms.Padding(4, 2, 4, 4);
            this.grp_corridor.Size = new System.Drawing.Size(273, 96);
            this.grp_corridor.TabIndex = 3;
            this.grp_corridor.TabStop = false;
            this.grp_corridor.Text = "Pass Layout";
            // 
            // lbl_coverage
            // 
            this.lbl_coverage.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.5F);
            this.lbl_coverage.ForeColor = System.Drawing.Color.DimGray;
            this.lbl_coverage.Location = new System.Drawing.Point(4, 67);
            this.lbl_coverage.Name = "lbl_coverage";
            this.lbl_coverage.Size = new System.Drawing.Size(248, 20);
            this.lbl_coverage.TabIndex = 1;
            // 
            // tbl_corridor
            // 
            this.tbl_corridor.ColumnCount = 3;
            this.tbl_corridor.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 85F));
            this.tbl_corridor.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 65F));
            this.tbl_corridor.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tbl_corridor.Controls.Add(this.lbl_numpasses, 0, 0);
            this.tbl_corridor.Controls.Add(this.NUM_numpasses, 1, 0);
            this.tbl_corridor.Controls.Add(this.lbl_passes_unit, 2, 0);
            this.tbl_corridor.Controls.Add(this.lbl_passoffset, 0, 1);
            this.tbl_corridor.Controls.Add(this.NUM_passoffset, 1, 1);
            this.tbl_corridor.Controls.Add(this.lbl_offset_unit, 2, 1);
            this.tbl_corridor.Dock = System.Windows.Forms.DockStyle.Top;
            this.tbl_corridor.Location = new System.Drawing.Point(4, 15);
            this.tbl_corridor.Name = "tbl_corridor";
            this.tbl_corridor.RowCount = 2;
            this.tbl_corridor.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tbl_corridor.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tbl_corridor.Size = new System.Drawing.Size(265, 48);
            this.tbl_corridor.TabIndex = 0;
            // 
            // lbl_numpasses
            // 
            this.lbl_numpasses.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.lbl_numpasses.AutoSize = true;
            this.lbl_numpasses.Location = new System.Drawing.Point(38, 5);
            this.lbl_numpasses.Name = "lbl_numpasses";
            this.lbl_numpasses.Size = new System.Drawing.Size(44, 13);
            this.lbl_numpasses.TabIndex = 0;
            this.lbl_numpasses.Text = "Passes:";
            // 
            // NUM_numpasses
            // 
            this.NUM_numpasses.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.NUM_numpasses.Location = new System.Drawing.Point(88, 3);
            this.NUM_numpasses.Maximum = new decimal(new int[] {
            20,
            0,
            0,
            0});
            this.NUM_numpasses.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.NUM_numpasses.Name = "NUM_numpasses";
            this.NUM_numpasses.Size = new System.Drawing.Size(59, 20);
            this.NUM_numpasses.TabIndex = 1;
            this.NUM_numpasses.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.NUM_numpasses.Value = new decimal(new int[] {
            3,
            0,
            0,
            0});
            this.NUM_numpasses.ValueChanged += new System.EventHandler(this.CorridorParams_ValueChanged);
            // 
            // lbl_passes_unit
            // 
            this.lbl_passes_unit.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lbl_passes_unit.AutoSize = true;
            this.lbl_passes_unit.Location = new System.Drawing.Point(153, 5);
            this.lbl_passes_unit.Name = "lbl_passes_unit";
            this.lbl_passes_unit.Size = new System.Drawing.Size(28, 13);
            this.lbl_passes_unit.TabIndex = 2;
            this.lbl_passes_unit.Text = "lines";
            // 
            // lbl_passoffset
            // 
            this.lbl_passoffset.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.lbl_passoffset.AutoSize = true;
            this.lbl_passoffset.Location = new System.Drawing.Point(44, 29);
            this.lbl_passoffset.Name = "lbl_passoffset";
            this.lbl_passoffset.Size = new System.Drawing.Size(38, 13);
            this.lbl_passoffset.TabIndex = 3;
            this.lbl_passoffset.Text = "Offset:";
            // 
            // NUM_passoffset
            // 
            this.NUM_passoffset.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.NUM_passoffset.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.NUM_passoffset.Location = new System.Drawing.Point(88, 27);
            this.NUM_passoffset.Maximum = new decimal(new int[] {
            5000,
            0,
            0,
            0});
            this.NUM_passoffset.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.NUM_passoffset.Name = "NUM_passoffset";
            this.NUM_passoffset.Size = new System.Drawing.Size(59, 20);
            this.NUM_passoffset.TabIndex = 4;
            this.NUM_passoffset.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.NUM_passoffset.Value = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.NUM_passoffset.ValueChanged += new System.EventHandler(this.CorridorParams_ValueChanged);
            // 
            // lbl_offset_unit
            // 
            this.lbl_offset_unit.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lbl_offset_unit.AutoSize = true;
            this.lbl_offset_unit.Location = new System.Drawing.Point(153, 29);
            this.lbl_offset_unit.Name = "lbl_offset_unit";
            this.lbl_offset_unit.Size = new System.Drawing.Size(15, 13);
            this.lbl_offset_unit.TabIndex = 5;
            this.lbl_offset_unit.Text = "m";
            // 
            // grp_options
            //
            this.grp_options.Controls.Add(this.CHK_reverse);
            this.grp_options.Controls.Add(this.CHK_returnpath);
            this.grp_options.Dock = System.Windows.Forms.DockStyle.Top;
            this.grp_options.Location = new System.Drawing.Point(4, 320);
            this.grp_options.Name = "grp_options";
            this.grp_options.Padding = new System.Windows.Forms.Padding(6, 4, 4, 4);
            this.grp_options.Size = new System.Drawing.Size(273, 64);
            this.grp_options.TabIndex = 4;
            this.grp_options.TabStop = false;
            this.grp_options.Text = "Options";
            //
            // CHK_reverse
            //
            this.CHK_reverse.AutoSize = true;
            this.CHK_reverse.Location = new System.Drawing.Point(6, 18);
            this.CHK_reverse.Name = "CHK_reverse";
            this.CHK_reverse.Size = new System.Drawing.Size(111, 17);
            this.CHK_reverse.TabIndex = 0;
            this.CHK_reverse.Text = "Reverse Direction";
            //
            // CHK_returnpath
            //
            this.CHK_returnpath.AutoSize = true;
            this.CHK_returnpath.Location = new System.Drawing.Point(6, 41);
            this.CHK_returnpath.Name = "CHK_returnpath";
            this.CHK_returnpath.Size = new System.Drawing.Size(190, 17);
            this.CHK_returnpath.TabIndex = 1;
            this.CHK_returnpath.Text = "Insert DO_RETURN_PATH_START";
            //
            // grp_turns
            // 
            this.grp_turns.Controls.Add(this.lbl_turninfo);
            this.grp_turns.Controls.Add(this.tbl_turns);
            this.grp_turns.Dock = System.Windows.Forms.DockStyle.Top;
            this.grp_turns.Location = new System.Drawing.Point(4, 124);
            this.grp_turns.Name = "grp_turns";
            this.grp_turns.Padding = new System.Windows.Forms.Padding(4, 2, 4, 4);
            this.grp_turns.Size = new System.Drawing.Size(273, 196);
            this.grp_turns.TabIndex = 5;
            this.grp_turns.TabStop = false;
            this.grp_turns.Text = "Turn Settings";
            // 
            // lbl_turninfo
            // 
            this.lbl_turninfo.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.5F);
            this.lbl_turninfo.ForeColor = System.Drawing.Color.DimGray;
            this.lbl_turninfo.Location = new System.Drawing.Point(4, 138);
            this.lbl_turninfo.Name = "lbl_turninfo";
            this.lbl_turninfo.Size = new System.Drawing.Size(248, 36);
            this.lbl_turninfo.TabIndex = 1;
            // 
            // tbl_turns
            // 
            this.tbl_turns.ColumnCount = 3;
            this.tbl_turns.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 95F));
            this.tbl_turns.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 65F));
            this.tbl_turns.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tbl_turns.Controls.Add(this.lbl_low_thresh, 0, 0);
            this.tbl_turns.Controls.Add(this.NUM_low_thresh, 1, 0);
            this.tbl_turns.Controls.Add(this.lbl_deg_low, 2, 0);
            this.tbl_turns.Controls.Add(this.lbl_high_thresh, 0, 1);
            this.tbl_turns.Controls.Add(this.NUM_high_thresh, 1, 1);
            this.tbl_turns.Controls.Add(this.lbl_deg_high, 2, 1);
            this.tbl_turns.Controls.Add(this.lbl_extension, 0, 2);
            this.tbl_turns.Controls.Add(this.NUM_extension, 1, 2);
            this.tbl_turns.Controls.Add(this.lbl_ext_unit, 2, 2);
            this.tbl_turns.Controls.Add(this.lbl_turnradius, 0, 3);
            this.tbl_turns.Controls.Add(this.NUM_turnradius, 1, 3);
            this.tbl_turns.Controls.Add(this.lbl_tr_unit, 2, 3);
            this.tbl_turns.Controls.Add(this.lbl_cornerradius, 0, 4);
            this.tbl_turns.Controls.Add(this.NUM_cornerradius, 1, 4);
            this.tbl_turns.Controls.Add(this.lbl_cr_unit, 2, 4);
            this.tbl_turns.Dock = System.Windows.Forms.DockStyle.Top;
            this.tbl_turns.Location = new System.Drawing.Point(4, 15);
            this.tbl_turns.Name = "tbl_turns";
            this.tbl_turns.RowCount = 5;
            this.tbl_turns.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tbl_turns.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tbl_turns.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tbl_turns.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tbl_turns.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 24F));
            this.tbl_turns.Size = new System.Drawing.Size(265, 120);
            this.tbl_turns.TabIndex = 0;
            // 
            // lbl_low_thresh
            // 
            this.lbl_low_thresh.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.lbl_low_thresh.AutoSize = true;
            this.lbl_low_thresh.Location = new System.Drawing.Point(27, 5);
            this.lbl_low_thresh.Name = "lbl_low_thresh";
            this.lbl_low_thresh.Size = new System.Drawing.Size(65, 13);
            this.lbl_low_thresh.TabIndex = 0;
            this.lbl_low_thresh.Text = "Corner cut <";
            // 
            // NUM_low_thresh
            // 
            this.NUM_low_thresh.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.NUM_low_thresh.Location = new System.Drawing.Point(98, 3);
            this.NUM_low_thresh.Maximum = new decimal(new int[] {
            45,
            0,
            0,
            0});
            this.NUM_low_thresh.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.NUM_low_thresh.Name = "NUM_low_thresh";
            this.NUM_low_thresh.Size = new System.Drawing.Size(59, 20);
            this.NUM_low_thresh.TabIndex = 1;
            this.NUM_low_thresh.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.NUM_low_thresh.Value = new decimal(new int[] {
            15,
            0,
            0,
            0});
            this.NUM_low_thresh.ValueChanged += new System.EventHandler(this.TurnParams_ValueChanged);
            // 
            // lbl_deg_low
            // 
            this.lbl_deg_low.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lbl_deg_low.AutoSize = true;
            this.lbl_deg_low.Location = new System.Drawing.Point(163, 5);
            this.lbl_deg_low.Name = "lbl_deg_low";
            this.lbl_deg_low.Size = new System.Drawing.Size(11, 13);
            this.lbl_deg_low.TabIndex = 2;
            this.lbl_deg_low.Text = "°";
            // 
            // lbl_high_thresh
            // 
            this.lbl_high_thresh.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.lbl_high_thresh.AutoSize = true;
            this.lbl_high_thresh.Location = new System.Drawing.Point(22, 29);
            this.lbl_high_thresh.Name = "lbl_high_thresh";
            this.lbl_high_thresh.Size = new System.Drawing.Size(70, 13);
            this.lbl_high_thresh.TabIndex = 3;
            this.lbl_high_thresh.Text = "Dubins turn ≥";
            // 
            // NUM_high_thresh
            // 
            this.NUM_high_thresh.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.NUM_high_thresh.Location = new System.Drawing.Point(98, 27);
            this.NUM_high_thresh.Maximum = new decimal(new int[] {
            90,
            0,
            0,
            0});
            this.NUM_high_thresh.Minimum = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.NUM_high_thresh.Name = "NUM_high_thresh";
            this.NUM_high_thresh.Size = new System.Drawing.Size(59, 20);
            this.NUM_high_thresh.TabIndex = 4;
            this.NUM_high_thresh.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.NUM_high_thresh.Value = new decimal(new int[] {
            60,
            0,
            0,
            0});
            this.NUM_high_thresh.ValueChanged += new System.EventHandler(this.TurnParams_ValueChanged);
            // 
            // lbl_deg_high
            // 
            this.lbl_deg_high.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lbl_deg_high.AutoSize = true;
            this.lbl_deg_high.Location = new System.Drawing.Point(163, 29);
            this.lbl_deg_high.Name = "lbl_deg_high";
            this.lbl_deg_high.Size = new System.Drawing.Size(11, 13);
            this.lbl_deg_high.TabIndex = 5;
            this.lbl_deg_high.Text = "°";
            // 
            // lbl_extension
            // 
            this.lbl_extension.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.lbl_extension.AutoSize = true;
            this.lbl_extension.Location = new System.Drawing.Point(30, 53);
            this.lbl_extension.Name = "lbl_extension";
            this.lbl_extension.Size = new System.Drawing.Size(62, 13);
            this.lbl_extension.TabIndex = 6;
            this.lbl_extension.Text = "Overfly dist:";
            // 
            // NUM_extension
            // 
            this.NUM_extension.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.NUM_extension.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.NUM_extension.Location = new System.Drawing.Point(98, 51);
            this.NUM_extension.Maximum = new decimal(new int[] {
            2000,
            0,
            0,
            0});
            this.NUM_extension.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.NUM_extension.Name = "NUM_extension";
            this.NUM_extension.Size = new System.Drawing.Size(59, 20);
            this.NUM_extension.TabIndex = 7;
            this.NUM_extension.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.NUM_extension.Value = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.NUM_extension.ValueChanged += new System.EventHandler(this.TurnParams_ValueChanged);
            // 
            // lbl_ext_unit
            // 
            this.lbl_ext_unit.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lbl_ext_unit.AutoSize = true;
            this.lbl_ext_unit.Location = new System.Drawing.Point(163, 53);
            this.lbl_ext_unit.Name = "lbl_ext_unit";
            this.lbl_ext_unit.Size = new System.Drawing.Size(15, 13);
            this.lbl_ext_unit.TabIndex = 8;
            this.lbl_ext_unit.Text = "m";
            // 
            // lbl_turnradius
            // 
            this.lbl_turnradius.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.lbl_turnradius.AutoSize = true;
            this.lbl_turnradius.Location = new System.Drawing.Point(23, 77);
            this.lbl_turnradius.Name = "lbl_turnradius";
            this.lbl_turnradius.Size = new System.Drawing.Size(69, 13);
            this.lbl_turnradius.TabIndex = 9;
            this.lbl_turnradius.Text = "S-turn radius:";
            // 
            // NUM_turnradius
            // 
            this.NUM_turnradius.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.NUM_turnradius.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.NUM_turnradius.Location = new System.Drawing.Point(98, 75);
            this.NUM_turnradius.Maximum = new decimal(new int[] {
            5000,
            0,
            0,
            0});
            this.NUM_turnradius.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.NUM_turnradius.Name = "NUM_turnradius";
            this.NUM_turnradius.Size = new System.Drawing.Size(59, 20);
            this.NUM_turnradius.TabIndex = 10;
            this.NUM_turnradius.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.NUM_turnradius.Value = new decimal(new int[] {
            300,
            0,
            0,
            0});
            this.NUM_turnradius.ValueChanged += new System.EventHandler(this.TurnParams_ValueChanged);
            // 
            // lbl_tr_unit
            // 
            this.lbl_tr_unit.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lbl_tr_unit.AutoSize = true;
            this.lbl_tr_unit.Location = new System.Drawing.Point(163, 77);
            this.lbl_tr_unit.Name = "lbl_tr_unit";
            this.lbl_tr_unit.Size = new System.Drawing.Size(15, 13);
            this.lbl_tr_unit.TabIndex = 11;
            this.lbl_tr_unit.Text = "m";
            // 
            // lbl_cornerradius
            // 
            this.lbl_cornerradius.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.lbl_cornerradius.AutoSize = true;
            this.lbl_cornerradius.Location = new System.Drawing.Point(20, 101);
            this.lbl_cornerradius.Name = "lbl_cornerradius";
            this.lbl_cornerradius.Size = new System.Drawing.Size(72, 13);
            this.lbl_cornerradius.TabIndex = 12;
            this.lbl_cornerradius.Text = "Corner radius:";
            // 
            // NUM_cornerradius
            // 
            this.NUM_cornerradius.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.NUM_cornerradius.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.NUM_cornerradius.Location = new System.Drawing.Point(98, 99);
            this.NUM_cornerradius.Maximum = new decimal(new int[] {
            5000,
            0,
            0,
            0});
            this.NUM_cornerradius.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.NUM_cornerradius.Name = "NUM_cornerradius";
            this.NUM_cornerradius.Size = new System.Drawing.Size(59, 20);
            this.NUM_cornerradius.TabIndex = 13;
            this.NUM_cornerradius.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.NUM_cornerradius.Value = new decimal(new int[] {
            150,
            0,
            0,
            0});
            this.NUM_cornerradius.ValueChanged += new System.EventHandler(this.TurnParams_ValueChanged);
            // 
            // lbl_cr_unit
            // 
            this.lbl_cr_unit.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lbl_cr_unit.AutoSize = true;
            this.lbl_cr_unit.Location = new System.Drawing.Point(163, 101);
            this.lbl_cr_unit.Name = "lbl_cr_unit";
            this.lbl_cr_unit.Size = new System.Drawing.Size(15, 13);
            this.lbl_cr_unit.TabIndex = 14;
            this.lbl_cr_unit.Text = "m";
            // 
            // grp_stats
            // 
            this.grp_stats.Controls.Add(this.lbl_stats);
            this.grp_stats.Dock = System.Windows.Forms.DockStyle.Top;
            this.grp_stats.Location = new System.Drawing.Point(4, 39);
            this.grp_stats.Name = "grp_stats";
            this.grp_stats.Padding = new System.Windows.Forms.Padding(6, 4, 4, 4);
            this.grp_stats.Size = new System.Drawing.Size(273, 85);
            this.grp_stats.TabIndex = 6;
            this.grp_stats.TabStop = false;
            this.grp_stats.Text = "Statistics";
            // 
            // lbl_stats
            // 
            this.lbl_stats.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.5F);
            this.lbl_stats.ForeColor = System.Drawing.Color.DimGray;
            this.lbl_stats.Location = new System.Drawing.Point(6, 18);
            this.lbl_stats.Name = "lbl_stats";
            this.lbl_stats.Size = new System.Drawing.Size(270, 58);
            this.lbl_stats.TabIndex = 0;
            this.lbl_stats.Text = "(generate mission to see stats)";
            // 
            // pnl_buttons
            // 
            this.pnl_buttons.Controls.Add(this.BUT_generate);
            this.pnl_buttons.Controls.Add(this.BUT_accept);
            this.pnl_buttons.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnl_buttons.Location = new System.Drawing.Point(4, 4);
            this.pnl_buttons.Name = "pnl_buttons";
            this.pnl_buttons.Padding = new System.Windows.Forms.Padding(4, 4, 4, 0);
            this.pnl_buttons.Size = new System.Drawing.Size(273, 35);
            this.pnl_buttons.TabIndex = 7;
            // 
            // BUT_generate
            // 
            this.BUT_generate.Location = new System.Drawing.Point(4, 4);
            this.BUT_generate.Name = "BUT_generate";
            this.BUT_generate.Size = new System.Drawing.Size(80, 26);
            this.BUT_generate.TabIndex = 0;
            this.BUT_generate.Text = "Generate";
            this.BUT_generate.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.BUT_generate.Click += new System.EventHandler(this.BUT_generate_Click);
            // 
            // BUT_accept
            // 
            this.BUT_accept.Enabled = false;
            this.BUT_accept.Location = new System.Drawing.Point(90, 4);
            this.BUT_accept.Name = "BUT_accept";
            this.BUT_accept.Size = new System.Drawing.Size(80, 26);
            this.BUT_accept.TabIndex = 1;
            this.BUT_accept.Text = "Accept";
            this.BUT_accept.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.BUT_accept.Click += new System.EventHandler(this.BUT_accept_Click);
            // 
            // pnl_elevation
            // 
            this.pnl_elevation.Controls.Add(this.elev_profile);
            this.pnl_elevation.Controls.Add(this.lbl_elev_title);
            this.pnl_elevation.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnl_elevation.Location = new System.Drawing.Point(0, 520);
            this.pnl_elevation.MinimumSize = new System.Drawing.Size(0, 80);
            this.pnl_elevation.Name = "pnl_elevation";
            this.pnl_elevation.Size = new System.Drawing.Size(905, 230);
            this.pnl_elevation.TabIndex = 1;
            // 
            // elev_profile
            // 
            this.elev_profile.AltMultiplier = 1D;
            this.elev_profile.AltUnit = "m";
            this.elev_profile.DistMultiplier = 1D;
            this.elev_profile.DistUnit = "m";
            this.elev_profile.Dock = System.Windows.Forms.DockStyle.Fill;
            this.elev_profile.Location = new System.Drawing.Point(0, 18);
            this.elev_profile.Name = "elev_profile";
            this.elev_profile.Size = new System.Drawing.Size(905, 212);
            this.elev_profile.TabIndex = 0;
            // 
            // lbl_elev_title
            // 
            this.lbl_elev_title.Dock = System.Windows.Forms.DockStyle.Top;
            this.lbl_elev_title.Font = new System.Drawing.Font("Microsoft Sans Serif", 7.5F, System.Drawing.FontStyle.Bold);
            this.lbl_elev_title.Location = new System.Drawing.Point(0, 0);
            this.lbl_elev_title.Name = "lbl_elev_title";
            this.lbl_elev_title.Padding = new System.Windows.Forms.Padding(4, 0, 0, 0);
            this.lbl_elev_title.Size = new System.Drawing.Size(905, 18);
            this.lbl_elev_title.TabIndex = 1;
            this.lbl_elev_title.Text = "Elevation Profile (AGL)  — hover/drag dots & bars to adjust  |  scroll=zoom Y  Ct" +
    "rl+scroll=zoom X  drag=pan  dbl-click=reset";
            this.lbl_elev_title.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // CorridorPlanForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1200, 750);
            this.Controls.Add(this.map);
            this.Controls.Add(this.splitter_bottom);
            this.Controls.Add(this.pnl_elevation);
            this.Controls.Add(this.tabControl1);
            this.MinimumSize = new System.Drawing.Size(900, 600);
            this.Name = "CorridorPlanForm";
            this.Text = "Corridor Survey Planner";
            this.Load += new System.EventHandler(this.CorridorPlanForm_Load);
            this.tabControl1.ResumeLayout(false);
            this.tabOptions.ResumeLayout(false);
            this.pnl_scroll.ResumeLayout(false);
            this.grp_mainline.ResumeLayout(false);
            this.grp_branches.ResumeLayout(false);
            this.grp_altitude.ResumeLayout(false);
            this.tbl_altitude.ResumeLayout(false);
            this.tbl_altitude.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_minalgl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_maxagl)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_defagl)).EndInit();
            this.grp_speed.ResumeLayout(false);
            this.tbl_speed.ResumeLayout(false);
            this.tbl_speed.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_speed)).EndInit();
            this.grp_corridor.ResumeLayout(false);
            this.tbl_corridor.ResumeLayout(false);
            this.tbl_corridor.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_numpasses)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_passoffset)).EndInit();
            this.grp_options.ResumeLayout(false);
            this.grp_options.PerformLayout();
            this.grp_turns.ResumeLayout(false);
            this.tbl_turns.ResumeLayout(false);
            this.tbl_turns.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_low_thresh)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_high_thresh)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_extension)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_turnradius)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_cornerradius)).EndInit();
            this.grp_stats.ResumeLayout(false);
            this.pnl_buttons.ResumeLayout(false);
            this.pnl_elevation.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private MissionPlanner.Controls.myGMAP map;
        private System.Windows.Forms.Splitter splitter_bottom;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabOptions;
        private System.Windows.Forms.Panel pnl_scroll;
        private System.Windows.Forms.GroupBox grp_mainline;
        private System.Windows.Forms.ListBox LST_mainline;
        private MissionPlanner.Controls.MyButton BUT_mainline_add;
        private MissionPlanner.Controls.MyButton BUT_mainline_remove;
        private System.Windows.Forms.GroupBox grp_branches;
        private System.Windows.Forms.ListBox LST_branches;
        private MissionPlanner.Controls.MyButton BUT_branches_add;
        private MissionPlanner.Controls.MyButton BUT_branches_remove;
        private System.Windows.Forms.GroupBox grp_altitude;
        private System.Windows.Forms.TableLayoutPanel tbl_altitude;
        private System.Windows.Forms.Label lbl_minalgl;
        private System.Windows.Forms.NumericUpDown NUM_minalgl;
        private System.Windows.Forms.Label lbl_unit1;
        private System.Windows.Forms.Label lbl_maxagl;
        private System.Windows.Forms.NumericUpDown NUM_maxagl;
        private System.Windows.Forms.Label lbl_unit2;
        private System.Windows.Forms.Label lbl_defagl;
        private System.Windows.Forms.NumericUpDown NUM_defagl;
        private System.Windows.Forms.Label lbl_unit3;
        private System.Windows.Forms.GroupBox grp_speed;
        private System.Windows.Forms.TableLayoutPanel tbl_speed;
        private System.Windows.Forms.Label lbl_speed;
        private System.Windows.Forms.NumericUpDown NUM_speed;
        private System.Windows.Forms.Label lbl_speed_unit;
        private System.Windows.Forms.GroupBox grp_corridor;
        private System.Windows.Forms.TableLayoutPanel tbl_corridor;
        private System.Windows.Forms.Label lbl_numpasses;
        private System.Windows.Forms.NumericUpDown NUM_numpasses;
        private System.Windows.Forms.Label lbl_passes_unit;
        private System.Windows.Forms.Label lbl_passoffset;
        private System.Windows.Forms.NumericUpDown NUM_passoffset;
        private System.Windows.Forms.Label lbl_offset_unit;
        private System.Windows.Forms.Label lbl_coverage;
        private System.Windows.Forms.GroupBox grp_options;
        private System.Windows.Forms.CheckBox CHK_reverse;
        private System.Windows.Forms.CheckBox CHK_returnpath;
        private System.Windows.Forms.GroupBox grp_turns;
        private System.Windows.Forms.TableLayoutPanel tbl_turns;
        private System.Windows.Forms.Label lbl_low_thresh;
        private System.Windows.Forms.NumericUpDown NUM_low_thresh;
        private System.Windows.Forms.Label lbl_deg_low;
        private System.Windows.Forms.Label lbl_high_thresh;
        private System.Windows.Forms.NumericUpDown NUM_high_thresh;
        private System.Windows.Forms.Label lbl_deg_high;
        private System.Windows.Forms.Label lbl_extension;
        private System.Windows.Forms.NumericUpDown NUM_extension;
        private System.Windows.Forms.Label lbl_ext_unit;
        private System.Windows.Forms.Label lbl_turnradius;
        private System.Windows.Forms.NumericUpDown NUM_turnradius;
        private System.Windows.Forms.Label lbl_tr_unit;
        private System.Windows.Forms.Label lbl_cornerradius;
        private System.Windows.Forms.NumericUpDown NUM_cornerradius;
        private System.Windows.Forms.Label lbl_cr_unit;
        private System.Windows.Forms.Label lbl_turninfo;
        private System.Windows.Forms.GroupBox grp_stats;
        private System.Windows.Forms.Label lbl_stats;
        private System.Windows.Forms.Panel pnl_buttons;
        private MissionPlanner.Controls.MyButton BUT_generate;
        private MissionPlanner.Controls.MyButton BUT_accept;
        private System.Windows.Forms.Panel pnl_elevation;
        private System.Windows.Forms.Label lbl_elev_title;
        private Carbonix.UI.ElevationProfileControl elev_profile;
    }
}
