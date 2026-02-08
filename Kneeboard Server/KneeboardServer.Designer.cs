
namespace Kneeboard_Server
{
    partial class Kneeboard_Server
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Kneeboard_Server));
            this.minimizeButton = new System.Windows.Forms.Label();
            this.maximizeButton = new System.Windows.Forms.Label();
            this.closeButton = new System.Windows.Forms.Button();
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this.label3 = new System.Windows.Forms.Label();
            this.information = new System.Windows.Forms.Label();
            this.MyToolTip = new System.Windows.Forms.ToolTip(this.components);
            this.label2 = new System.Windows.Forms.Label();
            this.panel1 = new System.Windows.Forms.Panel();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.UpdateMessage = new System.Windows.Forms.TextBox();
            this.deleteFolderButton = new System.Windows.Forms.Button();
            this.addFolderButton = new System.Windows.Forms.Button();
            this.loadButton = new System.Windows.Forms.Button();
            this.editButton = new System.Windows.Forms.Button();
            this.saveButton = new System.Windows.Forms.Button();
            this.deleteFileButton = new System.Windows.Forms.Button();
            this.addFileButton = new System.Windows.Forms.Button();
            this.statusBox = new System.Windows.Forms.TextBox();
            this.navdataProgressLabel = new System.Windows.Forms.Label();
            this.navdataProgressBar = new System.Windows.Forms.ProgressBar();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.showToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.exitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.notifyIcon = new System.Windows.Forms.NotifyIcon(this.components);
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.label1 = new System.Windows.Forms.Label();
            this.panel1.SuspendLayout();
            this.contextMenuStrip1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // minimizeButton
            // 
            this.minimizeButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.minimizeButton.AutoSize = true;
            this.minimizeButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.minimizeButton.Location = new System.Drawing.Point(367, 17);
            this.minimizeButton.Name = "minimizeButton";
            this.minimizeButton.Size = new System.Drawing.Size(13, 13);
            this.minimizeButton.TabIndex = 25;
            this.minimizeButton.Text = "_";
            this.minimizeButton.Click += new System.EventHandler(this.Minimize_Click);
            this.minimizeButton.MouseHover += new System.EventHandler(this.MinimizeButton_MouseHover);
            // 
            // maximizeButton
            // 
            this.maximizeButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.maximizeButton.AutoSize = true;
            this.maximizeButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.maximizeButton.Location = new System.Drawing.Point(386, 17);
            this.maximizeButton.Name = "maximizeButton";
            this.maximizeButton.Size = new System.Drawing.Size(13, 13);
            this.maximizeButton.TabIndex = 24;
            this.maximizeButton.Text = "+";
            this.maximizeButton.Click += new System.EventHandler(this.Maximize_Click);
            this.maximizeButton.MouseHover += new System.EventHandler(this.MaximizeButton_MouseHover);
            // 
            // closeButton
            // 
            this.closeButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.closeButton.BackColor = System.Drawing.SystemColors.Window;
            this.closeButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.closeButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.closeButton.Location = new System.Drawing.Point(405, 12);
            this.closeButton.Name = "closeButton";
            this.closeButton.Size = new System.Drawing.Size(23, 23);
            this.closeButton.TabIndex = 23;
            this.closeButton.Text = "X";
            this.closeButton.UseVisualStyleBackColor = false;
            this.closeButton.Click += new System.EventHandler(this.Close_Click);
            this.closeButton.MouseHover += new System.EventHandler(this.CloseButton_MouseHover);
            // 
            // folderBrowserDialog1
            // 
            this.folderBrowserDialog1.RootFolder = System.Environment.SpecialFolder.ProgramFilesX86;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.ForeColor = System.Drawing.SystemColors.Highlight;
            this.label3.Location = new System.Drawing.Point(36, 9);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(137, 20);
            this.label3.TabIndex = 30;
            this.label3.Text = "Kneeboard Server";
            // 
            // information
            // 
            this.information.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.information.AutoSize = true;
            this.information.Cursor = System.Windows.Forms.Cursors.Hand;
            this.information.Font = new System.Drawing.Font("Segoe MDL2 Assets", 8.25F);
            this.information.ForeColor = System.Drawing.SystemColors.Highlight;
            this.information.Location = new System.Drawing.Point(330, 17);
            this.information.Name = "information";
            this.information.Size = new System.Drawing.Size(16, 11);
            this.information.TabIndex = 37;
            this.information.Text = "";
            this.information.Click += new System.EventHandler(this.Information_Click);
            this.information.MouseHover += new System.EventHandler(this.Information_MouseHover);
            // 
            // label2
            // 
            this.label2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.label2.AutoSize = true;
            this.label2.ForeColor = System.Drawing.SystemColors.Highlight;
            this.label2.Location = new System.Drawing.Point(349, 17);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(13, 13);
            this.label2.TabIndex = 40;
            this.label2.Text = "?";
            this.MyToolTip.SetToolTip(this.label2, "manual");
            this.label2.Click += new System.EventHandler(this.label2_Click);
            // 
            // panel1
            // 
            this.panel1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panel1.Controls.Add(this.treeView1);
            this.panel1.Controls.Add(this.UpdateMessage);
            this.panel1.Controls.Add(this.deleteFolderButton);
            this.panel1.Controls.Add(this.addFolderButton);
            this.panel1.Controls.Add(this.loadButton);
            this.panel1.Controls.Add(this.editButton);
            this.panel1.Controls.Add(this.saveButton);
            this.panel1.Controls.Add(this.deleteFileButton);
            this.panel1.Controls.Add(this.addFileButton);
            this.panel1.Controls.Add(this.statusBox);
            this.panel1.Controls.Add(this.navdataProgressLabel);
            this.panel1.Controls.Add(this.navdataProgressBar);
            this.panel1.Location = new System.Drawing.Point(12, 41);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(416, 274);
            this.panel1.TabIndex = 38;
            // 
            // treeView1
            // 
            this.treeView1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.treeView1.Location = new System.Drawing.Point(0, 53);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(416, 192);
            this.treeView1.TabIndex = 56;
            this.treeView1.DoubleClick += new System.EventHandler(this.treeView1_DoubleClick);
            // 
            // UpdateMessage
            // 
            this.UpdateMessage.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.UpdateMessage.BackColor = System.Drawing.Color.Red;
            this.UpdateMessage.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.UpdateMessage.Cursor = System.Windows.Forms.Cursors.Arrow;
            this.UpdateMessage.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.UpdateMessage.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.UpdateMessage.Location = new System.Drawing.Point(0, 253);
            this.UpdateMessage.Margin = new System.Windows.Forms.Padding(5);
            this.UpdateMessage.Name = "UpdateMessage";
            this.UpdateMessage.Size = new System.Drawing.Size(416, 21);
            this.UpdateMessage.TabIndex = 54;
            this.UpdateMessage.Text = "Update is available!";
            this.UpdateMessage.Click += new System.EventHandler(this.statusBox_Click);
            // 
            // deleteFolderButton
            // 
            this.deleteFolderButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.deleteFolderButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.deleteFolderButton.Image = ((System.Drawing.Image)(resources.GetObject("deleteFolderButton.Image")));
            this.deleteFolderButton.Location = new System.Drawing.Point(55, 0);
            this.deleteFolderButton.Margin = new System.Windows.Forms.Padding(5);
            this.deleteFolderButton.Name = "deleteFolderButton";
            this.deleteFolderButton.Size = new System.Drawing.Size(45, 45);
            this.deleteFolderButton.TabIndex = 53;
            this.deleteFolderButton.UseVisualStyleBackColor = true;
            this.deleteFolderButton.Click += new System.EventHandler(this.deleteFolderButton_Click);
            this.deleteFolderButton.MouseHover += new System.EventHandler(this.DeleteFolderButton_MouseHover);
            // 
            // addFolderButton
            // 
            this.addFolderButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.addFolderButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.addFolderButton.Image = ((System.Drawing.Image)(resources.GetObject("addFolderButton.Image")));
            this.addFolderButton.Location = new System.Drawing.Point(0, 0);
            this.addFolderButton.Margin = new System.Windows.Forms.Padding(5);
            this.addFolderButton.Name = "addFolderButton";
            this.addFolderButton.Size = new System.Drawing.Size(45, 45);
            this.addFolderButton.TabIndex = 52;
            this.addFolderButton.UseVisualStyleBackColor = true;
            this.addFolderButton.Click += new System.EventHandler(this.button1_Click);
            this.addFolderButton.MouseHover += new System.EventHandler(this.AddFolderButton_MouseHover);
            // 
            // loadButton
            // 
            this.loadButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.loadButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.loadButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.loadButton.Image = ((System.Drawing.Image)(resources.GetObject("loadButton.Image")));
            this.loadButton.Location = new System.Drawing.Point(261, 0);
            this.loadButton.Margin = new System.Windows.Forms.Padding(5);
            this.loadButton.Name = "loadButton";
            this.loadButton.Size = new System.Drawing.Size(45, 45);
            this.loadButton.TabIndex = 50;
            this.loadButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.loadButton.UseVisualStyleBackColor = true;
            this.loadButton.Click += new System.EventHandler(this.LoadButton_Click);
            this.loadButton.MouseHover += new System.EventHandler(this.LoadButton_MouseHover);
            // 
            // editButton
            // 
            this.editButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.editButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.editButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.editButton.Image = ((System.Drawing.Image)(resources.GetObject("editButton.Image")));
            this.editButton.Location = new System.Drawing.Point(371, 0);
            this.editButton.Margin = new System.Windows.Forms.Padding(5);
            this.editButton.Name = "editButton";
            this.editButton.Size = new System.Drawing.Size(45, 45);
            this.editButton.TabIndex = 49;
            this.editButton.UseVisualStyleBackColor = true;
            this.editButton.Click += new System.EventHandler(this.EditButton_Click);
            this.editButton.MouseHover += new System.EventHandler(this.EditButton_MouseHover);
            // 
            // saveButton
            // 
            this.saveButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.saveButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.saveButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.saveButton.Image = ((System.Drawing.Image)(resources.GetObject("saveButton.Image")));
            this.saveButton.Location = new System.Drawing.Point(316, 0);
            this.saveButton.Margin = new System.Windows.Forms.Padding(5);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(45, 45);
            this.saveButton.TabIndex = 48;
            this.saveButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.saveButton.UseVisualStyleBackColor = true;
            this.saveButton.Click += new System.EventHandler(this.SaveButton_Click);
            this.saveButton.MouseHover += new System.EventHandler(this.SaveButton_MouseHover);
            // 
            // deleteFileButton
            // 
            this.deleteFileButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.deleteFileButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.deleteFileButton.Image = global::Kneeboard_Server.Properties.Resources.delete_file;
            this.deleteFileButton.Location = new System.Drawing.Point(165, 0);
            this.deleteFileButton.Margin = new System.Windows.Forms.Padding(5);
            this.deleteFileButton.Name = "deleteFileButton";
            this.deleteFileButton.Size = new System.Drawing.Size(45, 45);
            this.deleteFileButton.TabIndex = 46;
            this.deleteFileButton.UseVisualStyleBackColor = true;
            this.deleteFileButton.Click += new System.EventHandler(this.DeleteFileButton_Click);
            this.deleteFileButton.MouseHover += new System.EventHandler(this.DeleteFileButton_MouseHover);
            // 
            // addFileButton
            // 
            this.addFileButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.addFileButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.addFileButton.Image = global::Kneeboard_Server.Properties.Resources.upload_file;
            this.addFileButton.Location = new System.Drawing.Point(110, 0);
            this.addFileButton.Margin = new System.Windows.Forms.Padding(5);
            this.addFileButton.Name = "addFileButton";
            this.addFileButton.Size = new System.Drawing.Size(45, 45);
            this.addFileButton.TabIndex = 45;
            this.addFileButton.UseVisualStyleBackColor = true;
            this.addFileButton.Click += new System.EventHandler(this.AddFileButton_Click);
            this.addFileButton.MouseHover += new System.EventHandler(this.AddFileButton_MouseHover);
            // 
            // statusBox
            // 
            this.statusBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.statusBox.BackColor = System.Drawing.SystemColors.MenuHighlight;
            this.statusBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.statusBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.statusBox.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.statusBox.Location = new System.Drawing.Point(0, 253);
            this.statusBox.Margin = new System.Windows.Forms.Padding(5);
            this.statusBox.Name = "statusBox";
            this.statusBox.Size = new System.Drawing.Size(416, 21);
            this.statusBox.TabIndex = 44;
            this.statusBox.Text = "Status: Server is not running! Plese select a working folder.";
            // 
            // navdataProgressLabel
            // 
            this.navdataProgressLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.navdataProgressLabel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(120)))), ((int)(((byte)(215)))));
            this.navdataProgressLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.navdataProgressLabel.ForeColor = System.Drawing.Color.White;
            this.navdataProgressLabel.Location = new System.Drawing.Point(0, 253);
            this.navdataProgressLabel.Name = "navdataProgressLabel";
            this.navdataProgressLabel.Size = new System.Drawing.Size(416, 21);
            this.navdataProgressLabel.TabIndex = 56;
            this.navdataProgressLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.navdataProgressLabel.Visible = false;
            // 
            // navdataProgressBar
            // 
            this.navdataProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.navdataProgressBar.Location = new System.Drawing.Point(0, 253);
            this.navdataProgressBar.Name = "navdataProgressBar";
            this.navdataProgressBar.Size = new System.Drawing.Size(416, 21);
            this.navdataProgressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;
            this.navdataProgressBar.TabIndex = 55;
            this.navdataProgressBar.Visible = false;
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.showToolStripMenuItem,
            this.exitToolStripMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(205, 48);
            // 
            // showToolStripMenuItem
            // 
            this.showToolStripMenuItem.Name = "showToolStripMenuItem";
            this.showToolStripMenuItem.Size = new System.Drawing.Size(204, 22);
            this.showToolStripMenuItem.Text = "showToolStripMenuItem";
            // 
            // exitToolStripMenuItem
            // 
            this.exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            this.exitToolStripMenuItem.Size = new System.Drawing.Size(204, 22);
            this.exitToolStripMenuItem.Text = "exitToolStripMenuItem";
            // 
            // notifyIcon
            // 
            this.notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
            this.notifyIcon.BalloonTipText = "The application is running in Background";
            this.notifyIcon.BalloonTipTitle = "Kneeboard Server";
            this.notifyIcon.Icon = ((System.Drawing.Icon)(resources.GetObject("notifyIcon.Icon")));
            this.notifyIcon.Text = "Kneeboard Server";
            this.notifyIcon.MouseClick += new System.Windows.Forms.MouseEventHandler(this.notifyIcon_MouseClick);
            // 
            // pictureBox1
            // 
            this.pictureBox1.Enabled = false;
            this.pictureBox1.Image = ((System.Drawing.Image)(resources.GetObject("pictureBox1.Image")));
            this.pictureBox1.InitialImage = ((System.Drawing.Image)(resources.GetObject("pictureBox1.InitialImage")));
            this.pictureBox1.Location = new System.Drawing.Point(12, 8);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(18, 22);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 31;
            this.pictureBox1.TabStop = false;
            // 
            // label1
            // 
            this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Wingdings 3", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(2)));
            this.label1.ForeColor = System.Drawing.SystemColors.Highlight;
            this.label1.Location = new System.Drawing.Point(313, 18);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(15, 12);
            this.label1.TabIndex = 39;
            this.label1.Text = "P";
            this.label1.Click += new System.EventHandler(this.label1_Click_1);
            this.label1.MouseHover += new System.EventHandler(this.label1_MouseHover);
            // 
            // Kneeboard_Server
            // 
            this.AccessibleName = "";
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.ClientSize = new System.Drawing.Size(440, 327);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.panel1);
            this.Controls.Add(this.information);
            this.Controls.Add(this.minimizeButton);
            this.Controls.Add(this.maximizeButton);
            this.Controls.Add(this.closeButton);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this.label3);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MinimumSize = new System.Drawing.Size(440, 327);
            this.Name = "Kneeboard_Server";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Show;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "s";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.KneeboardServer_FormClosing);
            this.Load += new System.EventHandler(this.KneeboardServer_Load);
            this.SizeChanged += new System.EventHandler(this.Kneeboard_Server_SizeChanged);
            this.MouseDoubleClick += new System.Windows.Forms.MouseEventHandler(this.Kneeboard_Server_MouseDoubleClick);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.KneeboardServer_MouseDown);
            this.MouseMove += new System.Windows.Forms.MouseEventHandler(this.KneeboardServer_MouseMove);
            this.MouseUp += new System.Windows.Forms.MouseEventHandler(this.KneeboardServer_MouseUp);
            this.panel1.ResumeLayout(false);
            this.panel1.PerformLayout();
            this.contextMenuStrip1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Label minimizeButton;
        private System.Windows.Forms.Label maximizeButton;
        private System.Windows.Forms.Button closeButton;
        public System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label information;
        private System.Windows.Forms.ToolTip MyToolTip;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Button loadButton;
        private System.Windows.Forms.Button editButton;
        private System.Windows.Forms.Button saveButton;
        private System.Windows.Forms.Button deleteFileButton;
        private System.Windows.Forms.Button addFileButton;
        private System.Windows.Forms.TextBox statusBox;
        private System.Windows.Forms.ProgressBar navdataProgressBar;
        private System.Windows.Forms.Label navdataProgressLabel;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem showToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem exitToolStripMenuItem;
        private System.Windows.Forms.NotifyIcon notifyIcon;
        private System.Windows.Forms.Button addFolderButton;
        private System.Windows.Forms.Button deleteFolderButton;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox UpdateMessage;
        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.Label label2;
    }
}