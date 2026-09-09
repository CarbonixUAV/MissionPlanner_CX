namespace Carbonix.UI
{
    partial class EFBTab
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
            this.GRP_destination = new System.Windows.Forms.GroupBox();
            this.TBL_destination = new System.Windows.Forms.TableLayoutPanel();
            this.LBL_address = new System.Windows.Forms.Label();
            this.TXT_destination = new System.Windows.Forms.TextBox();
            this.LED_liveness = new Bulb.LedBulb();
            this.LBL_liveness = new System.Windows.Forms.Label();
            this.GRP_identity = new System.Windows.Forms.GroupBox();
            this.TBL_identity = new System.Windows.Forms.TableLayoutPanel();
            this.LBL_icao = new System.Windows.Forms.Label();
            this.CMB_icao = new System.Windows.Forms.ComboBox();
            this.LBL_callsign = new System.Windows.Forms.Label();
            this.CMB_callsign = new System.Windows.Forms.ComboBox();
            this.LBL_callsign_out = new System.Windows.Forms.Label();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.BUT_start = new MissionPlanner.Controls.MyButton();
            this.LBL_status = new System.Windows.Forms.Label();
            this.CHK_sitl_ack = new System.Windows.Forms.CheckBox();
            this._timer = new System.Windows.Forms.Timer(this.components);
            this.GRP_destination.SuspendLayout();
            this.TBL_destination.SuspendLayout();
            this.GRP_identity.SuspendLayout();
            this.TBL_identity.SuspendLayout();
            this.tableLayoutPanel1.SuspendLayout();
            this.SuspendLayout();
            // 
            // GRP_destination
            // 
            this.GRP_destination.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.GRP_destination.Controls.Add(this.TBL_destination);
            this.GRP_destination.Location = new System.Drawing.Point(3, 3);
            this.GRP_destination.Name = "GRP_destination";
            this.GRP_destination.Size = new System.Drawing.Size(274, 74);
            this.GRP_destination.TabIndex = 0;
            this.GRP_destination.TabStop = false;
            this.GRP_destination.Text = "Destination";
            // 
            // TBL_destination
            // 
            this.TBL_destination.ColumnCount = 3;
            this.TBL_destination.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            this.TBL_destination.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.TBL_destination.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.TBL_destination.Controls.Add(this.LBL_address, 0, 0);
            this.TBL_destination.Controls.Add(this.TXT_destination, 1, 0);
            this.TBL_destination.Controls.Add(this.LED_liveness, 2, 0);
            this.TBL_destination.Controls.Add(this.LBL_liveness, 1, 1);
            this.TBL_destination.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TBL_destination.Location = new System.Drawing.Point(3, 16);
            this.TBL_destination.Name = "TBL_destination";
            this.TBL_destination.RowCount = 2;
            this.TBL_destination.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.TBL_destination.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.TBL_destination.Size = new System.Drawing.Size(268, 55);
            this.TBL_destination.TabIndex = 0;
            // 
            // LBL_address
            // 
            this.LBL_address.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.LBL_address.AutoSize = true;
            this.LBL_address.Location = new System.Drawing.Point(3, 6);
            this.LBL_address.Name = "LBL_address";
            this.LBL_address.Size = new System.Drawing.Size(45, 13);
            this.LBL_address.TabIndex = 0;
            this.LBL_address.Text = "Address";
            // 
            // TXT_destination
            // 
            this.TXT_destination.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.TXT_destination.Location = new System.Drawing.Point(63, 3);
            this.TXT_destination.Name = "TXT_destination";
            this.TXT_destination.Size = new System.Drawing.Size(184, 20);
            this.TXT_destination.TabIndex = 2;
            this.TXT_destination.TextChanged += new System.EventHandler(this.TXT_destination_TextChanged);
            // 
            // LED_liveness
            // 
            this.LED_liveness.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.LED_liveness.Location = new System.Drawing.Point(253, 3);
            this.LED_liveness.Name = "LED_liveness";
            this.LED_liveness.On = false;
            this.LED_liveness.Size = new System.Drawing.Size(12, 12);
            this.LED_liveness.TabIndex = 2;
            this.LED_liveness.TabStop = false;
            this.LED_liveness.Text = "Alive";
            // 
            // LBL_liveness
            // 
            this.LBL_liveness.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.LBL_liveness.AutoEllipsis = true;
            this.TBL_destination.SetColumnSpan(this.LBL_liveness, 2);
            this.LBL_liveness.Location = new System.Drawing.Point(63, 26);
            this.LBL_liveness.Name = "LBL_liveness";
            this.LBL_liveness.Size = new System.Drawing.Size(202, 29);
            this.LBL_liveness.TabIndex = 3;
            // 
            // GRP_identity
            // 
            this.GRP_identity.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.GRP_identity.Controls.Add(this.TBL_identity);
            this.GRP_identity.Location = new System.Drawing.Point(3, 83);
            this.GRP_identity.Name = "GRP_identity";
            this.GRP_identity.Size = new System.Drawing.Size(274, 75);
            this.GRP_identity.TabIndex = 1;
            this.GRP_identity.TabStop = false;
            this.GRP_identity.Text = "Identity";
            // 
            // TBL_identity
            // 
            this.TBL_identity.ColumnCount = 3;
            this.TBL_identity.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            this.TBL_identity.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.TBL_identity.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.TBL_identity.Controls.Add(this.LBL_icao, 0, 0);
            this.TBL_identity.Controls.Add(this.CMB_icao, 1, 0);
            this.TBL_identity.Controls.Add(this.LBL_callsign, 0, 1);
            this.TBL_identity.Controls.Add(this.CMB_callsign, 1, 1);
            this.TBL_identity.Controls.Add(this.LBL_callsign_out, 2, 1);
            this.TBL_identity.Dock = System.Windows.Forms.DockStyle.Fill;
            this.TBL_identity.Location = new System.Drawing.Point(3, 16);
            this.TBL_identity.Name = "TBL_identity";
            this.TBL_identity.RowCount = 2;
            this.TBL_identity.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.TBL_identity.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.TBL_identity.Size = new System.Drawing.Size(268, 56);
            this.TBL_identity.TabIndex = 1;
            // 
            // LBL_icao
            // 
            this.LBL_icao.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.LBL_icao.AutoSize = true;
            this.LBL_icao.Location = new System.Drawing.Point(3, 7);
            this.LBL_icao.Name = "LBL_icao";
            this.LBL_icao.Size = new System.Drawing.Size(32, 13);
            this.LBL_icao.TabIndex = 0;
            this.LBL_icao.Text = "ICAO";
            // 
            // CMB_icao
            // 
            this.CMB_icao.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.CMB_icao.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.CMB_icao.FormattingEnabled = true;
            this.CMB_icao.Location = new System.Drawing.Point(63, 3);
            this.CMB_icao.Name = "CMB_icao";
            this.CMB_icao.Size = new System.Drawing.Size(124, 21);
            this.CMB_icao.TabIndex = 5;
            this.CMB_icao.SelectedIndexChanged += new System.EventHandler(this.CMB_icao_SelectedIndexChanged);
            // 
            // LBL_callsign
            // 
            this.LBL_callsign.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.LBL_callsign.AutoSize = true;
            this.LBL_callsign.Location = new System.Drawing.Point(3, 35);
            this.LBL_callsign.Name = "LBL_callsign";
            this.LBL_callsign.Size = new System.Drawing.Size(43, 13);
            this.LBL_callsign.TabIndex = 4;
            this.LBL_callsign.Text = "Callsign";
            // 
            // CMB_callsign
            // 
            this.CMB_callsign.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            this.CMB_callsign.FormattingEnabled = true;
            this.CMB_callsign.Location = new System.Drawing.Point(63, 31);
            this.CMB_callsign.Name = "CMB_callsign";
            this.CMB_callsign.Size = new System.Drawing.Size(124, 21);
            this.CMB_callsign.TabIndex = 6;
            this.CMB_callsign.TextChanged += new System.EventHandler(this.CMB_callsign_TextChanged);
            this.CMB_callsign.Leave += new System.EventHandler(this.CMB_callsign_Leave);
            // 
            // LBL_callsign_out
            // 
            this.LBL_callsign_out.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.LBL_callsign_out.Location = new System.Drawing.Point(193, 35);
            this.LBL_callsign_out.Name = "LBL_callsign_out";
            this.LBL_callsign_out.Size = new System.Drawing.Size(72, 13);
            this.LBL_callsign_out.TabIndex = 7;
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.AutoSize = true;
            this.tableLayoutPanel1.ColumnCount = 1;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.Controls.Add(this.GRP_destination, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.GRP_identity, 0, 1);
            this.tableLayoutPanel1.Controls.Add(this.BUT_start, 0, 2);
            this.tableLayoutPanel1.Controls.Add(this.LBL_status, 0, 3);
            this.tableLayoutPanel1.Controls.Add(this.CHK_sitl_ack, 0, 4);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Top;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanel1.MaximumSize = new System.Drawing.Size(350, 0);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 5;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 32F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 42F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.tableLayoutPanel1.Size = new System.Drawing.Size(280, 258);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // BUT_start
            // 
            this.BUT_start.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.BUT_start.Location = new System.Drawing.Point(3, 164);
            this.BUT_start.Name = "BUT_start";
            this.BUT_start.Size = new System.Drawing.Size(274, 26);
            this.BUT_start.TabIndex = 2;
            this.BUT_start.Text = "Start";
            this.BUT_start.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.BUT_start.UseVisualStyleBackColor = true;
            this.BUT_start.Click += new System.EventHandler(this.BUT_start_Click);
            // 
            // LBL_status
            // 
            this.LBL_status.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.LBL_status.Location = new System.Drawing.Point(3, 193);
            this.LBL_status.Name = "LBL_status";
            this.LBL_status.Size = new System.Drawing.Size(274, 42);
            this.LBL_status.TabIndex = 3;
            // 
            // CHK_sitl_ack
            // 
            this.CHK_sitl_ack.AutoSize = true;
            this.CHK_sitl_ack.Location = new System.Drawing.Point(3, 238);
            this.CHK_sitl_ack.Name = "CHK_sitl_ack";
            this.CHK_sitl_ack.Size = new System.Drawing.Size(158, 17);
            this.CHK_sitl_ack.TabIndex = 4;
            this.CHK_sitl_ack.Text = "I have disabled AvPlan Live";
            this.CHK_sitl_ack.UseVisualStyleBackColor = true;
            this.CHK_sitl_ack.Visible = false;
            this.CHK_sitl_ack.CheckedChanged += new System.EventHandler(this.CHK_sitl_ack_CheckedChanged);
            // 
            // _timer
            // 
            this._timer.Interval = 1000;
            this._timer.Tick += new System.EventHandler(this._timer_Tick);
            // 
            // EFBTab
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.Controls.Add(this.tableLayoutPanel1);
            this.Name = "EFBTab";
            this.Size = new System.Drawing.Size(280, 340);
            this.GRP_destination.ResumeLayout(false);
            this.TBL_destination.ResumeLayout(false);
            this.TBL_destination.PerformLayout();
            this.GRP_identity.ResumeLayout(false);
            this.TBL_identity.ResumeLayout(false);
            this.TBL_identity.PerformLayout();
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox GRP_destination;
        private System.Windows.Forms.GroupBox GRP_identity;
        private System.Windows.Forms.Label LBL_address;
        private System.Windows.Forms.Label LBL_liveness;
        private Bulb.LedBulb LED_liveness;
        private System.Windows.Forms.TableLayoutPanel TBL_destination;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.TextBox TXT_destination;
        private System.Windows.Forms.TableLayoutPanel TBL_identity;
        private System.Windows.Forms.Label LBL_icao;
        private System.Windows.Forms.Label LBL_callsign;
        private System.Windows.Forms.ComboBox CMB_icao;
        private System.Windows.Forms.ComboBox CMB_callsign;
        private System.Windows.Forms.Label LBL_callsign_out;
        private MissionPlanner.Controls.MyButton BUT_start;
        private System.Windows.Forms.Label LBL_status;
        private System.Windows.Forms.Timer _timer;
        private System.Windows.Forms.CheckBox CHK_sitl_ack;
    }
}
