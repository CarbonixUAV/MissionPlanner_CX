namespace Carbonix
{
    partial class ActionsControl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.CHK_loitdirection = new System.Windows.Forms.CheckBox();
            this.BUT_rtl = new MissionPlanner.Controls.MyButton();
            this.BUT_airspeed = new MissionPlanner.Controls.MyButton();
            this.BUT_loitradius = new MissionPlanner.Controls.MyButton();
            this.BUT_setwp = new MissionPlanner.Controls.MyButton();
            this.BUT_qloiter = new MissionPlanner.Controls.MyButton();
            this.BUT_guidedalt = new MissionPlanner.Controls.MyButton();
            this.BUT_auto = new MissionPlanner.Controls.MyButton();
            this.NUM_airspeed = new System.Windows.Forms.NumericUpDown();
            this.NUM_loitradius = new System.Windows.Forms.NumericUpDown();
            this.NUM_guidedalt = new System.Windows.Forms.NumericUpDown();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.LBL_loitradiusunits = new System.Windows.Forms.Label();
            this.LBL_airspeedunits = new System.Windows.Forms.Label();
            this.CMB_altframe = new System.Windows.Forms.ComboBox();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_airspeed)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_loitradius)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_guidedalt)).BeginInit();
            this.tableLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // CHK_loitdirection
            // 
            this.CHK_loitdirection.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.CHK_loitdirection.AutoSize = true;
            this.CHK_loitdirection.Location = new System.Drawing.Point(156, 30);
            this.CHK_loitdirection.Margin = new System.Windows.Forms.Padding(0);
            this.CHK_loitdirection.Name = "CHK_loitdirection";
            this.CHK_loitdirection.Padding = new System.Windows.Forms.Padding(3, 0, 0, 0);
            this.CHK_loitdirection.Size = new System.Drawing.Size(19, 30);
            this.CHK_loitdirection.TabIndex = 89;
            this.toolTip1.SetToolTip(this.CHK_loitdirection, "Check for CCW orbit");
            this.CHK_loitdirection.UseVisualStyleBackColor = false;
            this.CHK_loitdirection.CheckedChanged += new System.EventHandler(this.CHK_loitdirection_CheckedChanged);
            // 
            // BUT_rtl
            // 
            this.BUT_rtl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.BUT_rtl.ColorMouseDown = System.Drawing.Color.Empty;
            this.BUT_rtl.ColorMouseOver = System.Drawing.Color.Empty;
            this.BUT_rtl.ColorNotEnabled = System.Drawing.Color.Empty;
            this.BUT_rtl.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.BUT_rtl.Location = new System.Drawing.Point(6, 62);
            this.BUT_rtl.Margin = new System.Windows.Forms.Padding(2);
            this.BUT_rtl.Name = "BUT_rtl";
            this.BUT_rtl.Size = new System.Drawing.Size(72, 26);
            this.BUT_rtl.TabIndex = 85;
            this.BUT_rtl.Text = "RTL";
            this.BUT_rtl.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.BUT_rtl, "Change mode to RTL");
            this.BUT_rtl.UseVisualStyleBackColor = true;
            this.BUT_rtl.Click += new System.EventHandler(this.BUT_mode_Click);
            // 
            // BUT_airspeed
            // 
            this.BUT_airspeed.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.BUT_airspeed.ColorMouseDown = System.Drawing.Color.Empty;
            this.BUT_airspeed.ColorMouseOver = System.Drawing.Color.Empty;
            this.BUT_airspeed.ColorNotEnabled = System.Drawing.Color.Empty;
            this.BUT_airspeed.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.BUT_airspeed.Location = new System.Drawing.Point(82, 62);
            this.BUT_airspeed.Margin = new System.Windows.Forms.Padding(2);
            this.BUT_airspeed.Name = "BUT_airspeed";
            this.BUT_airspeed.Size = new System.Drawing.Size(72, 26);
            this.BUT_airspeed.TabIndex = 83;
            this.BUT_airspeed.Text = "Set Airspeed";
            this.BUT_airspeed.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.BUT_airspeed, "Changes to the airspeed on the right");
            this.BUT_airspeed.UseVisualStyleBackColor = true;
            this.BUT_airspeed.Click += new System.EventHandler(this.BUT_airspeed_Click);
            // 
            // BUT_loitradius
            // 
            this.BUT_loitradius.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.BUT_loitradius.ColorMouseDown = System.Drawing.Color.Empty;
            this.BUT_loitradius.ColorMouseOver = System.Drawing.Color.Empty;
            this.BUT_loitradius.ColorNotEnabled = System.Drawing.Color.Empty;
            this.BUT_loitradius.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.BUT_loitradius.Location = new System.Drawing.Point(82, 32);
            this.BUT_loitradius.Margin = new System.Windows.Forms.Padding(2);
            this.BUT_loitradius.Name = "BUT_loitradius";
            this.BUT_loitradius.Size = new System.Drawing.Size(72, 26);
            this.BUT_loitradius.TabIndex = 78;
            this.BUT_loitradius.Text = "Set Loiter Radius";
            this.BUT_loitradius.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.BUT_loitradius, "Changes to the loiter radius on the right");
            this.BUT_loitradius.UseVisualStyleBackColor = true;
            this.BUT_loitradius.Click += new System.EventHandler(this.BUT_loitradius_Click);
            // 
            // BUT_setwp
            // 
            this.BUT_setwp.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.BUT_setwp.ColorMouseDown = System.Drawing.Color.Empty;
            this.BUT_setwp.ColorMouseOver = System.Drawing.Color.Empty;
            this.BUT_setwp.ColorNotEnabled = System.Drawing.Color.Empty;
            this.BUT_setwp.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.BUT_setwp.Location = new System.Drawing.Point(6, 92);
            this.BUT_setwp.Margin = new System.Windows.Forms.Padding(2);
            this.BUT_setwp.Name = "BUT_setwp";
            this.BUT_setwp.Size = new System.Drawing.Size(72, 26);
            this.BUT_setwp.TabIndex = 75;
            this.BUT_setwp.Text = "Set WP";
            this.BUT_setwp.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.BUT_setwp, "Changes the current target waypoint");
            this.BUT_setwp.UseVisualStyleBackColor = true;
            this.BUT_setwp.Click += new System.EventHandler(this.BUT_setwp_Click);
            // 
            // BUT_qloiter
            // 
            this.BUT_qloiter.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.BUT_qloiter.ColorMouseDown = System.Drawing.Color.Empty;
            this.BUT_qloiter.ColorMouseOver = System.Drawing.Color.Empty;
            this.BUT_qloiter.ColorNotEnabled = System.Drawing.Color.Empty;
            this.BUT_qloiter.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.BUT_qloiter.Location = new System.Drawing.Point(6, 32);
            this.BUT_qloiter.Margin = new System.Windows.Forms.Padding(2);
            this.BUT_qloiter.Name = "BUT_qloiter";
            this.BUT_qloiter.Size = new System.Drawing.Size(72, 26);
            this.BUT_qloiter.TabIndex = 76;
            this.BUT_qloiter.Text = "QLoiter";
            this.BUT_qloiter.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.BUT_qloiter, "Change mode to QLoiter");
            this.BUT_qloiter.UseVisualStyleBackColor = true;
            this.BUT_qloiter.Click += new System.EventHandler(this.BUT_mode_Click);
            // 
            // BUT_guidedalt
            // 
            this.BUT_guidedalt.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.BUT_guidedalt.ColorMouseDown = System.Drawing.Color.Empty;
            this.BUT_guidedalt.ColorMouseOver = System.Drawing.Color.Empty;
            this.BUT_guidedalt.ColorNotEnabled = System.Drawing.Color.Empty;
            this.BUT_guidedalt.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.BUT_guidedalt.Location = new System.Drawing.Point(82, 2);
            this.BUT_guidedalt.Margin = new System.Windows.Forms.Padding(2);
            this.BUT_guidedalt.Name = "BUT_guidedalt";
            this.BUT_guidedalt.Size = new System.Drawing.Size(72, 26);
            this.BUT_guidedalt.TabIndex = 73;
            this.BUT_guidedalt.Text = "Set Guided Altitude";
            this.BUT_guidedalt.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.BUT_guidedalt, "Sets the \"Fly to Here\" altitude");
            this.BUT_guidedalt.UseVisualStyleBackColor = true;
            this.BUT_guidedalt.Click += new System.EventHandler(this.BUT_guidedalt_Click);
            // 
            // BUT_auto
            // 
            this.BUT_auto.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.BUT_auto.ColorMouseDown = System.Drawing.Color.Empty;
            this.BUT_auto.ColorMouseOver = System.Drawing.Color.Empty;
            this.BUT_auto.ColorNotEnabled = System.Drawing.Color.Empty;
            this.BUT_auto.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.BUT_auto.Location = new System.Drawing.Point(6, 2);
            this.BUT_auto.Margin = new System.Windows.Forms.Padding(2);
            this.BUT_auto.Name = "BUT_auto";
            this.BUT_auto.Size = new System.Drawing.Size(72, 26);
            this.BUT_auto.TabIndex = 74;
            this.BUT_auto.Text = "Auto";
            this.BUT_auto.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.BUT_auto, "Change mode to Auto");
            this.BUT_auto.UseVisualStyleBackColor = true;
            this.BUT_auto.Click += new System.EventHandler(this.BUT_mode_Click);
            // 
            // NUM_airspeed
            // 
            this.NUM_airspeed.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.NUM_airspeed.Location = new System.Drawing.Point(177, 65);
            this.NUM_airspeed.Margin = new System.Windows.Forms.Padding(2);
            this.NUM_airspeed.Maximum = new decimal(new int[] {
            58,
            0,
            0,
            0});
            this.NUM_airspeed.Minimum = new decimal(new int[] {
            39,
            0,
            0,
            0});
            this.NUM_airspeed.Name = "NUM_airspeed";
            this.NUM_airspeed.Size = new System.Drawing.Size(56, 20);
            this.NUM_airspeed.TabIndex = 82;
            this.NUM_airspeed.Value = new decimal(new int[] {
            39,
            0,
            0,
            0});
            this.NUM_airspeed.ValueChanged += new System.EventHandler(this.NUM_ValueChanged);
            this.NUM_airspeed.KeyDown += new System.Windows.Forms.KeyEventHandler(this.NUM_KeyDown);
            // 
            // NUM_loitradius
            // 
            this.NUM_loitradius.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.NUM_loitradius.Increment = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.NUM_loitradius.Location = new System.Drawing.Point(177, 35);
            this.NUM_loitradius.Margin = new System.Windows.Forms.Padding(2);
            this.NUM_loitradius.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.NUM_loitradius.Minimum = new decimal(new int[] {
            500,
            0,
            0,
            0});
            this.NUM_loitradius.Name = "NUM_loitradius";
            this.NUM_loitradius.Size = new System.Drawing.Size(56, 20);
            this.NUM_loitradius.TabIndex = 79;
            this.NUM_loitradius.Value = new decimal(new int[] {
            500,
            0,
            0,
            0});
            this.NUM_loitradius.ValueChanged += new System.EventHandler(this.NUM_ValueChanged);
            this.NUM_loitradius.KeyDown += new System.Windows.Forms.KeyEventHandler(this.NUM_KeyDown);
            // 
            // NUM_guidedalt
            // 
            this.NUM_guidedalt.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.NUM_guidedalt.Increment = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.NUM_guidedalt.Location = new System.Drawing.Point(177, 5);
            this.NUM_guidedalt.Margin = new System.Windows.Forms.Padding(2);
            this.NUM_guidedalt.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.NUM_guidedalt.Minimum = new decimal(new int[] {
            500,
            0,
            0,
            0});
            this.NUM_guidedalt.Name = "NUM_guidedalt";
            this.NUM_guidedalt.Size = new System.Drawing.Size(56, 20);
            this.NUM_guidedalt.TabIndex = 91;
            this.NUM_guidedalt.Value = new decimal(new int[] {
            500,
            0,
            0,
            0});
            this.NUM_guidedalt.ValueChanged += new System.EventHandler(this.NUM_ValueChanged);
            this.NUM_guidedalt.KeyDown += new System.Windows.Forms.KeyEventHandler(this.NUM_KeyDown);
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.ColumnCount = 5;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 19F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            this.tableLayoutPanel1.Controls.Add(this.LBL_loitradiusunits, 4, 1);
            this.tableLayoutPanel1.Controls.Add(this.BUT_rtl, 0, 2);
            this.tableLayoutPanel1.Controls.Add(this.BUT_loitradius, 1, 1);
            this.tableLayoutPanel1.Controls.Add(this.BUT_setwp, 0, 3);
            this.tableLayoutPanel1.Controls.Add(this.BUT_qloiter, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.BUT_guidedalt, 1, 0);
            this.tableLayoutPanel1.Controls.Add(this.BUT_auto, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.NUM_loitradius, 3, 1);
            this.tableLayoutPanel1.Controls.Add(this.CHK_loitdirection, 2, 1);
            this.tableLayoutPanel1.Controls.Add(this.NUM_guidedalt, 3, 0);
            this.tableLayoutPanel1.Controls.Add(this.BUT_airspeed, 1, 2);
            this.tableLayoutPanel1.Controls.Add(this.NUM_airspeed, 3, 2);
            this.tableLayoutPanel1.Controls.Add(this.LBL_airspeedunits, 4, 2);
            this.tableLayoutPanel1.Controls.Add(this.CMB_altframe, 4, 0);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanel1.Margin = new System.Windows.Forms.Padding(2);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.Padding = new System.Windows.Forms.Padding(4, 0, 5, 0);
            this.tableLayoutPanel1.RowCount = 5;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 1F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(300, 122);
            this.tableLayoutPanel1.TabIndex = 79;
            // 
            // LBL_loitradiusunits
            // 
            this.LBL_loitradiusunits.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.LBL_loitradiusunits.Location = new System.Drawing.Point(237, 38);
            this.LBL_loitradiusunits.Margin = new System.Windows.Forms.Padding(2);
            this.LBL_loitradiusunits.Name = "LBL_loitradiusunits";
            this.LBL_loitradiusunits.Size = new System.Drawing.Size(56, 13);
            this.LBL_loitradiusunits.TabIndex = 88;
            this.LBL_loitradiusunits.Text = "ft";
            // 
            // LBL_airspeedunits
            // 
            this.LBL_airspeedunits.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.LBL_airspeedunits.Location = new System.Drawing.Point(237, 68);
            this.LBL_airspeedunits.Margin = new System.Windows.Forms.Padding(2);
            this.LBL_airspeedunits.Name = "LBL_airspeedunits";
            this.LBL_airspeedunits.Size = new System.Drawing.Size(56, 13);
            this.LBL_airspeedunits.TabIndex = 86;
            this.LBL_airspeedunits.Text = "kts";
            // 
            // CMB_altframe
            // 
            this.CMB_altframe.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.CMB_altframe.FormattingEnabled = true;
            this.CMB_altframe.Location = new System.Drawing.Point(238, 4);
            this.CMB_altframe.Margin = new System.Windows.Forms.Padding(3, 3, 0, 3);
            this.CMB_altframe.Name = "CMB_altframe";
            this.CMB_altframe.Size = new System.Drawing.Size(57, 21);
            this.CMB_altframe.TabIndex = 93;
            this.CMB_altframe.SelectedIndexChanged += new System.EventHandler(this.CMB_altframe_SelectedIndexChanged);
            this.CMB_altframe.KeyDown += new System.Windows.Forms.KeyEventHandler(this.CMB_KeyDown);
            // 
            // ActionsControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.tableLayoutPanel1);
            this.Name = "ActionsControl";
            this.Size = new System.Drawing.Size(300, 122);
            this.VisibleChanged += new System.EventHandler(this.ActionsControl_VisibleChanged);
            ((System.ComponentModel.ISupportInitialize)(this.NUM_airspeed)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_loitradius)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.NUM_guidedalt)).EndInit();
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private MissionPlanner.Controls.MyButton BUT_setwp;
        private MissionPlanner.Controls.MyButton BUT_qloiter;
        private MissionPlanner.Controls.MyButton BUT_auto;
        private MissionPlanner.Controls.MyButton BUT_guidedalt;
        private MissionPlanner.Controls.MyButton BUT_loitradius;
        private System.Windows.Forms.NumericUpDown NUM_loitradius;
        private MissionPlanner.Controls.MyButton BUT_airspeed;
        private System.Windows.Forms.NumericUpDown NUM_airspeed;
        private MissionPlanner.Controls.MyButton BUT_rtl;
        private System.Windows.Forms.Label LBL_loitradiusunits;
        private System.Windows.Forms.Label LBL_airspeedunits;
        private System.Windows.Forms.CheckBox CHK_loitdirection;
        private System.Windows.Forms.NumericUpDown NUM_guidedalt;
        private System.Windows.Forms.ComboBox CMB_altframe;
    }
}
