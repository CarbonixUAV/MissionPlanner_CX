﻿using System.Drawing;
namespace Carbonix
{
    partial class EmergencyTab
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
            this.but_fenceenable = new MissionPlanner.Controls.MyButton();
            this.but_fencedisable = new MissionPlanner.Controls.MyButton();
            this.but_manual = new MissionPlanner.Controls.MyButton();
            this.but_fbwa = new MissionPlanner.Controls.MyButton();
            this.but_qaforce = new MissionPlanner.Controls.MyButton();
            this.but_qaenable = new MissionPlanner.Controls.MyButton();
            this.but_qadisable = new MissionPlanner.Controls.MyButton();
            this.but_asenable = new MissionPlanner.Controls.MyButton();
            this.but_asdisable = new MissionPlanner.Controls.MyButton();
            this.but_qstab = new MissionPlanner.Controls.MyButton();
            this.but_qhover = new MissionPlanner.Controls.MyButton();
            this.but_disarm = new MissionPlanner.Controls.MyButton();
            this.but_arm = new MissionPlanner.Controls.MyButton();
            this.tableLayoutPanelOuter = new System.Windows.Forms.TableLayoutPanel();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.txt_messagebox = new System.Windows.Forms.TextBox();
            this.grp_modes = new System.Windows.Forms.GroupBox();
            this.table_modes = new System.Windows.Forms.TableLayoutPanel();
            this.grp_qassist = new System.Windows.Forms.GroupBox();
            this.grp_airspeed = new System.Windows.Forms.GroupBox();
            this.grp_engine = new System.Windows.Forms.GroupBox();
            this.but_startEng = new MissionPlanner.Controls.MyButton();
            this.but_killEng = new MissionPlanner.Controls.MyButton();
            this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            this.tableLayoutPanel2 = new System.Windows.Forms.TableLayoutPanel();
            this.tableLayoutPanelOuter.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.grp_modes.SuspendLayout();
            this.table_modes.SuspendLayout();
            this.grp_qassist.SuspendLayout();
            this.grp_airspeed.SuspendLayout();
            this.grp_engine.SuspendLayout();
            this.tableLayoutPanel1.SuspendLayout();
            this.tableLayoutPanel2.SuspendLayout();
            this.SuspendLayout();
            // 
            // but_fenceenable
            // 
            this.but_fenceenable.Location = new System.Drawing.Point(87, 19);
            this.but_fenceenable.Name = "but_fenceenable";
            this.but_fenceenable.Size = new System.Drawing.Size(75, 23);
            this.but_fenceenable.TabIndex = 1;
            this.but_fenceenable.Text = "Enable";
            this.but_fenceenable.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_fenceenable, "Enable fences");
            this.but_fenceenable.UseVisualStyleBackColor = true;
            this.but_fenceenable.Click += new System.EventHandler(this.but_fenceenable_Click);
            // 
            // but_fencedisable
            // 
            this.but_fencedisable.Location = new System.Drawing.Point(6, 19);
            this.but_fencedisable.Name = "but_fencedisable";
            this.but_fencedisable.Size = new System.Drawing.Size(75, 23);
            this.but_fencedisable.TabIndex = 0;
            this.but_fencedisable.Text = "Disable";
            this.but_fencedisable.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_fencedisable, "Disable all fences");
            this.but_fencedisable.UseVisualStyleBackColor = true;
            this.but_fencedisable.Click += new System.EventHandler(this.but_fencedisable_Click);
            // 
            // but_manual
            // 
            this.but_manual.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.but_manual.Location = new System.Drawing.Point(219, 3);
            this.but_manual.Name = "but_manual";
            this.but_manual.Size = new System.Drawing.Size(66, 23);
            this.but_manual.TabIndex = 1;
            this.but_manual.Text = "Manual";
            this.but_manual.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_manual, "Set mode to Manual");
            this.but_manual.UseVisualStyleBackColor = true;
            this.but_manual.Click += new System.EventHandler(this.but_mode_Click);
            // 
            // but_fbwa
            // 
            this.but_fbwa.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.but_fbwa.Location = new System.Drawing.Point(147, 3);
            this.but_fbwa.Name = "but_fbwa";
            this.but_fbwa.Size = new System.Drawing.Size(66, 23);
            this.but_fbwa.TabIndex = 0;
            this.but_fbwa.Text = "FBWA";
            this.but_fbwa.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_fbwa, "Set mode to FBWA");
            this.but_fbwa.UseVisualStyleBackColor = true;
            this.but_fbwa.Click += new System.EventHandler(this.but_mode_Click);
            // 
            // but_qaforce
            // 
            this.but_qaforce.Location = new System.Drawing.Point(168, 19);
            this.but_qaforce.Name = "but_qaforce";
            this.but_qaforce.Size = new System.Drawing.Size(75, 23);
            this.but_qaforce.TabIndex = 2;
            this.but_qaforce.Text = "Force";
            this.but_qaforce.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_qaforce, "Force QASSIST to turn on now");
            this.but_qaforce.UseVisualStyleBackColor = true;
            this.but_qaforce.Click += new System.EventHandler(this.but_qaforce_Click);
            // 
            // but_qaenable
            // 
            this.but_qaenable.Location = new System.Drawing.Point(87, 19);
            this.but_qaenable.Name = "but_qaenable";
            this.but_qaenable.Size = new System.Drawing.Size(75, 23);
            this.but_qaenable.TabIndex = 1;
            this.but_qaenable.Text = "Enable";
            this.but_qaenable.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_qaenable, "Enable QASSIST");
            this.but_qaenable.UseVisualStyleBackColor = true;
            this.but_qaenable.Click += new System.EventHandler(this.but_qaenable_Click);
            // 
            // but_qadisable
            // 
            this.but_qadisable.Location = new System.Drawing.Point(6, 19);
            this.but_qadisable.Name = "but_qadisable";
            this.but_qadisable.Size = new System.Drawing.Size(75, 23);
            this.but_qadisable.TabIndex = 0;
            this.but_qadisable.Text = "Disable";
            this.but_qadisable.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_qadisable, "Disable QASSIST");
            this.but_qadisable.UseVisualStyleBackColor = true;
            this.but_qadisable.Click += new System.EventHandler(this.but_qadisable_Click);
            // 
            // but_asenable
            // 
            this.but_asenable.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.but_asenable.Location = new System.Drawing.Point(70, 3);
            this.but_asenable.Name = "but_asenable";
            this.but_asenable.Size = new System.Drawing.Size(62, 23);
            this.but_asenable.TabIndex = 1;
            this.but_asenable.Text = "Enable";
            this.but_asenable.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_asenable, "Restore airspeed sensor");
            this.but_asenable.UseVisualStyleBackColor = true;
            this.but_asenable.Click += new System.EventHandler(this.but_asenable_Click);
            // 
            // but_asdisable
            // 
            this.but_asdisable.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.but_asdisable.Location = new System.Drawing.Point(3, 3);
            this.but_asdisable.Name = "but_asdisable";
            this.but_asdisable.Size = new System.Drawing.Size(61, 23);
            this.but_asdisable.TabIndex = 0;
            this.but_asdisable.Text = "Disable";
            this.but_asdisable.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_asdisable, "Disable airspeed sensor");
            this.but_asdisable.UseVisualStyleBackColor = true;
            this.but_asdisable.Click += new System.EventHandler(this.but_asdisable_Click);
            // 
            // but_qstab
            // 
            this.but_qstab.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.but_qstab.Location = new System.Drawing.Point(75, 3);
            this.but_qstab.Name = "but_qstab";
            this.but_qstab.Size = new System.Drawing.Size(66, 23);
            this.but_qstab.TabIndex = 2;
            this.but_qstab.Text = "QStabilize";
            this.but_qstab.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_qstab, "Set mode to QStabilize");
            this.but_qstab.UseVisualStyleBackColor = true;
            this.but_qstab.Click += new System.EventHandler(this.but_mode_Click);
            // 
            // but_qhover
            // 
            this.but_qhover.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.but_qhover.Location = new System.Drawing.Point(3, 3);
            this.but_qhover.Name = "but_qhover";
            this.but_qhover.Size = new System.Drawing.Size(66, 23);
            this.but_qhover.TabIndex = 3;
            this.but_qhover.Text = "QHover";
            this.but_qhover.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_qhover, "Set mode to QHover");
            this.but_qhover.UseVisualStyleBackColor = true;
            this.but_qhover.Click += new System.EventHandler(this.but_mode_Click);
            // 
            // but_disarm
            // 
            this.but_disarm.Location = new System.Drawing.Point(87, 19);
            this.but_disarm.Name = "but_disarm";
            this.but_disarm.Size = new System.Drawing.Size(75, 23);
            this.but_disarm.TabIndex = 1;
            this.but_disarm.Text = "DISARM";
            this.but_disarm.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_disarm, "Disarm aircraft");
            this.but_disarm.UseVisualStyleBackColor = true;
            this.but_disarm.Click += new System.EventHandler(this.but_disarm_Click);
            // 
            // but_arm
            // 
            this.but_arm.Location = new System.Drawing.Point(6, 19);
            this.but_arm.Name = "but_arm";
            this.but_arm.Size = new System.Drawing.Size(75, 23);
            this.but_arm.TabIndex = 0;
            this.but_arm.Text = "ARM";
            this.but_arm.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_arm, "Arm aircraft (with force-arm option)");
            this.but_arm.UseVisualStyleBackColor = true;
            this.but_arm.Click += new System.EventHandler(this.but_arm_Click);
            // 
            // tableLayoutPanelOuter
            // 
            this.tableLayoutPanelOuter.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanelOuter.ColumnCount = 2;
            this.tableLayoutPanelOuter.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanelOuter.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanelOuter.Controls.Add(this.groupBox1, 0, 4);
            this.tableLayoutPanelOuter.Controls.Add(this.groupBox2, 0, 3);
            this.tableLayoutPanelOuter.Controls.Add(this.txt_messagebox, 0, 5);
            this.tableLayoutPanelOuter.Controls.Add(this.grp_modes, 0, 0);
            this.tableLayoutPanelOuter.Controls.Add(this.grp_qassist, 0, 2);
            this.tableLayoutPanelOuter.Controls.Add(this.grp_engine, 1, 1);
            this.tableLayoutPanelOuter.Controls.Add(this.grp_airspeed, 0, 1);
            this.tableLayoutPanelOuter.Location = new System.Drawing.Point(0, 0);
            this.tableLayoutPanelOuter.Margin = new System.Windows.Forms.Padding(2);
            this.tableLayoutPanelOuter.Name = "tableLayoutPanelOuter";
            this.tableLayoutPanelOuter.RowCount = 6;
            this.tableLayoutPanelOuter.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            this.tableLayoutPanelOuter.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            this.tableLayoutPanelOuter.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            this.tableLayoutPanelOuter.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            this.tableLayoutPanelOuter.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 60F));
            this.tableLayoutPanelOuter.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.tableLayoutPanelOuter.Size = new System.Drawing.Size(300, 367);
            this.tableLayoutPanelOuter.TabIndex = 79;
            // 
            // groupBox1
            // 
            this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanelOuter.SetColumnSpan(this.groupBox1, 2);
            this.groupBox1.Controls.Add(this.but_disarm);
            this.groupBox1.Controls.Add(this.but_arm);
            this.groupBox1.Location = new System.Drawing.Point(3, 243);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(294, 54);
            this.groupBox1.TabIndex = 8;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Arm/Disarm";
            // 
            // groupBox2
            // 
            this.groupBox2.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanelOuter.SetColumnSpan(this.groupBox2, 2);
            this.groupBox2.Controls.Add(this.but_fenceenable);
            this.groupBox2.Controls.Add(this.but_fencedisable);
            this.groupBox2.Location = new System.Drawing.Point(3, 183);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(294, 54);
            this.groupBox2.TabIndex = 7;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Fence";
            // 
            // txt_messagebox
            // 
            this.tableLayoutPanelOuter.SetColumnSpan(this.txt_messagebox, 2);
            this.txt_messagebox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txt_messagebox.Location = new System.Drawing.Point(2, 302);
            this.txt_messagebox.Margin = new System.Windows.Forms.Padding(2);
            this.txt_messagebox.Multiline = true;
            this.txt_messagebox.Name = "txt_messagebox";
            this.txt_messagebox.ReadOnly = true;
            this.txt_messagebox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txt_messagebox.Size = new System.Drawing.Size(296, 63);
            this.txt_messagebox.TabIndex = 2;
            this.txt_messagebox.Text = "Transition complete\r\nTransition airspeed wait\r\nAirspeed calibration complete\r\nAir" +
    "speed sensor calibration started";
            // 
            // grp_modes
            // 
            this.grp_modes.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanelOuter.SetColumnSpan(this.grp_modes, 2);
            this.grp_modes.Controls.Add(this.table_modes);
            this.grp_modes.Location = new System.Drawing.Point(3, 3);
            this.grp_modes.Name = "grp_modes";
            this.grp_modes.Size = new System.Drawing.Size(294, 54);
            this.grp_modes.TabIndex = 4;
            this.grp_modes.TabStop = false;
            this.grp_modes.Text = "Modes";
            // 
            // table_modes
            // 
            this.table_modes.ColumnCount = 4;
            this.table_modes.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.table_modes.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.table_modes.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.table_modes.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.table_modes.Controls.Add(this.but_manual, 3, 0);
            this.table_modes.Controls.Add(this.but_qhover, 0, 0);
            this.table_modes.Controls.Add(this.but_qstab, 1, 0);
            this.table_modes.Controls.Add(this.but_fbwa, 2, 0);
            this.table_modes.Dock = System.Windows.Forms.DockStyle.Fill;
            this.table_modes.Location = new System.Drawing.Point(3, 16);
            this.table_modes.Margin = new System.Windows.Forms.Padding(0);
            this.table_modes.Name = "table_modes";
            this.table_modes.RowCount = 1;
            this.table_modes.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.table_modes.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 35F));
            this.table_modes.Size = new System.Drawing.Size(288, 35);
            this.table_modes.TabIndex = 0;
            // 
            // grp_qassist
            // 
            this.grp_qassist.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanelOuter.SetColumnSpan(this.grp_qassist, 2);
            this.grp_qassist.Controls.Add(this.but_qaforce);
            this.grp_qassist.Controls.Add(this.but_qaenable);
            this.grp_qassist.Controls.Add(this.but_qadisable);
            this.grp_qassist.Location = new System.Drawing.Point(3, 123);
            this.grp_qassist.Name = "grp_qassist";
            this.grp_qassist.Size = new System.Drawing.Size(294, 54);
            this.grp_qassist.TabIndex = 5;
            this.grp_qassist.TabStop = false;
            this.grp_qassist.Text = "QASSIST";
            // 
            // grp_airspeed
            // 
            this.grp_airspeed.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grp_airspeed.Controls.Add(this.tableLayoutPanel1);
            this.grp_airspeed.Location = new System.Drawing.Point(3, 63);
            this.grp_airspeed.Name = "grp_airspeed";
            this.grp_airspeed.Size = new System.Drawing.Size(144, 54);
            this.grp_airspeed.TabIndex = 6;
            this.grp_airspeed.TabStop = false;
            this.grp_airspeed.Text = "Airspeed";
            // 
            // grp_engine
            // 
            this.grp_engine.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grp_engine.Controls.Add(this.tableLayoutPanel2);
            this.grp_engine.Location = new System.Drawing.Point(153, 63);
            this.grp_engine.Name = "grp_engine";
            this.grp_engine.Size = new System.Drawing.Size(144, 54);
            this.grp_engine.TabIndex = 9;
            this.grp_engine.TabStop = false;
            this.grp_engine.Text = "Engine";
            // 
            // but_startEng
            // 
            this.but_startEng.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.but_startEng.Location = new System.Drawing.Point(72, 3);
            this.but_startEng.Name = "but_startEng";
            this.but_startEng.Size = new System.Drawing.Size(63, 23);
            this.but_startEng.TabIndex = 1;
            this.but_startEng.Text = "Start Engine";
            this.but_startEng.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_startEng, "Send Start Engine Command");
            this.but_startEng.UseVisualStyleBackColor = true;
            this.but_startEng.Click += new System.EventHandler(this.but_startEng_Click);
            // 
            // but_killEng
            // 
            this.but_killEng.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.but_killEng.Location = new System.Drawing.Point(3, 3);
            this.but_killEng.Name = "but_killEng";
            this.but_killEng.Size = new System.Drawing.Size(63, 23);
            this.but_killEng.TabIndex = 0;
            this.but_killEng.Text = "Kill Engine";
            this.but_killEng.TextColorNotEnabled = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(87)))), ((int)(((byte)(4)))));
            this.toolTip1.SetToolTip(this.but_killEng, "Send Kill Engine Command");
            this.but_killEng.UseVisualStyleBackColor = true;
            this.but_killEng.Click += new System.EventHandler(this.but_killEng_Click);
            // 
            // tableLayoutPanel1
            // 
            this.tableLayoutPanel1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanel1.ColumnCount = 2;
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.Controls.Add(this.but_asdisable, 0, 0);
            this.tableLayoutPanel1.Controls.Add(this.but_asenable, 1, 0);
            this.tableLayoutPanel1.Location = new System.Drawing.Point(6, 19);
            this.tableLayoutPanel1.Name = "tableLayoutPanel1";
            this.tableLayoutPanel1.RowCount = 1;
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel1.Size = new System.Drawing.Size(135, 29);
            this.tableLayoutPanel1.TabIndex = 2;
            // 
            // tableLayoutPanel2
            // 
            this.tableLayoutPanel2.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.tableLayoutPanel2.ColumnCount = 2;
            this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel2.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel2.Controls.Add(this.but_killEng, 0, 0);
            this.tableLayoutPanel2.Controls.Add(this.but_startEng, 1, 0);
            this.tableLayoutPanel2.Location = new System.Drawing.Point(3, 19);
            this.tableLayoutPanel2.Name = "tableLayoutPanel2";
            this.tableLayoutPanel2.RowCount = 1;
            this.tableLayoutPanel2.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.tableLayoutPanel2.Size = new System.Drawing.Size(138, 29);
            this.tableLayoutPanel2.TabIndex = 2;
            // 
            // EmergencyTab
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.tableLayoutPanelOuter);
            this.Name = "EmergencyTab";
            this.Size = new System.Drawing.Size(300, 367);
            this.VisibleChanged += new System.EventHandler(this.EmergencyTab_VisibleChanged);
            this.tableLayoutPanelOuter.ResumeLayout(false);
            this.tableLayoutPanelOuter.PerformLayout();
            this.groupBox1.ResumeLayout(false);
            this.groupBox2.ResumeLayout(false);
            this.grp_modes.ResumeLayout(false);
            this.table_modes.ResumeLayout(false);
            this.grp_qassist.ResumeLayout(false);
            this.grp_airspeed.ResumeLayout(false);
            this.grp_engine.ResumeLayout(false);
            this.tableLayoutPanel1.ResumeLayout(false);
            this.tableLayoutPanel2.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanelOuter;
        private System.Windows.Forms.GroupBox groupBox2;
        private MissionPlanner.Controls.MyButton but_fenceenable;
        private MissionPlanner.Controls.MyButton but_fencedisable;
        private System.Windows.Forms.GroupBox grp_modes;
        private MissionPlanner.Controls.MyButton but_manual;
        private MissionPlanner.Controls.MyButton but_fbwa;
        private System.Windows.Forms.GroupBox grp_qassist;
        private MissionPlanner.Controls.MyButton but_qaforce;
        private MissionPlanner.Controls.MyButton but_qaenable;
        private MissionPlanner.Controls.MyButton but_qadisable;
        private System.Windows.Forms.GroupBox grp_airspeed;
        private MissionPlanner.Controls.MyButton but_asenable;
        private MissionPlanner.Controls.MyButton but_asdisable;
        private System.Windows.Forms.TextBox txt_messagebox;
        private MissionPlanner.Controls.MyButton but_qhover;
        private MissionPlanner.Controls.MyButton but_qstab;
        private System.Windows.Forms.TableLayoutPanel table_modes;
        private System.Windows.Forms.GroupBox groupBox1;
        private MissionPlanner.Controls.MyButton but_disarm;
        private MissionPlanner.Controls.MyButton but_arm;
        private System.Windows.Forms.GroupBox grp_engine;
        private MissionPlanner.Controls.MyButton but_startEng;
        private MissionPlanner.Controls.MyButton but_killEng;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel2;
    }
}
