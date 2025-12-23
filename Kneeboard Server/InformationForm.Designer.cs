
namespace Kneeboard_Server
{
    partial class InformationForm
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
            this.closeButton = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.Version = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.linkLabel1 = new System.Windows.Forms.LinkLabel();
            this.autostart = new System.Windows.Forms.CheckBox();
            this.minimized = new System.Windows.Forms.CheckBox();
            this.simStart = new System.Windows.Forms.CheckBox();
            this.folderpathInput = new System.Windows.Forms.TextBox();
            this.SimbriefIdInput = new System.Windows.Forms.TextBox();
            this.VatsimCidInput = new System.Windows.Forms.TextBox();
            this.IvaoVidInput = new System.Windows.Forms.TextBox();
            this.clearCacheButton = new System.Windows.Forms.Button();
            this.maxCacheSizeInput = new System.Windows.Forms.TextBox();
            this.cacheSizeLabel = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // closeButton
            // 
            this.closeButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.closeButton.BackColor = System.Drawing.SystemColors.Window;
            this.closeButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.closeButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.closeButton.Location = new System.Drawing.Point(174, 12);
            this.closeButton.Name = "closeButton";
            this.closeButton.Size = new System.Drawing.Size(23, 23);
            this.closeButton.TabIndex = 24;
            this.closeButton.Text = "X";
            this.closeButton.UseVisualStyleBackColor = false;
            this.closeButton.Click += new System.EventHandler(this.CloseButton_Click);
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.ForeColor = System.Drawing.SystemColors.Highlight;
            this.label1.Location = new System.Drawing.Point(12, 49);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(185, 23);
            this.label1.TabIndex = 25;
            this.label1.Text = "Kneeboard Server";
            this.label1.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // Version
            // 
            this.Version.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.Version.Font = new System.Drawing.Font("Microsoft Sans Serif", 11.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Version.ForeColor = System.Drawing.SystemColors.Highlight;
            this.Version.Location = new System.Drawing.Point(12, 80);
            this.Version.Name = "Version";
            this.Version.Size = new System.Drawing.Size(185, 23);
            this.Version.TabIndex = 26;
            this.Version.Text = "Version: 2.0.0.0";
            this.Version.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            //
            // label2
            //
            this.label2.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.ForeColor = System.Drawing.SystemColors.Highlight;
            this.label2.Location = new System.Drawing.Point(11, 367);
            this.label2.Name = "label2";
            this.label2.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this.label2.Size = new System.Drawing.Size(186, 23);
            this.label2.TabIndex = 28;
            this.label2.Text = "Gsimulations - 2021";
            this.label2.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.label2.UseCompatibleTextRendering = true;
            //
            // linkLabel1
            //
            this.linkLabel1.ActiveLinkColor = System.Drawing.Color.DodgerBlue;
            this.linkLabel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.linkLabel1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.linkLabel1.ForeColor = System.Drawing.SystemColors.Highlight;
            this.linkLabel1.Location = new System.Drawing.Point(12, 394);
            this.linkLabel1.Name = "linkLabel1";
            this.linkLabel1.Size = new System.Drawing.Size(185, 23);
            this.linkLabel1.TabIndex = 29;
            this.linkLabel1.TabStop = true;
            this.linkLabel1.Text = "support@gsimulations.com";
            this.linkLabel1.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.linkLabel1.VisitedLinkColor = System.Drawing.Color.Blue;
            // 
            // autostart
            // 
            this.autostart.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.autostart.AutoSize = true;
            this.autostart.Location = new System.Drawing.Point(46, 139);
            this.autostart.Name = "autostart";
            this.autostart.Size = new System.Drawing.Size(117, 17);
            this.autostart.TabIndex = 30;
            this.autostart.Text = "Start with Windows";
            this.autostart.UseVisualStyleBackColor = true;
            this.autostart.CheckedChanged += new System.EventHandler(this.chkBackup_CheckChanged);
            // 
            // minimized
            // 
            this.minimized.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.minimized.AutoSize = true;
            this.minimized.Location = new System.Drawing.Point(46, 162);
            this.minimized.Name = "minimized";
            this.minimized.Size = new System.Drawing.Size(116, 17);
            this.minimized.TabIndex = 31;
            this.minimized.Text = "Start in System tray";
            this.minimized.UseVisualStyleBackColor = true;
            this.minimized.CheckedChanged += new System.EventHandler(this.minimized_CheckedChanged);
            // 
            // simStart
            // 
            this.simStart.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.simStart.AutoSize = true;
            this.simStart.Checked = true;
            this.simStart.CheckState = System.Windows.Forms.CheckState.Checked;
            this.simStart.Location = new System.Drawing.Point(46, 116);
            this.simStart.Name = "simStart";
            this.simStart.Size = new System.Drawing.Size(116, 17);
            this.simStart.TabIndex = 32;
            this.simStart.Text = "Start with Simulator";
            this.simStart.UseVisualStyleBackColor = true;
            this.simStart.CheckedChanged += new System.EventHandler(this.MSFSStart_CheckChanged);
            // 
            // folderpathInput
            // 
            this.folderpathInput.Location = new System.Drawing.Point(16, 193);
            this.folderpathInput.Name = "folderpathInput";
            this.folderpathInput.Size = new System.Drawing.Size(181, 20);
            this.folderpathInput.TabIndex = 33;
            this.folderpathInput.Text = "Path to exe.xml";
            this.folderpathInput.MouseDown += new System.Windows.Forms.MouseEventHandler(this.folderpathInput_MouseDown);
            //
            // SimbriefIdInput
            //
            this.SimbriefIdInput.Location = new System.Drawing.Point(16, 219);
            this.SimbriefIdInput.Name = "SimbriefIdInput";
            this.SimbriefIdInput.Size = new System.Drawing.Size(181, 20);
            this.SimbriefIdInput.TabIndex = 34;
            this.SimbriefIdInput.Text = "Simbrief ID or Username";
            this.SimbriefIdInput.TextChanged += new System.EventHandler(this.SimbriefIdInput_TextChanged);
            //
            // VatsimCidInput
            //
            this.VatsimCidInput.Location = new System.Drawing.Point(16, 245);
            this.VatsimCidInput.Name = "VatsimCidInput";
            this.VatsimCidInput.Size = new System.Drawing.Size(181, 20);
            this.VatsimCidInput.TabIndex = 38;
            this.VatsimCidInput.Text = "VATSIM CID";
            this.VatsimCidInput.TextChanged += new System.EventHandler(this.VatsimCidInput_TextChanged);
            //
            // IvaoVidInput
            //
            this.IvaoVidInput.Location = new System.Drawing.Point(16, 271);
            this.IvaoVidInput.Name = "IvaoVidInput";
            this.IvaoVidInput.Size = new System.Drawing.Size(181, 20);
            this.IvaoVidInput.TabIndex = 39;
            this.IvaoVidInput.Text = "IVAO VID";
            this.IvaoVidInput.TextChanged += new System.EventHandler(this.IvaoVidInput_TextChanged);
            //
            // clearCacheButton
            //
            this.clearCacheButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.clearCacheButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.clearCacheButton.Location = new System.Drawing.Point(16, 332);
            this.clearCacheButton.Name = "clearCacheButton";
            this.clearCacheButton.Size = new System.Drawing.Size(181, 23);
            this.clearCacheButton.TabIndex = 35;
            this.clearCacheButton.Text = "Clear OpenAIP Cache";
            this.clearCacheButton.UseVisualStyleBackColor = true;
            this.clearCacheButton.Click += new System.EventHandler(this.ClearCacheButton_Click);
            //
            // maxCacheSizeInput
            //
            this.maxCacheSizeInput.Location = new System.Drawing.Point(130, 304);
            this.maxCacheSizeInput.Name = "maxCacheSizeInput";
            this.maxCacheSizeInput.Size = new System.Drawing.Size(67, 20);
            this.maxCacheSizeInput.TabIndex = 36;
            this.maxCacheSizeInput.Text = "0";
            this.maxCacheSizeInput.TextChanged += new System.EventHandler(this.MaxCacheSizeInput_TextChanged);
            //
            // cacheSizeLabel
            //
            this.cacheSizeLabel.AutoSize = true;
            this.cacheSizeLabel.ForeColor = System.Drawing.SystemColors.Highlight;
            this.cacheSizeLabel.Location = new System.Drawing.Point(13, 307);
            this.cacheSizeLabel.Name = "cacheSizeLabel";
            this.cacheSizeLabel.Size = new System.Drawing.Size(111, 13);
            this.cacheSizeLabel.TabIndex = 37;
            this.cacheSizeLabel.Text = "Max Cache Size (MB):";
            //
            // InformationForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.ClientSize = new System.Drawing.Size(209, 422);
            this.Controls.Add(this.IvaoVidInput);
            this.Controls.Add(this.VatsimCidInput);
            this.Controls.Add(this.cacheSizeLabel);
            this.Controls.Add(this.maxCacheSizeInput);
            this.Controls.Add(this.clearCacheButton);
            this.Controls.Add(this.SimbriefIdInput);
            this.Controls.Add(this.folderpathInput);
            this.Controls.Add(this.simStart);
            this.Controls.Add(this.minimized);
            this.Controls.Add(this.autostart);
            this.Controls.Add(this.linkLabel1);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.Version);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.closeButton);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "InformationForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Information";
            this.Load += new System.EventHandler(this.InformationForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button closeButton;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label Version;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.LinkLabel linkLabel1;
        private System.Windows.Forms.CheckBox autostart;
        private System.Windows.Forms.CheckBox minimized;
        private System.Windows.Forms.CheckBox simStart;
        private System.Windows.Forms.TextBox folderpathInput;
        private System.Windows.Forms.TextBox SimbriefIdInput;
        private System.Windows.Forms.TextBox VatsimCidInput;
        private System.Windows.Forms.TextBox IvaoVidInput;
        private System.Windows.Forms.Button clearCacheButton;
        private System.Windows.Forms.TextBox maxCacheSizeInput;
        private System.Windows.Forms.Label cacheSizeLabel;
    }
}