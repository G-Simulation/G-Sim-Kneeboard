using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Kneeboard_Server
{
    public partial class SetupWizardForm : Form
    {
        private readonly List<CheckBox> _panelCheckboxes = new List<CheckBox>();
        private CheckBox _manualPathCheckbox;
        private TextBox _manualPathTextBox;

        private Panel[] _steps;
        private int _currentStep;

        private string[] _stepTitles;

        // Public API (used by KneeboardServer.cs)
        public bool SimStartChecked => simStartCheckbox.Checked;
        public bool MinimizedChecked => minimizedCheckbox.Checked;
        public string SimbriefId => simbriefInput.Text.Trim();
        public string VatsimCid => vatsimCidInput.Text.Trim();
        public string IvaoVid => ivaoVidInput.Text.Trim();
        public string ExeXmlPath2024 => exeXml2024Input.Tag as string;
        public string ExeXmlPath2020 => exeXml2020Input.Tag as string;

        public SetupWizardForm()
        {
            InitializeComponent();
            ApplyLocalization();

            _steps = new Panel[] { stepMsfsVersion, stepAutostart, stepIds, stepPanel };
            _currentStep = 0;

            LoadCurrentSettings();
            AutoDetectMsfsVersions();
            UpdateExeXmlStatus();
            PopulatePanelInstallation();

            nextButton.Click += NextButton_Click;
            backButton.Click += BackButton_Click;
            skipButton.Click += SkipButton_Click;

            ShowStep(0);
        }

        private void ApplyLocalization()
        {
            _stepTitles = new[]
            {
                Properties.Strings.SetupWizard_StepTitle1,
                Properties.Strings.SetupWizard_StepTitle2,
                Properties.Strings.SetupWizard_StepTitle3,
                Properties.Strings.SetupWizard_StepTitle4
            };

            titleLabel.Text = Properties.Strings.SetupWizard_Title;
            msfsVersionLabel.Text = Properties.Strings.SetupWizard_MsfsQuestion;
            autostartGroupBox.Text = Properties.Strings.SetupWizard_AutostartGroup;
            simStartCheckbox.Text = Properties.Strings.SetupWizard_SimStartCheckbox;
            minimizedCheckbox.Text = Properties.Strings.SetupWizard_MinimizedCheckbox;
            networksGroupBox.Text = Properties.Strings.SetupWizard_OnlineNetworks;
            panelGroupBox.Text = Properties.Strings.SetupWizard_PanelInstallGroup;
            backButton.Text = Properties.Strings.SetupWizard_Back;
            nextButton.Text = Properties.Strings.SetupWizard_Next;
            skipButton.Text = Properties.Strings.SetupWizard_Skip;
        }

        private void LoadCurrentSettings()
        {
            simStartCheckbox.Checked = Properties.Settings.Default.simStart;
            minimizedCheckbox.Checked = Properties.Settings.Default.minimized;
            simbriefInput.Text = Properties.Settings.Default.simbriefId ?? "";
            vatsimCidInput.Text = Properties.Settings.Default.vatsimCid ?? "";
            ivaoVidInput.Text = Properties.Settings.Default.ivaoVid ?? "";
        }

        private void AutoDetectMsfsVersions()
        {
            var installations = MsfsPathDetector.DetectMsfsInstallations();
            bool found2024 = false;
            bool found2020 = false;
            foreach (var inst in installations)
            {
                if (inst.Version == "2024") found2024 = true;
                if (inst.Version == "2020") found2020 = true;
            }
            cb2024.Checked = found2024;
            cb2020.Checked = found2020;
        }

        private void UpdateExeXmlStatus()
        {
            string path2024 = MsfsPathDetector.DetectExeXmlPath2024();
            string path2020 = MsfsPathDetector.DetectExeXmlPath2020();

            if (path2024 != null)
            {
                exeXml2024Input.Text = path2024;
                exeXml2024Input.ForeColor = Color.Green;
                exeXml2024Input.Tag = path2024;
            }
            else
            {
                exeXml2024Input.Text = Properties.Strings.SetupWizard_NotDetectedClick;
                exeXml2024Input.ForeColor = Color.Gray;
                exeXml2024Input.Tag = null;
            }

            if (path2020 != null)
            {
                exeXml2020Input.Text = path2020;
                exeXml2020Input.ForeColor = Color.Green;
                exeXml2020Input.Tag = path2020;
            }
            else
            {
                exeXml2020Input.Text = Properties.Strings.SetupWizard_NotDetectedClick;
                exeXml2020Input.ForeColor = Color.Gray;
                exeXml2020Input.Tag = null;
            }

            exeXml2024Input.MouseDown += (s, ev) => BrowseForExeXml(exeXml2024Input);
            exeXml2020Input.MouseDown += (s, ev) => BrowseForExeXml(exeXml2020Input);
        }

        private void BrowseForExeXml(TextBox targetInput)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                    FileName = "exe.xml"
                };

                string currentPath = targetInput.Tag as string;
                if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(Path.GetDirectoryName(currentPath)))
                {
                    openFileDialog.InitialDirectory = Path.GetDirectoryName(currentPath);
                }

                if (openFileDialog.ShowDialog() == DialogResult.OK &&
                    openFileDialog.FileName.EndsWith("exe.xml"))
                {
                    targetInput.Text = openFileDialog.FileName;
                    targetInput.ForeColor = Color.Green;
                    targetInput.Tag = openFileDialog.FileName;
                }
            }
            catch { }
        }

        private void PopulatePanelInstallation()
        {
            bool hasSource = PanelDeploymentService.SourceExists();
            if (!hasSource)
            {
                panelGroupBox.Visible = false;
                return;
            }

            var installations = MsfsPathDetector.DetectMsfsInstallations();
            int innerY = 18;

            foreach (var inst in installations)
            {
                var info = MsfsPathDetector.GetInstalledPackageInfo(inst.CommunityPath);
                string status = info.IsInstalled
                    ? " " + string.Format(Properties.Strings.Info_Installed, info.Version)
                    : "";

                var cb = new CheckBox
                {
                    Text = $"{inst.Version} ({inst.Variant}){status}",
                    Tag = inst.CommunityPath,
                    Location = new Point(10, innerY),
                    Size = new Size(420, 20),
                    Checked = true,
                    Font = new Font("Segoe UI", 8.5F),
                    ForeColor = SystemColors.ControlText
                };
                _panelCheckboxes.Add(cb);
                panelGroupBox.Controls.Add(cb);

                var pathLabel = new Label
                {
                    Text = inst.CommunityPath,
                    Location = new Point(26, innerY + 19),
                    Size = new Size(410, 14),
                    Font = new Font("Segoe UI", 7F),
                    ForeColor = SystemColors.ControlDarkDark
                };
                panelGroupBox.Controls.Add(pathLabel);
                innerY += 36;
            }

            // Manual path option
            _manualPathCheckbox = new CheckBox
            {
                Text = Properties.Strings.SetupWizard_OtherPath,
                Location = new Point(10, innerY),
                Size = new Size(100, 20),
                Checked = false,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = SystemColors.ControlText
            };
            panelGroupBox.Controls.Add(_manualPathCheckbox);

            _manualPathTextBox = new TextBox
            {
                Location = new Point(115, innerY),
                Size = new Size(270, 22),
                Enabled = false
            };
            panelGroupBox.Controls.Add(_manualPathTextBox);

            var browseButton = new Button
            {
                Text = "...",
                Location = new Point(390, innerY - 1),
                Size = new Size(35, 24),
                FlatStyle = FlatStyle.Flat,
                ForeColor = SystemColors.Highlight,
                Enabled = false
            };
            browseButton.Click += (s, ev) =>
            {
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.Description = Properties.Strings.SetupWizard_BrowseCommunity;
                    if (fbd.ShowDialog() == DialogResult.OK)
                        _manualPathTextBox.Text = fbd.SelectedPath;
                }
            };
            panelGroupBox.Controls.Add(browseButton);

            _manualPathCheckbox.CheckedChanged += (s, ev) =>
            {
                _manualPathTextBox.Enabled = _manualPathCheckbox.Checked;
                browseButton.Enabled = _manualPathCheckbox.Checked;
            };

            innerY += 30;
            panelGroupBox.Size = new Size(446, innerY + 5);

            // Resize stepPanel to fit
            int panelBottom = panelGroupBox.Bottom + 10;
            if (panelBottom > stepPanel.Height)
                stepPanel.Height = panelBottom;
        }

        private void ShowStep(int step)
        {
            _currentStep = step;

            for (int i = 0; i < _steps.Length; i++)
                _steps[i].Visible = (i == step);

            stepLabel.Text = _stepTitles[step];

            // Back button: hidden on first step
            backButton.Visible = step > 0;

            // Next button: localized "Finish" on last step
            bool isLastStep = (step == _steps.Length - 1);
            nextButton.Text = isLastStep ? Properties.Strings.SetupWizard_Finish : Properties.Strings.SetupWizard_Next;

            // Adjust form height based on current step panel
            int contentBottom = _steps[step].Top + _steps[step].Height;
            int buttonY = contentBottom + 8;
            backButton.Top = buttonY;
            nextButton.Top = buttonY;
            skipButton.Top = buttonY;
            this.ClientSize = new Size(470, buttonY + 40);
        }

        private void NextButton_Click(object sender, EventArgs e)
        {
            if (_currentStep < _steps.Length - 1)
            {
                ShowStep(_currentStep + 1);
            }
            else
            {
                // Last step: finish wizard
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        private void BackButton_Click(object sender, EventArgs e)
        {
            if (_currentStep > 0)
                ShowStep(_currentStep - 1);
        }

        private void SkipButton_Click(object sender, EventArgs e)
        {
            // Cancel at any point = no side effects
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlPaint.DrawBorder(e.Graphics, ClientRectangle,
                SystemColors.Highlight, ButtonBorderStyle.Solid);
        }

        public List<string> GetSelectedPanelPaths()
        {
            var paths = new List<string>();
            foreach (var cb in _panelCheckboxes)
            {
                if (cb.Checked)
                    paths.Add((string)cb.Tag);
            }
            if (_manualPathCheckbox != null && _manualPathCheckbox.Checked
                && !string.IsNullOrEmpty(_manualPathTextBox?.Text))
            {
                paths.Add(_manualPathTextBox.Text.Trim());
            }
            return paths;
        }
    }
}
