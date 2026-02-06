
namespace Kneeboard_Server
{
    partial class SpendenForm
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
            this.titleLabel = new System.Windows.Forms.Label();
            this.versionLabel = new System.Windows.Forms.Label();
            this.supportTextLabel = new System.Windows.Forms.Label();
            this.qrPictureBox = new System.Windows.Forms.PictureBox();
            this.donateLink = new System.Windows.Forms.LinkLabel();
            this.footerLabel = new System.Windows.Forms.Label();
            this.footerEmailLabel = new System.Windows.Forms.LinkLabel();
            this.closeBigButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.qrPictureBox)).BeginInit();
            this.SuspendLayout();
            //
            // closeButton
            //
            this.closeButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.closeButton.BackColor = System.Drawing.SystemColors.Window;
            this.closeButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.closeButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.closeButton.Location = new System.Drawing.Point(301, 8);
            this.closeButton.Name = "closeButton";
            this.closeButton.Size = new System.Drawing.Size(23, 23);
            this.closeButton.TabIndex = 24;
            this.closeButton.Text = "X";
            this.closeButton.UseVisualStyleBackColor = false;
            this.closeButton.Click += new System.EventHandler(this.CloseButton_Click);
            //
            // titleLabel
            //
            this.titleLabel.Font = new System.Drawing.Font("Segoe UI", 14.25F, System.Drawing.FontStyle.Bold);
            this.titleLabel.ForeColor = System.Drawing.SystemColors.Highlight;
            this.titleLabel.Location = new System.Drawing.Point(12, 8);
            this.titleLabel.Name = "titleLabel";
            this.titleLabel.Size = new System.Drawing.Size(280, 25);
            this.titleLabel.TabIndex = 25;
            this.titleLabel.Text = "Support gsim Kneeboard";
            this.titleLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // versionLabel
            //
            this.versionLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.versionLabel.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.versionLabel.Location = new System.Drawing.Point(12, 33);
            this.versionLabel.Name = "versionLabel";
            this.versionLabel.Size = new System.Drawing.Size(280, 16);
            this.versionLabel.TabIndex = 26;
            this.versionLabel.Text = "Version: 2.0.0.0";
            this.versionLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // supportTextLabel
            //
            this.supportTextLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.supportTextLabel.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.supportTextLabel.Location = new System.Drawing.Point(12, 58);
            this.supportTextLabel.Name = "supportTextLabel";
            this.supportTextLabel.Size = new System.Drawing.Size(310, 130);
            this.supportTextLabel.TabIndex = 27;
            this.supportTextLabel.Text = "gsim Kneeboard is a free project built with passion in my spare time.\r\n\r\nHowever," +
                " server hosting, the website, and ongoing development come with real costs.\r\n\r\nYo" +
                "ur donation helps keep this project alive and enables new features. Every contrib" +
                "ution \u2013 no matter how small \u2013 goes directly into the project and is greatly apprec" +
                "iated.\r\n\r\nThank you for your support!";
            //
            // qrPictureBox
            //
            this.qrPictureBox.Location = new System.Drawing.Point(110, 195);
            this.qrPictureBox.Name = "qrPictureBox";
            this.qrPictureBox.Size = new System.Drawing.Size(115, 115);
            this.qrPictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.qrPictureBox.TabIndex = 28;
            this.qrPictureBox.TabStop = false;
            //
            // donateLink
            //
            this.donateLink.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            this.donateLink.LinkColor = System.Drawing.SystemColors.Highlight;
            this.donateLink.Location = new System.Drawing.Point(12, 316);
            this.donateLink.Name = "donateLink";
            this.donateLink.Size = new System.Drawing.Size(310, 23);
            this.donateLink.TabIndex = 29;
            this.donateLink.TabStop = true;
            this.donateLink.Text = "Donate via PayPal";
            this.donateLink.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.donateLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.DonateLink_LinkClicked);
            //
            // footerLabel
            //
            this.footerLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.footerLabel.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.footerLabel.Location = new System.Drawing.Point(12, 350);
            this.footerLabel.Name = "footerLabel";
            this.footerLabel.Size = new System.Drawing.Size(310, 16);
            this.footerLabel.TabIndex = 30;
            this.footerLabel.Text = "Gsimulations - 2021";
            this.footerLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // footerEmailLabel
            //
            this.footerEmailLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.footerEmailLabel.Location = new System.Drawing.Point(12, 366);
            this.footerEmailLabel.Name = "footerEmailLabel";
            this.footerEmailLabel.Size = new System.Drawing.Size(310, 16);
            this.footerEmailLabel.TabIndex = 31;
            this.footerEmailLabel.TabStop = true;
            this.footerEmailLabel.Text = "support@gsimulations.com";
            this.footerEmailLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.footerEmailLabel.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.FooterEmailLabel_LinkClicked);
            //
            // closeBigButton
            //
            this.closeBigButton.BackColor = System.Drawing.SystemColors.Window;
            this.closeBigButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.closeBigButton.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold);
            this.closeBigButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.closeBigButton.Location = new System.Drawing.Point(108, 392);
            this.closeBigButton.Name = "closeBigButton";
            this.closeBigButton.Size = new System.Drawing.Size(120, 30);
            this.closeBigButton.TabIndex = 32;
            this.closeBigButton.Text = "Schließen";
            this.closeBigButton.UseVisualStyleBackColor = false;
            this.closeBigButton.Click += new System.EventHandler(this.CloseButton_Click);
            //
            // SpendenForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.ClientSize = new System.Drawing.Size(335, 435);
            this.Controls.Add(this.closeBigButton);
            this.Controls.Add(this.footerEmailLabel);
            this.Controls.Add(this.footerLabel);
            this.Controls.Add(this.donateLink);
            this.Controls.Add(this.qrPictureBox);
            this.Controls.Add(this.supportTextLabel);
            this.Controls.Add(this.versionLabel);
            this.Controls.Add(this.titleLabel);
            this.Controls.Add(this.closeButton);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "SpendenForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Support";
            this.Load += new System.EventHandler(this.SpendenForm_Load);
            this.Paint += new System.Windows.Forms.PaintEventHandler(this.SpendenForm_Paint);
            ((System.ComponentModel.ISupportInitialize)(this.qrPictureBox)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button closeButton;
        private System.Windows.Forms.Label titleLabel;
        private System.Windows.Forms.Label versionLabel;
        private System.Windows.Forms.Label supportTextLabel;
        private System.Windows.Forms.PictureBox qrPictureBox;
        private System.Windows.Forms.LinkLabel donateLink;
        private System.Windows.Forms.Label footerLabel;
        private System.Windows.Forms.LinkLabel footerEmailLabel;
        private System.Windows.Forms.Button closeBigButton;
    }
}
