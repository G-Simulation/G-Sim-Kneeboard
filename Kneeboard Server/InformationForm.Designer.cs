
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
            this.exeXml2024Label = new System.Windows.Forms.Label();
            this.exeXml2024Input = new System.Windows.Forms.TextBox();
            this.exeXml2020Label = new System.Windows.Forms.Label();
            this.exeXml2020Input = new System.Windows.Forms.TextBox();
            this.SimbriefIdInput = new System.Windows.Forms.TextBox();
            this.simbriefStatusLabel = new System.Windows.Forms.Label();
            this.VatsimCidInput = new System.Windows.Forms.TextBox();
            this.vatsimStatusLabel = new System.Windows.Forms.Label();
            this.IvaoVidInput = new System.Windows.Forms.TextBox();
            this.ivaoStatusLabel = new System.Windows.Forms.Label();
            this.clearCacheButton = new System.Windows.Forms.Button();
            this.maxCacheSizeInput = new System.Windows.Forms.TextBox();
            this.cacheSizeLabel = new System.Windows.Forms.Label();
            this.navigraphLabel = new System.Windows.Forms.Label();
            this.navigraphStatusLabel = new System.Windows.Forms.Label();
            this.navigraphLoginButton = new System.Windows.Forms.Button();
            this.startupGroupBox = new System.Windows.Forms.GroupBox();
            this.idsGroupBox = new System.Windows.Forms.GroupBox();
            this.cacheGroupBox = new System.Windows.Forms.GroupBox();
            this.navigraphGroupBox = new System.Windows.Forms.GroupBox();
            this.elevationGroupBox = new System.Windows.Forms.GroupBox();
            this.useSrtmCheckbox = new System.Windows.Forms.CheckBox();
            this.srtmRegionComboBox = new System.Windows.Forms.ComboBox();
            this.downloadSrtmButton = new System.Windows.Forms.Button();
            this.elevationStatusLabel = new System.Windows.Forms.Label();
            this.panelGroupBox = new System.Windows.Forms.GroupBox();
            this.panelStatusTitleLabel = new System.Windows.Forms.Label();
            this.panelStatusLabel = new System.Windows.Forms.Label();
            this.panelPathLabel = new System.Windows.Forms.Label();
            this.installPanelButton = new System.Windows.Forms.Button();
            this.serialGroupBox = new System.Windows.Forms.GroupBox();
            this.serialNumberInput = new System.Windows.Forms.TextBox();
            this.serialStatusLabel = new System.Windows.Forms.Label();
            this.updateGroupBox = new System.Windows.Forms.GroupBox();
            this.autoUpdateCheckbox = new System.Windows.Forms.CheckBox();
            this.checkForUpdatesButton = new System.Windows.Forms.Button();
            this.supportLink = new System.Windows.Forms.LinkLabel();
            this.startupGroupBox.SuspendLayout();
            this.idsGroupBox.SuspendLayout();
            this.cacheGroupBox.SuspendLayout();
            this.navigraphGroupBox.SuspendLayout();
            this.elevationGroupBox.SuspendLayout();
            this.panelGroupBox.SuspendLayout();
            this.serialGroupBox.SuspendLayout();
            this.updateGroupBox.SuspendLayout();
            this.SuspendLayout();
            // 
            // closeButton
            // 
            this.closeButton.BackColor = System.Drawing.SystemColors.Window;
            this.closeButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.closeButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.closeButton.Location = new System.Drawing.Point(462, 8);
            this.closeButton.Name = "closeButton";
            this.closeButton.Size = new System.Drawing.Size(23, 23);
            this.closeButton.TabIndex = 24;
            this.closeButton.Text = "X";
            this.closeButton.UseVisualStyleBackColor = false;
            this.closeButton.Click += new System.EventHandler(this.CloseButton_Click);
            // 
            // label1
            // 
            this.label1.Font = new System.Drawing.Font("Segoe UI", 14.25F, System.Drawing.FontStyle.Bold);
            this.label1.ForeColor = System.Drawing.SystemColors.Highlight;
            this.label1.Location = new System.Drawing.Point(12, 29);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(466, 25);
            this.label1.TabIndex = 25;
            this.label1.Text = "Kneeboard Server";
            this.label1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // Version
            // 
            this.Version.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.Version.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.Version.Location = new System.Drawing.Point(12, 54);
            this.Version.Name = "Version";
            this.Version.Size = new System.Drawing.Size(466, 16);
            this.Version.TabIndex = 26;
            this.Version.Text = "Version: 2.0.0.0";
            this.Version.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // label2
            // 
            this.label2.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.label2.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.label2.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.label2.Location = new System.Drawing.Point(12, 497);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(466, 16);
            this.label2.TabIndex = 28;
            this.label2.Text = "Gsimulations - 2021";
            this.label2.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // linkLabel1
            // 
            this.linkLabel1.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.linkLabel1.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.linkLabel1.Location = new System.Drawing.Point(12, 513);
            this.linkLabel1.Name = "linkLabel1";
            this.linkLabel1.Size = new System.Drawing.Size(466, 16);
            this.linkLabel1.TabIndex = 29;
            this.linkLabel1.TabStop = true;
            this.linkLabel1.Text = "support@gsimulations.com";
            this.linkLabel1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // autostart
            // 
            this.autostart.AutoSize = true;
            this.autostart.ForeColor = System.Drawing.SystemColors.ControlText;
            this.autostart.Location = new System.Drawing.Point(10, 40);
            this.autostart.Name = "autostart";
            this.autostart.Size = new System.Drawing.Size(117, 17);
            this.autostart.TabIndex = 7;
            this.autostart.Text = "Start with Windows";
            this.autostart.UseVisualStyleBackColor = true;
            this.autostart.CheckedChanged += new System.EventHandler(this.chkBackup_CheckChanged);
            // 
            // minimized
            // 
            this.minimized.AutoSize = true;
            this.minimized.ForeColor = System.Drawing.SystemColors.ControlText;
            this.minimized.Location = new System.Drawing.Point(10, 60);
            this.minimized.Name = "minimized";
            this.minimized.Size = new System.Drawing.Size(116, 17);
            this.minimized.TabIndex = 8;
            this.minimized.Text = "Start in System tray";
            this.minimized.UseVisualStyleBackColor = true;
            this.minimized.CheckedChanged += new System.EventHandler(this.minimized_CheckedChanged);
            // 
            // simStart
            // 
            this.simStart.AutoSize = true;
            this.simStart.ForeColor = System.Drawing.SystemColors.ControlText;
            this.simStart.Location = new System.Drawing.Point(10, 20);
            this.simStart.Name = "simStart";
            this.simStart.Size = new System.Drawing.Size(116, 17);
            this.simStart.TabIndex = 0;
            this.simStart.Text = "Start with Simulator";
            this.simStart.UseVisualStyleBackColor = true;
            this.simStart.CheckedChanged += new System.EventHandler(this.MSFSStart_CheckChanged);
            //
            // exeXml2024Label
            //
            this.exeXml2024Label.AutoSize = true;
            this.exeXml2024Label.Font = new System.Drawing.Font("Segoe UI", 7F);
            this.exeXml2024Label.ForeColor = System.Drawing.SystemColors.ControlText;
            this.exeXml2024Label.Location = new System.Drawing.Point(8, 84);
            this.exeXml2024Label.Name = "exeXml2024Label";
            this.exeXml2024Label.Size = new System.Drawing.Size(30, 12);
            this.exeXml2024Label.TabIndex = 3;
            this.exeXml2024Label.Text = "2024:";
            //
            // exeXml2024Input
            //
            this.exeXml2024Input.Font = new System.Drawing.Font("Segoe UI", 7F);
            this.exeXml2024Input.ForeColor = System.Drawing.SystemColors.GrayText;
            this.exeXml2024Input.Location = new System.Drawing.Point(42, 81);
            this.exeXml2024Input.Name = "exeXml2024Input";
            this.exeXml2024Input.ReadOnly = true;
            this.exeXml2024Input.Size = new System.Drawing.Size(178, 19);
            this.exeXml2024Input.TabIndex = 4;
            this.exeXml2024Input.Text = "Auto-Erkennung...";
            this.exeXml2024Input.MouseDown += new System.Windows.Forms.MouseEventHandler(this.ExeXml2024Input_MouseDown);
            //
            // exeXml2020Label
            //
            this.exeXml2020Label.AutoSize = true;
            this.exeXml2020Label.Font = new System.Drawing.Font("Segoe UI", 7F);
            this.exeXml2020Label.ForeColor = System.Drawing.SystemColors.ControlText;
            this.exeXml2020Label.Location = new System.Drawing.Point(8, 104);
            this.exeXml2020Label.Name = "exeXml2020Label";
            this.exeXml2020Label.Size = new System.Drawing.Size(30, 12);
            this.exeXml2020Label.TabIndex = 5;
            this.exeXml2020Label.Text = "2020:";
            //
            // exeXml2020Input
            //
            this.exeXml2020Input.Font = new System.Drawing.Font("Segoe UI", 7F);
            this.exeXml2020Input.ForeColor = System.Drawing.SystemColors.GrayText;
            this.exeXml2020Input.Location = new System.Drawing.Point(42, 101);
            this.exeXml2020Input.Name = "exeXml2020Input";
            this.exeXml2020Input.ReadOnly = true;
            this.exeXml2020Input.Size = new System.Drawing.Size(178, 19);
            this.exeXml2020Input.TabIndex = 6;
            this.exeXml2020Input.Text = "Auto-Erkennung...";
            this.exeXml2020Input.MouseDown += new System.Windows.Forms.MouseEventHandler(this.ExeXml2020Input_MouseDown);
            // 
            // SimbriefIdInput
            // 
            this.SimbriefIdInput.ForeColor = System.Drawing.SystemColors.GrayText;
            this.SimbriefIdInput.Location = new System.Drawing.Point(10, 20);
            this.SimbriefIdInput.Name = "SimbriefIdInput";
            this.SimbriefIdInput.Size = new System.Drawing.Size(170, 20);
            this.SimbriefIdInput.TabIndex = 0;
            this.SimbriefIdInput.Text = "SimBrief ID or Username";
            this.SimbriefIdInput.TextChanged += new System.EventHandler(this.SimbriefIdInput_TextChanged);
            //
            // simbriefStatusLabel
            //
            this.simbriefStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            this.simbriefStatusLabel.ForeColor = System.Drawing.Color.Gray;
            this.simbriefStatusLabel.Location = new System.Drawing.Point(185, 23);
            this.simbriefStatusLabel.Name = "simbriefStatusLabel";
            this.simbriefStatusLabel.Size = new System.Drawing.Size(40, 13);
            this.simbriefStatusLabel.TabIndex = 10;
            this.simbriefStatusLabel.Text = "---";
            //
            // VatsimCidInput
            //
            this.VatsimCidInput.ForeColor = System.Drawing.SystemColors.GrayText;
            this.VatsimCidInput.Location = new System.Drawing.Point(10, 46);
            this.VatsimCidInput.Name = "VatsimCidInput";
            this.VatsimCidInput.Size = new System.Drawing.Size(170, 20);
            this.VatsimCidInput.TabIndex = 1;
            this.VatsimCidInput.Text = "VATSIM CID";
            this.VatsimCidInput.TextChanged += new System.EventHandler(this.VatsimCidInput_TextChanged);
            //
            // vatsimStatusLabel
            //
            this.vatsimStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            this.vatsimStatusLabel.ForeColor = System.Drawing.Color.Gray;
            this.vatsimStatusLabel.Location = new System.Drawing.Point(185, 49);
            this.vatsimStatusLabel.Name = "vatsimStatusLabel";
            this.vatsimStatusLabel.Size = new System.Drawing.Size(40, 13);
            this.vatsimStatusLabel.TabIndex = 11;
            this.vatsimStatusLabel.Text = "---";
            //
            // IvaoVidInput
            //
            this.IvaoVidInput.ForeColor = System.Drawing.SystemColors.GrayText;
            this.IvaoVidInput.Location = new System.Drawing.Point(10, 72);
            this.IvaoVidInput.Name = "IvaoVidInput";
            this.IvaoVidInput.Size = new System.Drawing.Size(170, 20);
            this.IvaoVidInput.TabIndex = 2;
            this.IvaoVidInput.Text = "IVAO VID";
            this.IvaoVidInput.TextChanged += new System.EventHandler(this.IvaoVidInput_TextChanged);
            //
            // ivaoStatusLabel
            //
            this.ivaoStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            this.ivaoStatusLabel.ForeColor = System.Drawing.Color.Gray;
            this.ivaoStatusLabel.Location = new System.Drawing.Point(185, 75);
            this.ivaoStatusLabel.Name = "ivaoStatusLabel";
            this.ivaoStatusLabel.Size = new System.Drawing.Size(40, 13);
            this.ivaoStatusLabel.TabIndex = 12;
            this.ivaoStatusLabel.Text = "---";
            // 
            // clearCacheButton
            // 
            this.clearCacheButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.clearCacheButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.clearCacheButton.Location = new System.Drawing.Point(10, 43);
            this.clearCacheButton.Name = "clearCacheButton";
            this.clearCacheButton.Size = new System.Drawing.Size(210, 23);
            this.clearCacheButton.TabIndex = 2;
            this.clearCacheButton.Text = "Clear Cache";
            this.clearCacheButton.UseVisualStyleBackColor = true;
            this.clearCacheButton.Click += new System.EventHandler(this.ClearCacheButton_Click);
            // 
            // maxCacheSizeInput
            // 
            this.maxCacheSizeInput.Location = new System.Drawing.Point(125, 19);
            this.maxCacheSizeInput.Name = "maxCacheSizeInput";
            this.maxCacheSizeInput.Size = new System.Drawing.Size(95, 20);
            this.maxCacheSizeInput.TabIndex = 1;
            this.maxCacheSizeInput.Text = "0";
            this.maxCacheSizeInput.TextChanged += new System.EventHandler(this.MaxCacheSizeInput_TextChanged);
            // 
            // cacheSizeLabel
            // 
            this.cacheSizeLabel.AutoSize = true;
            this.cacheSizeLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.cacheSizeLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this.cacheSizeLabel.Location = new System.Drawing.Point(7, 22);
            this.cacheSizeLabel.Name = "cacheSizeLabel";
            this.cacheSizeLabel.Size = new System.Drawing.Size(112, 13);
            this.cacheSizeLabel.TabIndex = 0;
            this.cacheSizeLabel.Text = "Max Cache Size (MB):";
            // 
            // navigraphLabel
            // 
            this.navigraphLabel.AutoSize = true;
            this.navigraphLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.navigraphLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this.navigraphLabel.Location = new System.Drawing.Point(7, 18);
            this.navigraphLabel.Name = "navigraphLabel";
            this.navigraphLabel.Size = new System.Drawing.Size(40, 13);
            this.navigraphLabel.TabIndex = 0;
            this.navigraphLabel.Text = "Status:";
            // 
            // navigraphStatusLabel
            // 
            this.navigraphStatusLabel.AutoSize = true;
            this.navigraphStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            this.navigraphStatusLabel.ForeColor = System.Drawing.Color.Gray;
            this.navigraphStatusLabel.Location = new System.Drawing.Point(50, 18);
            this.navigraphStatusLabel.Name = "navigraphStatusLabel";
            this.navigraphStatusLabel.Size = new System.Drawing.Size(83, 13);
            this.navigraphStatusLabel.TabIndex = 1;
            this.navigraphStatusLabel.Text = "Not logged in";
            // 
            // navigraphLoginButton
            // 
            this.navigraphLoginButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.navigraphLoginButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.navigraphLoginButton.Location = new System.Drawing.Point(10, 35);
            this.navigraphLoginButton.Name = "navigraphLoginButton";
            this.navigraphLoginButton.Size = new System.Drawing.Size(210, 23);
            this.navigraphLoginButton.TabIndex = 2;
            this.navigraphLoginButton.Text = "Login";
            this.navigraphLoginButton.UseVisualStyleBackColor = true;
            this.navigraphLoginButton.Click += new System.EventHandler(this.NavigraphLoginButton_Click);
            // 
            // startupGroupBox
            // 
            this.startupGroupBox.Controls.Add(this.simStart);
            this.startupGroupBox.Controls.Add(this.exeXml2024Label);
            this.startupGroupBox.Controls.Add(this.exeXml2024Input);
            this.startupGroupBox.Controls.Add(this.exeXml2020Label);
            this.startupGroupBox.Controls.Add(this.exeXml2020Input);
            this.startupGroupBox.Controls.Add(this.autostart);
            this.startupGroupBox.Controls.Add(this.minimized);
            this.startupGroupBox.ForeColor = System.Drawing.SystemColors.Highlight;
            this.startupGroupBox.Location = new System.Drawing.Point(12, 80);
            this.startupGroupBox.Name = "startupGroupBox";
            this.startupGroupBox.Size = new System.Drawing.Size(230, 130);
            this.startupGroupBox.TabIndex = 50;
            this.startupGroupBox.TabStop = false;
            this.startupGroupBox.Text = "Startup";
            // 
            // idsGroupBox
            // 
            this.idsGroupBox.Controls.Add(this.SimbriefIdInput);
            this.idsGroupBox.Controls.Add(this.simbriefStatusLabel);
            this.idsGroupBox.Controls.Add(this.VatsimCidInput);
            this.idsGroupBox.Controls.Add(this.vatsimStatusLabel);
            this.idsGroupBox.Controls.Add(this.IvaoVidInput);
            this.idsGroupBox.Controls.Add(this.ivaoStatusLabel);
            this.idsGroupBox.ForeColor = System.Drawing.SystemColors.Highlight;
            this.idsGroupBox.Location = new System.Drawing.Point(12, 215);
            this.idsGroupBox.Name = "idsGroupBox";
            this.idsGroupBox.Size = new System.Drawing.Size(230, 100);
            this.idsGroupBox.TabIndex = 51;
            this.idsGroupBox.TabStop = false;
            this.idsGroupBox.Text = "IDs";
            // 
            // cacheGroupBox
            // 
            this.cacheGroupBox.Controls.Add(this.cacheSizeLabel);
            this.cacheGroupBox.Controls.Add(this.maxCacheSizeInput);
            this.cacheGroupBox.Controls.Add(this.clearCacheButton);
            this.cacheGroupBox.ForeColor = System.Drawing.SystemColors.Highlight;
            this.cacheGroupBox.Location = new System.Drawing.Point(12, 320);
            this.cacheGroupBox.Name = "cacheGroupBox";
            this.cacheGroupBox.Size = new System.Drawing.Size(230, 72);
            this.cacheGroupBox.TabIndex = 52;
            this.cacheGroupBox.TabStop = false;
            this.cacheGroupBox.Text = "Cache";
            // 
            // navigraphGroupBox
            // 
            this.navigraphGroupBox.Controls.Add(this.navigraphLabel);
            this.navigraphGroupBox.Controls.Add(this.navigraphStatusLabel);
            this.navigraphGroupBox.Controls.Add(this.navigraphLoginButton);
            this.navigraphGroupBox.ForeColor = System.Drawing.SystemColors.Highlight;
            this.navigraphGroupBox.Location = new System.Drawing.Point(12, 397);
            this.navigraphGroupBox.Name = "navigraphGroupBox";
            this.navigraphGroupBox.Size = new System.Drawing.Size(230, 65);
            this.navigraphGroupBox.TabIndex = 54;
            this.navigraphGroupBox.TabStop = false;
            this.navigraphGroupBox.Text = "Navigraph";
            // 
            // elevationGroupBox
            // 
            this.elevationGroupBox.Controls.Add(this.useSrtmCheckbox);
            this.elevationGroupBox.Controls.Add(this.srtmRegionComboBox);
            this.elevationGroupBox.Controls.Add(this.downloadSrtmButton);
            this.elevationGroupBox.Controls.Add(this.elevationStatusLabel);
            this.elevationGroupBox.ForeColor = System.Drawing.SystemColors.Highlight;
            this.elevationGroupBox.Location = new System.Drawing.Point(248, 175);
            this.elevationGroupBox.Name = "elevationGroupBox";
            this.elevationGroupBox.Size = new System.Drawing.Size(230, 115);
            this.elevationGroupBox.TabIndex = 55;
            this.elevationGroupBox.TabStop = false;
            this.elevationGroupBox.Text = "Elevation Data (SRTM)";
            // 
            // useSrtmCheckbox
            // 
            this.useSrtmCheckbox.AutoSize = true;
            this.useSrtmCheckbox.ForeColor = System.Drawing.SystemColors.ControlText;
            this.useSrtmCheckbox.Location = new System.Drawing.Point(10, 18);
            this.useSrtmCheckbox.Name = "useSrtmCheckbox";
            this.useSrtmCheckbox.Size = new System.Drawing.Size(128, 17);
            this.useSrtmCheckbox.TabIndex = 0;
            this.useSrtmCheckbox.Text = "Use local SRTM data";
            this.useSrtmCheckbox.UseVisualStyleBackColor = true;
            this.useSrtmCheckbox.CheckedChanged += new System.EventHandler(this.UseSrtmCheckbox_CheckedChanged);
            // 
            // srtmRegionComboBox
            // 
            this.srtmRegionComboBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.srtmRegionComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.srtmRegionComboBox.ForeColor = System.Drawing.SystemColors.ControlText;
            this.srtmRegionComboBox.FormattingEnabled = true;
            this.srtmRegionComboBox.Location = new System.Drawing.Point(10, 40);
            this.srtmRegionComboBox.Name = "srtmRegionComboBox";
            this.srtmRegionComboBox.Size = new System.Drawing.Size(210, 21);
            this.srtmRegionComboBox.TabIndex = 3;
            // 
            // downloadSrtmButton
            // 
            this.downloadSrtmButton.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.downloadSrtmButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.downloadSrtmButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.downloadSrtmButton.Location = new System.Drawing.Point(10, 65);
            this.downloadSrtmButton.Name = "downloadSrtmButton";
            this.downloadSrtmButton.Size = new System.Drawing.Size(210, 23);
            this.downloadSrtmButton.TabIndex = 1;
            this.downloadSrtmButton.Text = "Download";
            this.downloadSrtmButton.UseVisualStyleBackColor = true;
            this.downloadSrtmButton.Click += new System.EventHandler(this.DownloadSrtmButton_Click);
            // 
            // elevationStatusLabel
            // 
            this.elevationStatusLabel.AutoSize = true;
            this.elevationStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.elevationStatusLabel.ForeColor = System.Drawing.Color.Gray;
            this.elevationStatusLabel.Location = new System.Drawing.Point(10, 92);
            this.elevationStatusLabel.Name = "elevationStatusLabel";
            this.elevationStatusLabel.Size = new System.Drawing.Size(34, 13);
            this.elevationStatusLabel.TabIndex = 2;
            this.elevationStatusLabel.Text = "0 files";
            // 
            // panelGroupBox
            // 
            this.panelGroupBox.Controls.Add(this.panelStatusTitleLabel);
            this.panelGroupBox.Controls.Add(this.panelStatusLabel);
            this.panelGroupBox.Controls.Add(this.panelPathLabel);
            this.panelGroupBox.Controls.Add(this.installPanelButton);
            this.panelGroupBox.ForeColor = System.Drawing.SystemColors.Highlight;
            this.panelGroupBox.Location = new System.Drawing.Point(248, 80);
            this.panelGroupBox.Name = "panelGroupBox";
            this.panelGroupBox.Size = new System.Drawing.Size(230, 90);
            this.panelGroupBox.TabIndex = 56;
            this.panelGroupBox.TabStop = false;
            this.panelGroupBox.Text = "MSFS Panel";
            // 
            // panelStatusTitleLabel
            // 
            this.panelStatusTitleLabel.AutoSize = true;
            this.panelStatusTitleLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.panelStatusTitleLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this.panelStatusTitleLabel.Location = new System.Drawing.Point(7, 18);
            this.panelStatusTitleLabel.Name = "panelStatusTitleLabel";
            this.panelStatusTitleLabel.Size = new System.Drawing.Size(40, 13);
            this.panelStatusTitleLabel.TabIndex = 0;
            this.panelStatusTitleLabel.Text = "Status:";
            // 
            // panelStatusLabel
            // 
            this.panelStatusLabel.AutoSize = true;
            this.panelStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            this.panelStatusLabel.ForeColor = System.Drawing.Color.Gray;
            this.panelStatusLabel.Location = new System.Drawing.Point(50, 18);
            this.panelStatusLabel.Name = "panelStatusLabel";
            this.panelStatusLabel.Size = new System.Drawing.Size(72, 13);
            this.panelStatusLabel.TabIndex = 1;
            this.panelStatusLabel.Text = "Checking...";
            // 
            // panelPathLabel
            // 
            this.panelPathLabel.Font = new System.Drawing.Font("Segoe UI", 7F);
            this.panelPathLabel.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.panelPathLabel.Location = new System.Drawing.Point(7, 35);
            this.panelPathLabel.Name = "panelPathLabel";
            this.panelPathLabel.Size = new System.Drawing.Size(216, 20);
            this.panelPathLabel.TabIndex = 2;
            this.panelPathLabel.Text = "---";
            // 
            // installPanelButton
            // 
            this.installPanelButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.installPanelButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.installPanelButton.Location = new System.Drawing.Point(10, 58);
            this.installPanelButton.Name = "installPanelButton";
            this.installPanelButton.Size = new System.Drawing.Size(210, 23);
            this.installPanelButton.TabIndex = 3;
            this.installPanelButton.Text = "Install Panel";
            this.installPanelButton.UseVisualStyleBackColor = true;
            this.installPanelButton.Click += new System.EventHandler(this.InstallPanelButton_Click);
            // 
            // serialGroupBox
            // 
            this.serialGroupBox.Controls.Add(this.serialNumberInput);
            this.serialGroupBox.Controls.Add(this.serialStatusLabel);
            this.serialGroupBox.ForeColor = System.Drawing.SystemColors.Highlight;
            this.serialGroupBox.Location = new System.Drawing.Point(248, 295);
            this.serialGroupBox.Name = "serialGroupBox";
            this.serialGroupBox.Size = new System.Drawing.Size(230, 45);
            this.serialGroupBox.TabIndex = 58;
            this.serialGroupBox.TabStop = false;
            this.serialGroupBox.Text = "License Key";
            // 
            // serialNumberInput
            // 
            this.serialNumberInput.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.serialNumberInput.ForeColor = System.Drawing.SystemColors.ControlText;
            this.serialNumberInput.Location = new System.Drawing.Point(10, 19);
            this.serialNumberInput.Name = "serialNumberInput";
            this.serialNumberInput.Size = new System.Drawing.Size(170, 20);
            this.serialNumberInput.TabIndex = 1;
            this.serialNumberInput.TextChanged += new System.EventHandler(this.SerialNumberInput_TextChanged);
            // 
            // serialStatusLabel
            // 
            this.serialStatusLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold);
            this.serialStatusLabel.ForeColor = System.Drawing.Color.Gray;
            this.serialStatusLabel.Location = new System.Drawing.Point(185, 22);
            this.serialStatusLabel.Name = "serialStatusLabel";
            this.serialStatusLabel.Size = new System.Drawing.Size(40, 13);
            this.serialStatusLabel.TabIndex = 2;
            this.serialStatusLabel.Text = "---";
            // 
            // updateGroupBox
            // 
            this.updateGroupBox.Controls.Add(this.autoUpdateCheckbox);
            this.updateGroupBox.Controls.Add(this.checkForUpdatesButton);
            this.updateGroupBox.ForeColor = System.Drawing.SystemColors.Highlight;
            this.updateGroupBox.Location = new System.Drawing.Point(248, 345);
            this.updateGroupBox.Name = "updateGroupBox";
            this.updateGroupBox.Size = new System.Drawing.Size(230, 65);
            this.updateGroupBox.TabIndex = 59;
            this.updateGroupBox.TabStop = false;
            this.updateGroupBox.Text = "Updates";
            // 
            // autoUpdateCheckbox
            // 
            this.autoUpdateCheckbox.AutoSize = true;
            this.autoUpdateCheckbox.Checked = true;
            this.autoUpdateCheckbox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.autoUpdateCheckbox.ForeColor = System.Drawing.SystemColors.ControlText;
            this.autoUpdateCheckbox.Location = new System.Drawing.Point(10, 19);
            this.autoUpdateCheckbox.Name = "autoUpdateCheckbox";
            this.autoUpdateCheckbox.Size = new System.Drawing.Size(122, 17);
            this.autoUpdateCheckbox.TabIndex = 0;
            this.autoUpdateCheckbox.Text = "Auto-check updates";
            this.autoUpdateCheckbox.UseVisualStyleBackColor = true;
            this.autoUpdateCheckbox.CheckedChanged += new System.EventHandler(this.AutoUpdateCheckbox_CheckedChanged);
            // 
            // checkForUpdatesButton
            // 
            this.checkForUpdatesButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.checkForUpdatesButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.checkForUpdatesButton.Location = new System.Drawing.Point(10, 39);
            this.checkForUpdatesButton.Name = "checkForUpdatesButton";
            this.checkForUpdatesButton.Size = new System.Drawing.Size(210, 23);
            this.checkForUpdatesButton.TabIndex = 1;
            this.checkForUpdatesButton.Text = "Check for updates";
            this.checkForUpdatesButton.UseVisualStyleBackColor = true;
            this.checkForUpdatesButton.Click += new System.EventHandler(this.CheckForUpdatesButton_Click);
            // 
            // supportLink
            // 
            this.supportLink.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
            this.supportLink.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.supportLink.LinkColor = System.Drawing.SystemColors.Highlight;
            this.supportLink.Location = new System.Drawing.Point(8, 467);
            this.supportLink.Name = "supportLink";
            this.supportLink.Size = new System.Drawing.Size(470, 30);
            this.supportLink.TabIndex = 57;
            this.supportLink.TabStop = true;
            this.supportLink.Text = "♥ Support this project";
            this.supportLink.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.supportLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.SupportLink_LinkClicked);
            // 
            // InformationForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.ClientSize = new System.Drawing.Size(490, 538);
            this.Controls.Add(this.closeButton);
            this.Controls.Add(this.updateGroupBox);
            this.Controls.Add(this.serialGroupBox);
            this.Controls.Add(this.elevationGroupBox);
            this.Controls.Add(this.supportLink);
            this.Controls.Add(this.panelGroupBox);
            this.Controls.Add(this.navigraphGroupBox);
            this.Controls.Add(this.cacheGroupBox);
            this.Controls.Add(this.idsGroupBox);
            this.Controls.Add(this.startupGroupBox);
            this.Controls.Add(this.linkLabel1);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.Version);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "InformationForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Information";
            this.Load += new System.EventHandler(this.InformationForm_Load);
            this.startupGroupBox.ResumeLayout(false);
            this.startupGroupBox.PerformLayout();
            this.idsGroupBox.ResumeLayout(false);
            this.idsGroupBox.PerformLayout();
            this.cacheGroupBox.ResumeLayout(false);
            this.cacheGroupBox.PerformLayout();
            this.navigraphGroupBox.ResumeLayout(false);
            this.navigraphGroupBox.PerformLayout();
            this.elevationGroupBox.ResumeLayout(false);
            this.elevationGroupBox.PerformLayout();
            this.panelGroupBox.ResumeLayout(false);
            this.panelGroupBox.PerformLayout();
            this.serialGroupBox.ResumeLayout(false);
            this.serialGroupBox.PerformLayout();
            this.updateGroupBox.ResumeLayout(false);
            this.updateGroupBox.PerformLayout();
            this.ResumeLayout(false);

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
        private System.Windows.Forms.Label exeXml2024Label;
        private System.Windows.Forms.TextBox exeXml2024Input;
        private System.Windows.Forms.Label exeXml2020Label;
        private System.Windows.Forms.TextBox exeXml2020Input;
        private System.Windows.Forms.TextBox SimbriefIdInput;
        private System.Windows.Forms.TextBox VatsimCidInput;
        private System.Windows.Forms.TextBox IvaoVidInput;
        private System.Windows.Forms.Button clearCacheButton;
        private System.Windows.Forms.TextBox maxCacheSizeInput;
        private System.Windows.Forms.Label cacheSizeLabel;
        private System.Windows.Forms.Label navigraphLabel;
        private System.Windows.Forms.Label navigraphStatusLabel;
        private System.Windows.Forms.Button navigraphLoginButton;
        private System.Windows.Forms.GroupBox startupGroupBox;
        private System.Windows.Forms.GroupBox idsGroupBox;
        private System.Windows.Forms.GroupBox cacheGroupBox;
        private System.Windows.Forms.GroupBox navigraphGroupBox;
        private System.Windows.Forms.GroupBox elevationGroupBox;
        private System.Windows.Forms.CheckBox useSrtmCheckbox;
        private System.Windows.Forms.Button downloadSrtmButton;
        private System.Windows.Forms.Label elevationStatusLabel;
        private System.Windows.Forms.ComboBox srtmRegionComboBox;
        private System.Windows.Forms.GroupBox panelGroupBox;
        private System.Windows.Forms.Label panelStatusTitleLabel;
        private System.Windows.Forms.Label panelStatusLabel;
        private System.Windows.Forms.Label panelPathLabel;
        private System.Windows.Forms.Button installPanelButton;
        private System.Windows.Forms.LinkLabel supportLink;
        private System.Windows.Forms.GroupBox serialGroupBox;
        private System.Windows.Forms.TextBox serialNumberInput;
        private System.Windows.Forms.Label serialStatusLabel;
        private System.Windows.Forms.GroupBox updateGroupBox;
        private System.Windows.Forms.CheckBox autoUpdateCheckbox;
        private System.Windows.Forms.Button checkForUpdatesButton;
        private System.Windows.Forms.Label simbriefStatusLabel;
        private System.Windows.Forms.Label vatsimStatusLabel;
        private System.Windows.Forms.Label ivaoStatusLabel;
    }
}
