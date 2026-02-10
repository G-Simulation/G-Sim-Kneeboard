
namespace Kneeboard_Server
{
    partial class SetupWizardForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.titleLabel = new System.Windows.Forms.Label();
            this.stepLabel = new System.Windows.Forms.Label();
            this.nextButton = new System.Windows.Forms.Button();
            this.backButton = new System.Windows.Forms.Button();
            this.skipButton = new System.Windows.Forms.Button();

            // ── Step 1: MSFS Version ──
            this.stepMsfsVersion = new System.Windows.Forms.Panel();
            this.msfsVersionLabel = new System.Windows.Forms.Label();
            this.cb2024 = new System.Windows.Forms.CheckBox();
            this.cb2020 = new System.Windows.Forms.CheckBox();

            // ── Step 2: Autostart & exe.xml ──
            this.stepAutostart = new System.Windows.Forms.Panel();
            this.autostartGroupBox = new System.Windows.Forms.GroupBox();
            this.simStartCheckbox = new System.Windows.Forms.CheckBox();
            this.exeXml2024Label = new System.Windows.Forms.Label();
            this.exeXml2024Input = new System.Windows.Forms.TextBox();
            this.exeXml2020Label = new System.Windows.Forms.Label();
            this.exeXml2020Input = new System.Windows.Forms.TextBox();
            this.minimizedCheckbox = new System.Windows.Forms.CheckBox();

            // ── Step 3: SimBrief & Netzwerke ──
            this.stepIds = new System.Windows.Forms.Panel();
            this.simbriefGroupBox = new System.Windows.Forms.GroupBox();
            this.simbriefLabel = new System.Windows.Forms.Label();
            this.simbriefInput = new System.Windows.Forms.TextBox();
            this.networksGroupBox = new System.Windows.Forms.GroupBox();
            this.vatsimLabel = new System.Windows.Forms.Label();
            this.vatsimCidInput = new System.Windows.Forms.TextBox();
            this.ivaoLabel = new System.Windows.Forms.Label();
            this.ivaoVidInput = new System.Windows.Forms.TextBox();

            // ── Step 4: Panel Installation ──
            this.stepPanel = new System.Windows.Forms.Panel();
            this.panelGroupBox = new System.Windows.Forms.GroupBox();

            this.SuspendLayout();
            this.stepMsfsVersion.SuspendLayout();
            this.stepAutostart.SuspendLayout();
            this.autostartGroupBox.SuspendLayout();
            this.stepIds.SuspendLayout();
            this.simbriefGroupBox.SuspendLayout();
            this.networksGroupBox.SuspendLayout();
            this.stepPanel.SuspendLayout();

            //
            // titleLabel
            //
            this.titleLabel.Font = new System.Drawing.Font("Segoe UI", 14.25F, System.Drawing.FontStyle.Bold);
            this.titleLabel.ForeColor = System.Drawing.SystemColors.Highlight;
            this.titleLabel.Location = new System.Drawing.Point(12, 8);
            this.titleLabel.Name = "titleLabel";
            this.titleLabel.Size = new System.Drawing.Size(440, 28);
            this.titleLabel.TabIndex = 0;
            this.titleLabel.Text = "Kneeboard Server Setup";
            //
            // stepLabel
            //
            this.stepLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.stepLabel.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.stepLabel.Location = new System.Drawing.Point(12, 38);
            this.stepLabel.Name = "stepLabel";
            this.stepLabel.Size = new System.Drawing.Size(440, 18);
            this.stepLabel.TabIndex = 1;
            this.stepLabel.Text = "Step 1 of 4";

            // ═══════════════════════════════════════════════════
            // Step 1: MSFS Version
            // ═══════════════════════════════════════════════════
            this.stepMsfsVersion.Location = new System.Drawing.Point(0, 62);
            this.stepMsfsVersion.Name = "stepMsfsVersion";
            this.stepMsfsVersion.Size = new System.Drawing.Size(470, 180);
            //
            // msfsVersionLabel
            //
            this.msfsVersionLabel.Font = new System.Drawing.Font("Segoe UI", 9.5F);
            this.msfsVersionLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this.msfsVersionLabel.Location = new System.Drawing.Point(12, 8);
            this.msfsVersionLabel.Name = "msfsVersionLabel";
            this.msfsVersionLabel.Size = new System.Drawing.Size(446, 40);
            this.msfsVersionLabel.Text = "Which MSFS version(s) do you use?";
            //
            // cb2024
            //
            this.cb2024.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.cb2024.ForeColor = System.Drawing.SystemColors.ControlText;
            this.cb2024.Location = new System.Drawing.Point(30, 55);
            this.cb2024.Name = "cb2024";
            this.cb2024.Size = new System.Drawing.Size(400, 28);
            this.cb2024.TabIndex = 0;
            this.cb2024.Text = "Microsoft Flight Simulator 2024";
            this.cb2024.UseVisualStyleBackColor = true;
            //
            // cb2020
            //
            this.cb2020.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.cb2020.ForeColor = System.Drawing.SystemColors.ControlText;
            this.cb2020.Location = new System.Drawing.Point(30, 90);
            this.cb2020.Name = "cb2020";
            this.cb2020.Size = new System.Drawing.Size(400, 28);
            this.cb2020.TabIndex = 1;
            this.cb2020.Text = "Microsoft Flight Simulator 2020";
            this.cb2020.UseVisualStyleBackColor = true;
            //
            this.stepMsfsVersion.Controls.Add(this.msfsVersionLabel);
            this.stepMsfsVersion.Controls.Add(this.cb2024);
            this.stepMsfsVersion.Controls.Add(this.cb2020);

            // ═══════════════════════════════════════════════════
            // Step 2: Autostart & exe.xml
            // ═══════════════════════════════════════════════════
            this.stepAutostart.Location = new System.Drawing.Point(0, 62);
            this.stepAutostart.Name = "stepAutostart";
            this.stepAutostart.Size = new System.Drawing.Size(470, 180);
            this.stepAutostart.Visible = false;
            //
            // autostartGroupBox
            //
            this.autostartGroupBox.Controls.Add(this.simStartCheckbox);
            this.autostartGroupBox.Controls.Add(this.exeXml2024Label);
            this.autostartGroupBox.Controls.Add(this.exeXml2024Input);
            this.autostartGroupBox.Controls.Add(this.exeXml2020Label);
            this.autostartGroupBox.Controls.Add(this.exeXml2020Input);
            this.autostartGroupBox.Controls.Add(this.minimizedCheckbox);
            this.autostartGroupBox.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.autostartGroupBox.ForeColor = System.Drawing.SystemColors.Highlight;
            this.autostartGroupBox.Location = new System.Drawing.Point(12, 5);
            this.autostartGroupBox.Name = "autostartGroupBox";
            this.autostartGroupBox.Size = new System.Drawing.Size(446, 130);
            this.autostartGroupBox.TabIndex = 0;
            this.autostartGroupBox.TabStop = false;
            this.autostartGroupBox.Text = "Autostart & Startup";
            //
            // simStartCheckbox
            //
            this.simStartCheckbox.Checked = true;
            this.simStartCheckbox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.simStartCheckbox.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.simStartCheckbox.ForeColor = System.Drawing.SystemColors.ControlText;
            this.simStartCheckbox.Location = new System.Drawing.Point(10, 20);
            this.simStartCheckbox.Name = "simStartCheckbox";
            this.simStartCheckbox.Size = new System.Drawing.Size(420, 20);
            this.simStartCheckbox.TabIndex = 0;
            this.simStartCheckbox.Text = "Start Kneeboard Server automatically with MSFS";
            this.simStartCheckbox.UseVisualStyleBackColor = true;
            //
            // exeXml2024Label
            //
            this.exeXml2024Label.AutoSize = true;
            this.exeXml2024Label.Font = new System.Drawing.Font("Segoe UI", 7F);
            this.exeXml2024Label.ForeColor = System.Drawing.SystemColors.ControlText;
            this.exeXml2024Label.Location = new System.Drawing.Point(8, 46);
            this.exeXml2024Label.Name = "exeXml2024Label";
            this.exeXml2024Label.Size = new System.Drawing.Size(27, 12);
            this.exeXml2024Label.TabIndex = 1;
            this.exeXml2024Label.Text = "2024:";
            //
            // exeXml2024Input
            //
            this.exeXml2024Input.Font = new System.Drawing.Font("Segoe UI", 7F);
            this.exeXml2024Input.ForeColor = System.Drawing.SystemColors.GrayText;
            this.exeXml2024Input.Location = new System.Drawing.Point(42, 43);
            this.exeXml2024Input.Name = "exeXml2024Input";
            this.exeXml2024Input.ReadOnly = true;
            this.exeXml2024Input.Size = new System.Drawing.Size(393, 20);
            this.exeXml2024Input.TabIndex = 2;
            this.exeXml2024Input.Text = "Auto-detecting...";
            //
            // exeXml2020Label
            //
            this.exeXml2020Label.AutoSize = true;
            this.exeXml2020Label.Font = new System.Drawing.Font("Segoe UI", 7F);
            this.exeXml2020Label.ForeColor = System.Drawing.SystemColors.ControlText;
            this.exeXml2020Label.Location = new System.Drawing.Point(8, 68);
            this.exeXml2020Label.Name = "exeXml2020Label";
            this.exeXml2020Label.Size = new System.Drawing.Size(27, 12);
            this.exeXml2020Label.TabIndex = 3;
            this.exeXml2020Label.Text = "2020:";
            //
            // exeXml2020Input
            //
            this.exeXml2020Input.Font = new System.Drawing.Font("Segoe UI", 7F);
            this.exeXml2020Input.ForeColor = System.Drawing.SystemColors.GrayText;
            this.exeXml2020Input.Location = new System.Drawing.Point(42, 65);
            this.exeXml2020Input.Name = "exeXml2020Input";
            this.exeXml2020Input.ReadOnly = true;
            this.exeXml2020Input.Size = new System.Drawing.Size(393, 20);
            this.exeXml2020Input.TabIndex = 4;
            this.exeXml2020Input.Text = "Auto-detecting...";
            //
            // minimizedCheckbox
            //
            this.minimizedCheckbox.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.minimizedCheckbox.ForeColor = System.Drawing.SystemColors.ControlText;
            this.minimizedCheckbox.Location = new System.Drawing.Point(10, 95);
            this.minimizedCheckbox.Name = "minimizedCheckbox";
            this.minimizedCheckbox.Size = new System.Drawing.Size(420, 20);
            this.minimizedCheckbox.TabIndex = 5;
            this.minimizedCheckbox.Text = "Start minimized";
            this.minimizedCheckbox.UseVisualStyleBackColor = true;
            //
            this.stepAutostart.Controls.Add(this.autostartGroupBox);

            // ═══════════════════════════════════════════════════
            // Step 3: SimBrief & Netzwerke
            // ═══════════════════════════════════════════════════
            this.stepIds.Location = new System.Drawing.Point(0, 62);
            this.stepIds.Name = "stepIds";
            this.stepIds.Size = new System.Drawing.Size(470, 180);
            this.stepIds.Visible = false;
            //
            // simbriefGroupBox
            //
            this.simbriefGroupBox.Controls.Add(this.simbriefLabel);
            this.simbriefGroupBox.Controls.Add(this.simbriefInput);
            this.simbriefGroupBox.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.simbriefGroupBox.ForeColor = System.Drawing.SystemColors.Highlight;
            this.simbriefGroupBox.Location = new System.Drawing.Point(12, 5);
            this.simbriefGroupBox.Name = "simbriefGroupBox";
            this.simbriefGroupBox.Size = new System.Drawing.Size(446, 48);
            this.simbriefGroupBox.TabIndex = 0;
            this.simbriefGroupBox.TabStop = false;
            this.simbriefGroupBox.Text = "SimBrief";
            //
            this.simbriefLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.simbriefLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this.simbriefLabel.Location = new System.Drawing.Point(10, 20);
            this.simbriefLabel.Name = "simbriefLabel";
            this.simbriefLabel.Size = new System.Drawing.Size(120, 18);
            this.simbriefLabel.Text = "Pilot ID / Username:";
            //
            this.simbriefInput.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.simbriefInput.Location = new System.Drawing.Point(135, 17);
            this.simbriefInput.Name = "simbriefInput";
            this.simbriefInput.Size = new System.Drawing.Size(300, 22);
            this.simbriefInput.TabIndex = 1;
            //
            // networksGroupBox
            //
            this.networksGroupBox.Controls.Add(this.vatsimLabel);
            this.networksGroupBox.Controls.Add(this.vatsimCidInput);
            this.networksGroupBox.Controls.Add(this.ivaoLabel);
            this.networksGroupBox.Controls.Add(this.ivaoVidInput);
            this.networksGroupBox.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.networksGroupBox.ForeColor = System.Drawing.SystemColors.Highlight;
            this.networksGroupBox.Location = new System.Drawing.Point(12, 60);
            this.networksGroupBox.Name = "networksGroupBox";
            this.networksGroupBox.Size = new System.Drawing.Size(446, 72);
            this.networksGroupBox.TabIndex = 1;
            this.networksGroupBox.TabStop = false;
            this.networksGroupBox.Text = "Online Networks";
            //
            this.vatsimLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.vatsimLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this.vatsimLabel.Location = new System.Drawing.Point(10, 20);
            this.vatsimLabel.Name = "vatsimLabel";
            this.vatsimLabel.Size = new System.Drawing.Size(120, 18);
            this.vatsimLabel.Text = "VATSIM CID:";
            //
            this.vatsimCidInput.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.vatsimCidInput.Location = new System.Drawing.Point(135, 17);
            this.vatsimCidInput.Name = "vatsimCidInput";
            this.vatsimCidInput.Size = new System.Drawing.Size(300, 22);
            this.vatsimCidInput.TabIndex = 1;
            //
            this.ivaoLabel.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.ivaoLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this.ivaoLabel.Location = new System.Drawing.Point(10, 46);
            this.ivaoLabel.Name = "ivaoLabel";
            this.ivaoLabel.Size = new System.Drawing.Size(120, 18);
            this.ivaoLabel.Text = "IVAO VID:";
            //
            this.ivaoVidInput.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            this.ivaoVidInput.Location = new System.Drawing.Point(135, 43);
            this.ivaoVidInput.Name = "ivaoVidInput";
            this.ivaoVidInput.Size = new System.Drawing.Size(300, 22);
            this.ivaoVidInput.TabIndex = 3;
            //
            this.stepIds.Controls.Add(this.simbriefGroupBox);
            this.stepIds.Controls.Add(this.networksGroupBox);

            // ═══════════════════════════════════════════════════
            // Step 4: Panel Installation (dynamically populated)
            // ═══════════════════════════════════════════════════
            this.stepPanel.Location = new System.Drawing.Point(0, 62);
            this.stepPanel.Name = "stepPanel";
            this.stepPanel.Size = new System.Drawing.Size(470, 180);
            this.stepPanel.Visible = false;
            //
            this.panelGroupBox.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.panelGroupBox.ForeColor = System.Drawing.SystemColors.Highlight;
            this.panelGroupBox.Location = new System.Drawing.Point(12, 5);
            this.panelGroupBox.Name = "panelGroupBox";
            this.panelGroupBox.Size = new System.Drawing.Size(446, 50);
            this.panelGroupBox.TabIndex = 0;
            this.panelGroupBox.TabStop = false;
            this.panelGroupBox.Text = "Kneeboard Panel Installation";
            //
            this.stepPanel.Controls.Add(this.panelGroupBox);

            // ═══════════════════════════════════════════════════
            // Navigation Buttons
            // ═══════════════════════════════════════════════════
            //
            // backButton
            //
            this.backButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.backButton.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.backButton.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.backButton.Location = new System.Drawing.Point(200, 250);
            this.backButton.Name = "backButton";
            this.backButton.Size = new System.Drawing.Size(80, 28);
            this.backButton.TabIndex = 10;
            this.backButton.Text = "Back";
            this.backButton.UseVisualStyleBackColor = true;
            this.backButton.Visible = false;
            //
            // nextButton
            //
            this.nextButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.nextButton.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.nextButton.ForeColor = System.Drawing.SystemColors.Highlight;
            this.nextButton.Location = new System.Drawing.Point(290, 250);
            this.nextButton.Name = "nextButton";
            this.nextButton.Size = new System.Drawing.Size(80, 28);
            this.nextButton.TabIndex = 11;
            this.nextButton.Text = "Next";
            this.nextButton.UseVisualStyleBackColor = true;
            //
            // skipButton
            //
            this.skipButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.skipButton.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.skipButton.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.skipButton.Location = new System.Drawing.Point(378, 250);
            this.skipButton.Name = "skipButton";
            this.skipButton.Size = new System.Drawing.Size(80, 28);
            this.skipButton.TabIndex = 12;
            this.skipButton.Text = "Skip";
            this.skipButton.UseVisualStyleBackColor = true;

            //
            // SetupWizardForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Window;
            this.ClientSize = new System.Drawing.Size(470, 290);
            this.Controls.Add(this.titleLabel);
            this.Controls.Add(this.stepLabel);
            this.Controls.Add(this.stepMsfsVersion);
            this.Controls.Add(this.stepAutostart);
            this.Controls.Add(this.stepIds);
            this.Controls.Add(this.stepPanel);
            this.Controls.Add(this.backButton);
            this.Controls.Add(this.nextButton);
            this.Controls.Add(this.skipButton);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "SetupWizardForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Kneeboard Server Setup";
            this.stepMsfsVersion.ResumeLayout(false);
            this.stepAutostart.ResumeLayout(false);
            this.autostartGroupBox.ResumeLayout(false);
            this.autostartGroupBox.PerformLayout();
            this.stepIds.ResumeLayout(false);
            this.simbriefGroupBox.ResumeLayout(false);
            this.simbriefGroupBox.PerformLayout();
            this.networksGroupBox.ResumeLayout(false);
            this.networksGroupBox.PerformLayout();
            this.stepPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Label titleLabel;
        private System.Windows.Forms.Label stepLabel;

        // Step 1
        private System.Windows.Forms.Panel stepMsfsVersion;
        private System.Windows.Forms.Label msfsVersionLabel;
        private System.Windows.Forms.CheckBox cb2024;
        private System.Windows.Forms.CheckBox cb2020;

        // Step 2
        private System.Windows.Forms.Panel stepAutostart;
        private System.Windows.Forms.GroupBox autostartGroupBox;
        private System.Windows.Forms.CheckBox simStartCheckbox;
        private System.Windows.Forms.Label exeXml2024Label;
        private System.Windows.Forms.TextBox exeXml2024Input;
        private System.Windows.Forms.Label exeXml2020Label;
        private System.Windows.Forms.TextBox exeXml2020Input;
        private System.Windows.Forms.CheckBox minimizedCheckbox;

        // Step 3
        private System.Windows.Forms.Panel stepIds;
        private System.Windows.Forms.GroupBox simbriefGroupBox;
        private System.Windows.Forms.Label simbriefLabel;
        private System.Windows.Forms.TextBox simbriefInput;
        private System.Windows.Forms.GroupBox networksGroupBox;
        private System.Windows.Forms.Label vatsimLabel;
        private System.Windows.Forms.TextBox vatsimCidInput;
        private System.Windows.Forms.Label ivaoLabel;
        private System.Windows.Forms.TextBox ivaoVidInput;

        // Step 4
        private System.Windows.Forms.Panel stepPanel;
        private System.Windows.Forms.GroupBox panelGroupBox;

        // Navigation
        private System.Windows.Forms.Button nextButton;
        private System.Windows.Forms.Button backButton;
        private System.Windows.Forms.Button skipButton;
    }
}
