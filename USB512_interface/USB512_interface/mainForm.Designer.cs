namespace USB512_interface
{
    partial class Form1
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStop = new System.Windows.Forms.Button();
            this.comboDrives = new System.Windows.Forms.ComboBox();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.lblSpeed = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.btnSaveFolder = new System.Windows.Forms.Button();
            this.lblRecInfo = new System.Windows.Forms.Label();
            this.llblSaveFolder = new System.Windows.Forms.LinkLabel();
            this.llblSavefile = new System.Windows.Forms.LinkLabel();
            this.btnPreview = new System.Windows.Forms.Button();
            this.btnScope = new System.Windows.Forms.Button();
            this.btnDfu = new System.Windows.Forms.Button();
            this.btnStreams = new System.Windows.Forms.Button();
            this.lblStaleBanks = new System.Windows.Forms.Label();
            this.SuspendLayout();
            //
            // btnStart
            //
            this.btnStart.Enabled = false;
            this.btnStart.Location = new System.Drawing.Point(184, 106);
            this.btnStart.Margin = new System.Windows.Forms.Padding(2);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(82, 32);
            this.btnStart.TabIndex = 0;
            this.btnStart.Text = "Start";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            //
            // btnStop
            //
            this.btnStop.Enabled = false;
            this.btnStop.Location = new System.Drawing.Point(270, 106);
            this.btnStop.Margin = new System.Windows.Forms.Padding(2);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(82, 32);
            this.btnStop.TabIndex = 0;
            this.btnStop.Text = "Stop";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            //
            // comboDrives
            //
            this.comboDrives.FormattingEnabled = true;
            this.comboDrives.ImeMode = System.Windows.Forms.ImeMode.Disable;
            this.comboDrives.Location = new System.Drawing.Point(5, 32);
            this.comboDrives.Margin = new System.Windows.Forms.Padding(2);
            this.comboDrives.Name = "comboDrives";
            this.comboDrives.Size = new System.Drawing.Size(442, 21);
            this.comboDrives.TabIndex = 1;
            this.comboDrives.SelectedIndexChanged += new System.EventHandler(this.comboDrives_SelectedIndexChanged);
            this.comboDrives.Click += new System.EventHandler(this.comboDrives_Click);
            //
            // btnRefresh
            //
            this.btnRefresh.Location = new System.Drawing.Point(451, 32);
            this.btnRefresh.Margin = new System.Windows.Forms.Padding(2);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(70, 19);
            this.btnRefresh.TabIndex = 0;
            this.btnRefresh.Text = "Refresh";
            this.btnRefresh.UseVisualStyleBackColor = true;
            this.btnRefresh.Click += new System.EventHandler(this.btnRefresh_Click);
            //
            // lblSpeed
            //
            this.lblSpeed.AutoSize = true;
            this.lblSpeed.Location = new System.Drawing.Point(5, 175);
            this.lblSpeed.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblSpeed.Name = "lblSpeed";
            this.lblSpeed.Size = new System.Drawing.Size(0, 13);
            this.lblSpeed.TabIndex = 2;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(5, 15);
            this.label1.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(83, 13);
            this.label1.TabIndex = 3;
            this.label1.Text = "WinUSB device";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(9, 63);
            this.label2.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(71, 13);
            this.label2.TabIndex = 3;
            this.label2.Text = "Output folder:";
            this.label2.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(9, 84);
            this.label4.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(50, 13);
            this.label4.TabIndex = 3;
            this.label4.Text = "File path:";
            this.label4.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // btnSaveFolder
            // 
            this.btnSaveFolder.Location = new System.Drawing.Point(5, 106);
            this.btnSaveFolder.Margin = new System.Windows.Forms.Padding(2);
            this.btnSaveFolder.Name = "btnSaveFolder";
            this.btnSaveFolder.Size = new System.Drawing.Size(152, 32);
            this.btnSaveFolder.TabIndex = 0;
            this.btnSaveFolder.Text = "Select folder";
            this.btnSaveFolder.UseVisualStyleBackColor = true;
            this.btnSaveFolder.Click += new System.EventHandler(this.btnSaveFolder_Click);
            // 
            // lblRecInfo
            // 
            this.lblRecInfo.AutoSize = true;
            this.lblRecInfo.Location = new System.Drawing.Point(5, 193);
            this.lblRecInfo.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblRecInfo.Name = "lblRecInfo";
            this.lblRecInfo.Size = new System.Drawing.Size(0, 13);
            this.lblRecInfo.TabIndex = 2;
            // 
            // llblSaveFolder
            // 
            this.llblSaveFolder.AutoSize = true;
            this.llblSaveFolder.Location = new System.Drawing.Point(82, 62);
            this.llblSaveFolder.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.llblSaveFolder.Name = "llblSaveFolder";
            this.llblSaveFolder.Size = new System.Drawing.Size(0, 13);
            this.llblSaveFolder.TabIndex = 4;
            this.llblSaveFolder.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.llblSaveFolder_LinkClicked);
            // 
            // llblSavefile
            // 
            this.llblSavefile.AutoSize = true;
            this.llblSavefile.Location = new System.Drawing.Point(82, 85);
            this.llblSavefile.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.llblSavefile.Name = "llblSavefile";
            this.llblSavefile.Size = new System.Drawing.Size(0, 13);
            this.llblSavefile.TabIndex = 5;
            this.llblSavefile.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.llblSavefile_LinkClicked);
            // 
            // btnPreview
            // 
            this.btnPreview.Location = new System.Drawing.Point(184, 138);
            this.btnPreview.Margin = new System.Windows.Forms.Padding(2);
            this.btnPreview.Name = "btnPreview";
            this.btnPreview.Size = new System.Drawing.Size(82, 36);
            this.btnPreview.TabIndex = 0;
            this.btnPreview.Text = "Camera preview";
            this.btnPreview.UseVisualStyleBackColor = true;
            this.btnPreview.Click += new System.EventHandler(this.btnPreview_Click);
            //
            // btnScope
            //
            this.btnScope.Location = new System.Drawing.Point(270, 138);
            this.btnScope.Margin = new System.Windows.Forms.Padding(2);
            this.btnScope.Name = "btnScope";
            this.btnScope.Size = new System.Drawing.Size(82, 36);
            this.btnScope.TabIndex = 0;
            this.btnScope.Text = "Ephys Preview";
            this.btnScope.UseVisualStyleBackColor = true;
            this.btnScope.Click += new System.EventHandler(this.btnScope_Click);
            //
            // btnDfu
            //
            this.btnDfu.Location = new System.Drawing.Point(471, 138);
            this.btnDfu.Margin = new System.Windows.Forms.Padding(2);
            this.btnDfu.Name = "btnDfu";
            this.btnDfu.Size = new System.Drawing.Size(93, 36);
            this.btnDfu.TabIndex = 0;
            this.btnDfu.Text = "Firmware Upgrade Mode";
            this.btnDfu.UseVisualStyleBackColor = true;
            this.btnDfu.Click += new System.EventHandler(this.btnDfu_Click);
            //
            // btnStreams
            //
            this.btnStreams.Location = new System.Drawing.Point(8, 138);
            this.btnStreams.Margin = new System.Windows.Forms.Padding(2);
            this.btnStreams.Name = "btnStreams";
            this.btnStreams.Size = new System.Drawing.Size(149, 36);
            this.btnStreams.TabIndex = 20;
            this.btnStreams.Text = "Streams settings";
            this.btnStreams.UseVisualStyleBackColor = true;
            this.btnStreams.Click += new System.EventHandler(this.btnStreams_Click);
            //
            // lblStaleBanks
            //
            this.lblStaleBanks.AutoSize = true;
            this.lblStaleBanks.Location = new System.Drawing.Point(5, 288);
            this.lblStaleBanks.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
            this.lblStaleBanks.Name = "lblStaleBanks";
            this.lblStaleBanks.Size = new System.Drawing.Size(0, 13);
            this.lblStaleBanks.TabIndex = 2;
            //
            // imuTelemetry
            //
            this.imuTelemetry.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(31)))), ((int)(((byte)(34)))), ((int)(((byte)(39)))));
            this.imuTelemetry.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.imuTelemetry.ForeColor = System.Drawing.Color.Gainsboro;
            this.imuTelemetry.Location = new System.Drawing.Point(5, 328);
            this.imuTelemetry.Margin = new System.Windows.Forms.Padding(2);
            this.imuTelemetry.Name = "imuTelemetry";
            this.imuTelemetry.Size = new System.Drawing.Size(559, 116);
            this.imuTelemetry.TabIndex = 6;
            //
            // Form1
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(596, 455);
            this.Controls.Add(this.imuTelemetry);
            this.Controls.Add(this.btnStreams);
            this.Controls.Add(this.llblSavefile);
            this.Controls.Add(this.llblSaveFolder);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.lblStaleBanks);
            this.Controls.Add(this.lblRecInfo);
            this.Controls.Add(this.lblSpeed);
            this.Controls.Add(this.comboDrives);
            this.Controls.Add(this.btnSaveFolder);
            this.Controls.Add(this.btnPreview);
            this.Controls.Add(this.btnScope);
            this.Controls.Add(this.btnDfu);
            this.Controls.Add(this.btnStop);
            this.Controls.Add(this.btnRefresh);
            this.Controls.Add(this.btnStart);
            this.Margin = new System.Windows.Forms.Padding(2);
            this.Name = "Form1";
            this.Text = "High-speed neural interface";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.ComboBox comboDrives;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.Label lblSpeed;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Button btnSaveFolder;
        private System.Windows.Forms.Label lblRecInfo;
        private System.Windows.Forms.LinkLabel llblSaveFolder;
        private System.Windows.Forms.LinkLabel llblSavefile;
        private System.Windows.Forms.Button btnPreview;
        private System.Windows.Forms.Button btnScope;
        private System.Windows.Forms.Button btnDfu;
        private System.Windows.Forms.Button btnStreams;
        private System.Windows.Forms.Label lblStaleBanks;
        private ImuTelemetryPanel imuTelemetry;
    }
}

