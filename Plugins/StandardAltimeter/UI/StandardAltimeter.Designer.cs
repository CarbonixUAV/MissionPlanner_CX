namespace StandardAltimeter
{
    partial class StandardAltimeter
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem inHgToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem hPaToolStripMenuItem;

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
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.lbl_msl = new System.Windows.Forms.Label();
            this.num_kollsman = new System.Windows.Forms.NumericUpDown();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.inHgToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.hPaToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.tableLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.num_kollsman)).BeginInit();
            this.SuspendLayout();
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.CellBorderStyle = System.Windows.Forms.TableLayoutPanelCellBorderStyle.Single;
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.tableLayoutPanel1.Controls.Add(this.lbl_msl, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.num_kollsman, 1, 0);
            this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tableLayoutPanel1.Location = new System.Drawing.Point(15, 10);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 1;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 47F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(195, 27);
            this.tableLayoutPanel1.TabIndex = 0;
            // 
            // lbl_msl
            // 
            this.lbl_msl.Anchor = System.Windows.Forms.AnchorStyles.Right;
            this.lbl_msl.AutoSize = true;
            this.lbl_msl.Font = new System.Drawing.Font("Microsoft Sans Serif", 14F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lbl_msl.Location = new System.Drawing.Point(16, 1);
            this.lbl_msl.Name = "lbl_msl";
            this.lbl_msl.Size = new System.Drawing.Size(116, 24);
            this.lbl_msl.TabIndex = 0;
            this.lbl_msl.Text = "10000 ft MSL";
            // 
            // num_kollsman
            // 
            this.num_kollsman.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.num_kollsman.DecimalPlaces = 2;
            this.num_kollsman.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.num_kollsman.Location = new System.Drawing.Point(139, 4);
            this.num_kollsman.Maximum = new decimal(new int[] {
            31,
            0,
            0,
            0});
            this.num_kollsman.Minimum = new decimal(new int[] {
            28,
            0,
            0,
            0});
            this.num_kollsman.Name = "num_kollsman";
            this.num_kollsman.Size = new System.Drawing.Size(52, 20);
            this.num_kollsman.TabIndex = 2;
            this.num_kollsman.Value = new decimal(new int[] {
            2992,
            0,
            0,
            131072});
            //
            // timer1
            //
            this.timer1.Interval = 5000;
            //
            // contextMenuStrip1
            //
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.inHgToolStripMenuItem,
            this.hPaToolStripMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(100, 48);
            this.contextMenuStrip1.Opening += new System.ComponentModel.CancelEventHandler(this.contextMenuStrip1_Opening);
            //
            // inHgToolStripMenuItem
            //
            this.inHgToolStripMenuItem.Name = "inHgToolStripMenuItem";
            this.inHgToolStripMenuItem.Size = new System.Drawing.Size(99, 22);
            this.inHgToolStripMenuItem.Text = "inHg";
            this.inHgToolStripMenuItem.Click += new System.EventHandler(this.inHgToolStripMenuItem_Click);
            //
            // hPaToolStripMenuItem
            //
            this.hPaToolStripMenuItem.Name = "hPaToolStripMenuItem";
            this.hPaToolStripMenuItem.Size = new System.Drawing.Size(99, 22);
            this.hPaToolStripMenuItem.Text = "hPa";
            this.hPaToolStripMenuItem.Click += new System.EventHandler(this.hPaToolStripMenuItem_Click);
            //
            // StandardAltimeter
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ContextMenuStrip = this.contextMenuStrip1;
            this.Controls.Add(this.tableLayoutPanel1);
            this.MinimumSize = new System.Drawing.Size(225, 47);
            this.Name = "StandardAltimeter";
            this.Padding = new System.Windows.Forms.Padding(15, 10, 15, 10);
            this.Size = new System.Drawing.Size(225, 47);
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.num_kollsman)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        public System.Windows.Forms.Label lbl_msl;
        public System.Windows.Forms.NumericUpDown num_kollsman;
        private System.Windows.Forms.Timer timer1;
    }
}
