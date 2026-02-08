using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Kneeboard_Server.Logging;
using Kneeboard_Server.Navigraph;

namespace Kneeboard_Server
{
    public partial class InformationForm : Form
    {
        public InformationForm()
        {
            InitializeComponent();
            if (Properties.Settings.Default.autostart == true)
            {
                autostart.Checked = true;
            }
            else
            {
                autostart.Checked = false;
            }

            // simStart Checkbox: tatsächlichen Status aus exe.xml lesen
            bool isInExeXml = IsKneeboardInAnyExeXml();
            simStart.Checked = isInExeXml;
            // Setting synchronisieren falls es abweicht
            if (Properties.Settings.Default.simStart != isInExeXml)
            {
                Properties.Settings.Default.simStart = isInExeXml;
                Properties.Settings.Default.Save();
            }

            if (Properties.Settings.Default.minimized == true)
            {
                minimized.Checked = true;
            }
            else
            {
                minimized.Checked = false;
            }

            // Auto-detect exe.xml paths
            UpdateExeXmlStatus();

            if (Properties.Settings.Default.simbriefId != "")
            {
                SimbriefIdInput.Text = Properties.Settings.Default.simbriefId;
            }

            if (Properties.Settings.Default.vatsimCid != "")
            {
                VatsimCidInput.Text = Properties.Settings.Default.vatsimCid;
            }

            if (Properties.Settings.Default.ivaoVid != "")
            {
                IvaoVidInput.Text = Properties.Settings.Default.ivaoVid;
            }

            // Update ID status labels
            UpdateIdStatusLabels();

            // Load max cache size setting (0 = unlimited)
            long maxCacheSize = Properties.Settings.Default.maxCacheSizeMB;
            maxCacheSizeInput.Text = maxCacheSize.ToString();
            UpdateCacheButtonText();

            // Load Navigraph status
            UpdateNavigraphStatus();

            // Load MSFS Panel status
            UpdatePanelStatus();

            // Load SRTM settings
            useSrtmCheckbox.Checked = Properties.Settings.Default.useSrtmElevation;
            UpdateSrtmControls();
            UpdateSrtmStatus();

            // Populate SRTM region ComboBox
            foreach (var region in SimpleHTTPServer.SrtmRegions)
            {
                srtmRegionComboBox.Items.Add(region.Value.description);
            }
            srtmRegionComboBox.SelectedIndex = 0; // Default to Europe

            // Load serial number
            string serial = Properties.Settings.Default.serialNumber;
            if (!string.IsNullOrEmpty(serial))
            {
                serialNumberInput.Text = serial;
            }
            UpdateSerialStatus();

            // Load auto-update setting
            autoUpdateCheckbox.Checked = Properties.Settings.Default.autoUpdateCheck;
        }

        private void InformationForm_Load(object sender, EventArgs e)
        {
            if (Owner != null)
                Location = new Point(Owner.Location.X + Owner.Width / 2 - Width / 2,
                    Owner.Location.Y + Owner.Height / 2 - Height / 2);
            Version.Text = "Version: " + Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.Highlight, ButtonBorderStyle.Solid);
        }


        private void chkBackup_CheckChanged(object sender, EventArgs e)
        {
            try
            {
                if (autostart.Checked == true)
                {
                    Properties.Settings.Default.autostart = true;
                    Properties.Settings.Default.Save();
                }
                else
                {
                    Properties.Settings.Default.autostart = false;
                    Properties.Settings.Default.Save();
                }
            }
            catch
            {

            }
        }

        private void MSFSStart_CheckChanged(object sender, EventArgs e)
        {
            try
            {
                Properties.Settings.Default.simStart = simStart.Checked;
                Properties.Settings.Default.Save();
                Kneeboard_Server.WriteExeXML();
            }
            catch
            {

            }
        }


        private void minimized_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                if (minimized.Checked == true)
                {
                    Properties.Settings.Default.minimized = true;
                    Properties.Settings.Default.Save();
                }
                else
                {
                    Properties.Settings.Default.minimized = false;
                    Properties.Settings.Default.Save();
                }
            }
            catch
            {

            }
        }

        private void UpdateExeXmlStatus()
        {
            string path2024 = MsfsPathDetector.DetectExeXmlPath2024();
            string path2020 = MsfsPathDetector.DetectExeXmlPath2020();

            if (path2024 != null)
            {
                exeXml2024Input.Text = path2024;
                exeXml2024Input.ForeColor = System.Drawing.Color.Green;
            }
            else
            {
                exeXml2024Input.Text = "Nicht erkannt";
                exeXml2024Input.ForeColor = System.Drawing.Color.Gray;
            }

            if (path2020 != null)
            {
                exeXml2020Input.Text = path2020;
                exeXml2020Input.ForeColor = System.Drawing.Color.Green;
            }
            else
            {
                exeXml2020Input.Text = "Nicht erkannt";
                exeXml2020Input.ForeColor = System.Drawing.Color.Gray;
            }
        }

        private void ExeXml2024Input_MouseDown(object sender, MouseEventArgs e)
        {
            BrowseForExeXml(exeXml2024Input);
        }

        private void ExeXml2020Input_MouseDown(object sender, MouseEventArgs e)
        {
            BrowseForExeXml(exeXml2020Input);
        }

        private void BrowseForExeXml(System.Windows.Forms.TextBox targetInput)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                    FileName = "exe.xml"
                };

                // Initial-Verzeichnis aus dem aktuellen Pfad im Textfeld setzen
                string currentPath = targetInput.Text;
                if (!string.IsNullOrEmpty(currentPath) && currentPath != "Nicht erkannt")
                {
                    string dir = Path.GetDirectoryName(currentPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        openFileDialog.InitialDirectory = dir;
                    }
                }

                if (openFileDialog.ShowDialog() == DialogResult.OK &&
                    openFileDialog.FileName.EndsWith("exe.xml"))
                {
                    targetInput.Text = openFileDialog.FileName;
                    targetInput.ForeColor = System.Drawing.Color.Green;
                    Properties.Settings.Default.exeXmlPath = openFileDialog.FileName;
                    Properties.Settings.Default.Save();
                }
            }
            catch
            {

            }
        }

        private void SimbriefIdInput_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.simbriefId = SimbriefIdInput.Text;
            Properties.Settings.Default.Save();
            // Restart background SimBrief sync with new ID
            Kneeboard_Server.StartBackgroundSimbriefSync();
            UpdateIdStatusLabels();
        }

        private void VatsimCidInput_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.vatsimCid = VatsimCidInput.Text;
            Properties.Settings.Default.Save();
            UpdateIdStatusLabels();
        }

        private void IvaoVidInput_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ivaoVid = IvaoVidInput.Text;
            Properties.Settings.Default.Save();
            UpdateIdStatusLabels();
        }

        private void UpdateIdStatusLabels()
        {
            // SimBrief
            string simbriefId = SimbriefIdInput.Text;
            if (!string.IsNullOrEmpty(simbriefId) && simbriefId != "SimBrief ID or Username")
            {
                simbriefStatusLabel.Text = "OK";
                simbriefStatusLabel.ForeColor = System.Drawing.Color.Green;
            }
            else
            {
                simbriefStatusLabel.Text = "---";
                simbriefStatusLabel.ForeColor = System.Drawing.Color.Gray;
            }

            // VATSIM
            string vatsimCid = VatsimCidInput.Text;
            if (!string.IsNullOrEmpty(vatsimCid) && vatsimCid != "VATSIM CID")
            {
                vatsimStatusLabel.Text = "OK";
                vatsimStatusLabel.ForeColor = System.Drawing.Color.Green;
            }
            else
            {
                vatsimStatusLabel.Text = "---";
                vatsimStatusLabel.ForeColor = System.Drawing.Color.Gray;
            }

            // IVAO
            string ivaoVid = IvaoVidInput.Text;
            if (!string.IsNullOrEmpty(ivaoVid) && ivaoVid != "IVAO VID")
            {
                ivaoStatusLabel.Text = "OK";
                ivaoStatusLabel.ForeColor = System.Drawing.Color.Green;
            }
            else
            {
                ivaoStatusLabel.Text = "---";
                ivaoStatusLabel.ForeColor = System.Drawing.Color.Gray;
            }
        }

        private void ClearCacheButton_Click(object sender, EventArgs e)
        {
            try
            {
                SimpleHTTPServer.ClearOpenAipCache();
                SimpleHTTPServer.ClearBoundariesCache();
                SimpleHTTPServer.ClearElevationCache();
                UpdateCacheButtonText();
                UpdateSrtmStatus();
                MessageBox.Show("Cache wurde geleert (OpenAIP + Boundaries + Elevation).", "Cache",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Leeren des Cache: {ex.Message}", "Fehler",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void MaxCacheSizeInput_TextChanged(object sender, EventArgs e)
        {
            if (long.TryParse(maxCacheSizeInput.Text, out long size) && size >= 0)
            {
                Properties.Settings.Default.maxCacheSizeMB = size;
                Properties.Settings.Default.Save();
            }
        }

        private void UpdateCacheButtonText()
        {
            try
            {
                long cacheSizeBytes = SimpleHTTPServer.GetCacheSize();
                double cacheSizeMB = cacheSizeBytes / (1024.0 * 1024.0);
                clearCacheButton.Text = $"Clear Cache ({cacheSizeMB:F1} MB)";
            }
            catch
            {
                clearCacheButton.Text = "Clear OpenAIP Cache";
            }
        }

        #region Navigraph Integration

        private NavigraphAuthService _navigraphAuth;

        private void UpdateNavigraphStatus()
        {
            try
            {
                _navigraphAuth = SimpleHTTPServer.GetNavigraphAuth();

                if (_navigraphAuth == null || !_navigraphAuth.IsAuthenticated)
                {
                    navigraphStatusLabel.Text = "Not logged in";
                    navigraphStatusLabel.ForeColor = System.Drawing.Color.Gray;
                    navigraphLoginButton.Text = "Login";
                }
                else
                {
                    var dataService = SimpleHTTPServer.GetNavigraphData();
                    string airac = dataService?.CurrentAiracCycle ?? "";

                    // Show "OK" with AIRAC cycle instead of username (which might be a GUID)
                    if (!string.IsNullOrEmpty(airac))
                    {
                        navigraphStatusLabel.Text = $"OK (AIRAC {airac})";
                    }
                    else
                    {
                        navigraphStatusLabel.Text = "OK";
                    }
                    navigraphStatusLabel.ForeColor = System.Drawing.Color.Green;
                    navigraphLoginButton.Text = "Logout";
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.UIError($"Navigraph status update error: {ex.Message}");
                navigraphStatusLabel.Text = "Error";
                navigraphStatusLabel.ForeColor = System.Drawing.Color.Red;
            }
        }

        private async void NavigraphLoginButton_Click(object sender, EventArgs e)
        {
            try
            {
                _navigraphAuth = SimpleHTTPServer.GetNavigraphAuth();

                if (_navigraphAuth == null)
                {
                    MessageBox.Show("Navigraph service not available.\n\nNavigraph integration coming soon.",
                        "Navigraph", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // If already authenticated, logout
                if (_navigraphAuth.IsAuthenticated)
                {
                    _navigraphAuth.Logout();
                    UpdateNavigraphStatus();
                    MessageBox.Show("Logged out from Navigraph.", "Navigraph",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Start device auth flow
                navigraphLoginButton.Enabled = false;
                navigraphLoginButton.Text = "Starting...";

                // Capture log messages to show actual API error
                string lastLogMessage = null;
                EventHandler<string> logHandler = (s2, msg) => lastLogMessage = msg;
                _navigraphAuth.OnLog += logHandler;

                var deviceCode = await _navigraphAuth.StartDeviceAuthFlowAsync();

                _navigraphAuth.OnLog -= logHandler;

                if (deviceCode == null)
                {
                    MessageBox.Show(
                        "Navigraph-Authentifizierung konnte nicht gestartet werden.\n\n" +
                        (lastLogMessage ?? "Bitte prüfe deine Internetverbindung und versuche es erneut."),
                        "Navigraph", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Show verification dialog
                string message = $"Please go to:\n\n{deviceCode.VerificationUri}\n\n" +
                    $"And enter code: {deviceCode.UserCode}\n\n" +
                    $"Click OK when done, or Cancel to abort.";

                // Copy code to clipboard
                try
                {
                    Clipboard.SetText(deviceCode.UserCode);
                    message += "\n(Code copied to clipboard)";
                }
                catch { }

                // Open browser
                try
                {
                    System.Diagnostics.Process.Start(deviceCode.VerificationUriComplete ?? deviceCode.VerificationUri);
                }
                catch { }

                navigraphLoginButton.Text = $"Code: {deviceCode.UserCode}";
                navigraphStatusLabel.Text = "Waiting for authorization...";
                navigraphStatusLabel.ForeColor = System.Drawing.Color.Orange;

                // Poll for authorization (waits until user completes or timeout)
                bool success = await _navigraphAuth.PollForTokenAsync(deviceCode.DeviceCode, deviceCode.Interval);

                if (success)
                {
                    // Check subscription status first
                    navigraphStatusLabel.Text = "Checking subscription...";
                    await _navigraphAuth.CheckSubscriptionAsync();

                    // Download navdata if user has subscription
                    if (_navigraphAuth.HasFmsDataSubscription)
                    {
                        navigraphStatusLabel.Text = "Downloading navdata...";
                        var dataService = SimpleHTTPServer.GetNavigraphData();
                        await dataService.CheckAndDownloadUpdatesAsync();
                    }

                    UpdateNavigraphStatus();

                    var successMessage = _navigraphAuth.HasFmsDataSubscription
                        ? $"Successfully logged in as {_navigraphAuth.Username}!\nNavdata will be updated."
                        : $"Successfully logged in as {_navigraphAuth.Username}!\nNo FMS Data subscription - using bundled database.";

                    MessageBox.Show(successMessage, "Navigraph", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    UpdateNavigraphStatus();
                    MessageBox.Show("Authentication failed or was cancelled.", "Navigraph",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.UIError($"Navigraph login error: {ex.Message}");
                MessageBox.Show($"Login error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                navigraphLoginButton.Enabled = true;
                UpdateNavigraphStatus();
            }
        }

        #endregion

        #region MSFS Panel Deployment

        private void UpdatePanelStatus()
        {
            try
            {
                if (!PanelDeploymentService.SourceExists())
                {
                    panelStatusLabel.Text = "Source missing";
                    panelStatusLabel.ForeColor = Color.Red;
                    panelPathLabel.Text = "";
                    installPanelButton.Enabled = false;
                    return;
                }

                var installations = MsfsPathDetector.DetectMsfsInstallations();

                if (installations.Count == 0)
                {
                    panelStatusLabel.Text = "MSFS not found";
                    panelStatusLabel.ForeColor = Color.Gray;
                    panelPathLabel.Text = "Use button to select manually";
                    installPanelButton.Text = "Install Panel (Browse...)";
                    installPanelButton.Enabled = true;
                    return;
                }

                // Status für alle erkannten Installationen zusammenfassen
                int installedCount = 0;
                int needsUpdateCount = 0;
                var statusParts = new System.Collections.Generic.List<string>();

                foreach (var inst in installations)
                {
                    var info = MsfsPathDetector.GetInstalledPackageInfo(inst.CommunityPath);
                    string shortVersion = inst.Version.Replace("MSFS ", "");
                    if (info.IsInstalled)
                    {
                        installedCount++;
                        if (PanelDeploymentService.NeedsUpdate(inst.CommunityPath))
                        {
                            needsUpdateCount++;
                            statusParts.Add($"{shortVersion}: v{info.Version}!");
                        }
                        else
                        {
                            statusParts.Add($"{shortVersion}: v{info.Version}");
                        }
                    }
                    else
                    {
                        statusParts.Add($"{shortVersion}: nicht installiert");
                    }
                }

                panelPathLabel.Text = string.Join("  |  ", statusParts);

                if (needsUpdateCount > 0)
                {
                    panelStatusLabel.Text = "Update available";
                    panelStatusLabel.ForeColor = Color.Orange;
                    installPanelButton.Text = "Update Panel";
                }
                else if (installedCount == installations.Count)
                {
                    panelStatusLabel.Text = "Installed";
                    panelStatusLabel.ForeColor = Color.Green;
                    installPanelButton.Text = "Reinstall Panel";
                }
                else if (installedCount > 0)
                {
                    panelStatusLabel.Text = "Partially installed";
                    panelStatusLabel.ForeColor = Color.Orange;
                    installPanelButton.Text = "Install Panel";
                }
                else
                {
                    panelStatusLabel.Text = "Not installed";
                    panelStatusLabel.ForeColor = Color.Gray;
                    installPanelButton.Text = "Install Panel";
                }

                installPanelButton.Enabled = true;
            }
            catch (Exception ex)
            {
                KneeboardLogger.UIError($"Panel status error: {ex.Message}");
                panelStatusLabel.Text = "Error";
                panelStatusLabel.ForeColor = Color.Red;
            }
        }

        private async void InstallPanelButton_Click(object sender, EventArgs e)
        {
            try
            {
                var installations = MsfsPathDetector.DetectMsfsInstallations();

                var selectedPaths = ShowInstallDialog(installations);
                if (selectedPaths == null || selectedPaths.Count == 0) return;

                installPanelButton.Enabled = false;
                installPanelButton.Text = "Installing...";
                panelStatusLabel.Text = "Deploying...";
                panelStatusLabel.ForeColor = Color.Orange;

                var progress = new Progress<string>(status =>
                {
                    if (InvokeRequired)
                        Invoke(new Action(() => panelStatusLabel.Text = status));
                    else
                        panelStatusLabel.Text = status;
                });

                int successCount = 0;
                var errors = new System.Collections.Generic.List<string>();

                foreach (var communityPath in selectedPaths)
                {
                    try
                    {
                        await Task.Run(() => PanelDeploymentService.DeployPanel(communityPath, progress));
                        successCount++;

                        // Save last used path
                        Properties.Settings.Default.communityFolderPath = communityPath;
                        Properties.Settings.Default.Save();
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{communityPath}: {ex.Message}");
                    }
                }

                UpdatePanelStatus();

                if (errors.Count > 0)
                {
                    MessageBox.Show(
                        $"{successCount} von {selectedPaths.Count} Installation(en) erfolgreich.\n\nFehler:\n{string.Join("\n", errors)}",
                        "MSFS Panel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(
                        $"Kneeboard Panel erfolgreich in {successCount} Ordner installiert!",
                        "MSFS Panel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                KneeboardLogger.UIError($"Panel install error: {ex.Message}");
                MessageBox.Show($"Installation failed: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdatePanelStatus();
            }
            finally
            {
                installPanelButton.Enabled = true;
            }
        }

        private System.Collections.Generic.List<string> ShowInstallDialog(System.Collections.Generic.List<MsfsInstallation> installations)
        {
            using (var form = new Form())
            {
                form.Text = "MSFS Community Ordner wählen";
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                var checkboxes = new System.Collections.Generic.List<System.Windows.Forms.CheckBox>();
                int yPos = 12;

                foreach (var inst in installations)
                {
                    var info = MsfsPathDetector.GetInstalledPackageInfo(inst.CommunityPath);
                    string status = info.IsInstalled ? $" [v{info.Version} installiert]" : "";

                    var cb = new System.Windows.Forms.CheckBox
                    {
                        Text = $"{inst.Version} ({inst.Variant}){status}",
                        Tag = inst.CommunityPath,
                        Location = new Point(12, yPos),
                        Size = new Size(420, 20),
                        Checked = true,
                        Font = new Font("Segoe UI", 8.5F)
                    };
                    checkboxes.Add(cb);
                    form.Controls.Add(cb);

                    var pathLabel = new System.Windows.Forms.Label
                    {
                        Text = inst.CommunityPath,
                        Location = new Point(28, yPos + 19),
                        Size = new Size(410, 14),
                        Font = new Font("Segoe UI", 7F),
                        ForeColor = SystemColors.ControlDarkDark
                    };
                    form.Controls.Add(pathLabel);
                    yPos += 40;
                }

                // Manuell-Option
                var manualCb = new System.Windows.Forms.CheckBox
                {
                    Text = "Anderer Pfad:",
                    Location = new Point(12, yPos),
                    Size = new Size(100, 20),
                    Checked = false,
                    Font = new Font("Segoe UI", 8.5F)
                };
                form.Controls.Add(manualCb);

                var manualPathBox = new System.Windows.Forms.TextBox
                {
                    Location = new Point(115, yPos),
                    Size = new Size(270, 22),
                    Enabled = false
                };
                form.Controls.Add(manualPathBox);

                var browseButton = new System.Windows.Forms.Button
                {
                    Text = "...",
                    Location = new Point(390, yPos - 1),
                    Size = new Size(35, 24),
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = SystemColors.Highlight,
                    Enabled = false
                };
                browseButton.Click += (s, ev) =>
                {
                    var path = MsfsPathDetector.BrowseForCommunityFolder();
                    if (!string.IsNullOrEmpty(path))
                        manualPathBox.Text = path;
                };
                form.Controls.Add(browseButton);

                manualCb.CheckedChanged += (s, ev) =>
                {
                    manualPathBox.Enabled = manualCb.Checked;
                    browseButton.Enabled = manualCb.Checked;
                };

                yPos += 40;

                var okButton = new System.Windows.Forms.Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Location = new Point(266, yPos),
                    Size = new Size(75, 23)
                };

                var cancelButton = new System.Windows.Forms.Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(347, yPos),
                    Size = new Size(75, 23)
                };

                form.Controls.AddRange(new Control[] { okButton, cancelButton });
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;
                form.Size = new Size(450, yPos + 65);

                if (form.ShowDialog(this) != DialogResult.OK)
                    return null;

                var selectedPaths = new System.Collections.Generic.List<string>();
                foreach (var cb in checkboxes)
                {
                    if (cb.Checked)
                        selectedPaths.Add(cb.Tag.ToString());
                }
                if (manualCb.Checked && !string.IsNullOrEmpty(manualPathBox.Text))
                    selectedPaths.Add(manualPathBox.Text);

                return selectedPaths.Count > 0 ? selectedPaths : null;
            }
        }

        #endregion

        #region SRTM Elevation Data

        private void UpdateSrtmStatus()
        {
            try
            {
                int fileCount = SimpleHTTPServer.GetSrtmFileCount();
                long srtmSize = SimpleHTTPServer.GetSrtmDataSize();
                long cacheSize = SimpleHTTPServer.GetCacheSize();

                string srtmSizeStr = FormatFileSize(srtmSize);
                string cacheSizeStr = FormatFileSize(cacheSize);

                if (fileCount > 0)
                {
                    elevationStatusLabel.Text = $"SRTM: {fileCount} files ({srtmSizeStr}) | Cache: {cacheSizeStr}";
                    elevationStatusLabel.ForeColor = System.Drawing.Color.Green;
                }
                else
                {
                    elevationStatusLabel.Text = $"No SRTM data | Cache: {cacheSizeStr}";
                    elevationStatusLabel.ForeColor = System.Drawing.Color.Gray;
                }
            }
            catch
            {
                elevationStatusLabel.Text = "Error";
                elevationStatusLabel.ForeColor = System.Drawing.Color.Red;
            }
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
            if (bytes >= 1024L * 1024)
                return $"{bytes / (1024.0 * 1024):F1} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }

        private void UpdateSrtmControls()
        {
            bool enabled = useSrtmCheckbox.Checked && !_isDownloading;
            srtmRegionComboBox.Enabled = enabled;
            downloadSrtmButton.Enabled = useSrtmCheckbox.Checked;
        }

        private void UseSrtmCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.useSrtmElevation = useSrtmCheckbox.Checked;
            Properties.Settings.Default.Save();
            UpdateSrtmControls();
        }

        private bool _isDownloading = false;

        private async void DownloadSrtmButton_Click(object sender, EventArgs e)
        {
            // If already downloading, cancel
            if (_isDownloading)
            {
                SimpleHTTPServer.CancelSrtmDownload();
                downloadSrtmButton.Text = "Cancelling...";
                downloadSrtmButton.Enabled = false;
                return;
            }

            // Get selected region
            if (srtmRegionComboBox.SelectedIndex < 0)
            {
                MessageBox.Show("Please select a region to download.", "SRTM Download",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Get region name from dictionary key by matching description
            string selectedDescription = srtmRegionComboBox.SelectedItem.ToString();
            string regionName = null;
            string regionDesc = null;
            foreach (var region in SimpleHTTPServer.SrtmRegions)
            {
                if (region.Value.description == selectedDescription)
                {
                    regionName = region.Key;
                    regionDesc = region.Value.description;
                    break;
                }
            }

            if (regionName == null)
            {
                MessageBox.Show("Invalid region selected.", "SRTM Download",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var result = MessageBox.Show(
                $"Download SRTM elevation data for {regionDesc}?\n\n" +
                "The data will be stored locally for offline use.\n" +
                "This may take a while depending on your connection.\n\n" +
                "Continue?",
                "Download SRTM Data",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            _isDownloading = true;
            srtmRegionComboBox.Enabled = false;
            downloadSrtmButton.Text = "Cancel";
            downloadSrtmButton.ForeColor = System.Drawing.Color.Red;
            elevationStatusLabel.Text = "Starting...";
            elevationStatusLabel.ForeColor = System.Drawing.Color.Orange;

            try
            {
                var progress = new Progress<string>(status =>
                {
                    if (InvokeRequired)
                        Invoke(new Action(() => elevationStatusLabel.Text = status));
                    else
                        elevationStatusLabel.Text = status;
                });

                await SimpleHTTPServer.DownloadSrtmRegionAsync(regionName, progress);

                UpdateSrtmStatus();
                MessageBox.Show("SRTM data download complete!", "Success",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("Download was cancelled.", "Cancelled",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateSrtmStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Download error: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateSrtmStatus();
            }
            finally
            {
                _isDownloading = false;
                downloadSrtmButton.Text = "Download";
                downloadSrtmButton.ForeColor = System.Drawing.SystemColors.Highlight;
                UpdateSrtmControls();
            }
        }

        #endregion

        private void SupportLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var spendenForm = new SpendenForm();
            spendenForm.ShowDialog(this);
        }

        #region Serial Number

        private void SerialNumberInput_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.serialNumber = serialNumberInput.Text.Trim();
            Properties.Settings.Default.Save();
            UpdateSerialStatus();
        }

        private void UpdateSerialStatus()
        {
            string serial = serialNumberInput.Text.Trim();
            if (string.IsNullOrEmpty(serial))
            {
                serialStatusLabel.Text = "";
                serialStatusLabel.ForeColor = System.Drawing.Color.Gray;
            }
            else if (IsSerialValid(serial))
            {
                serialStatusLabel.Text = "Valid";
                serialStatusLabel.ForeColor = System.Drawing.Color.Green;
            }
            else
            {
                serialStatusLabel.Text = "Invalid";
                serialStatusLabel.ForeColor = System.Drawing.Color.Red;
            }
        }

        public static bool IsSerialValid(string serial)
        {
            if (string.IsNullOrEmpty(serial))
                return false;

            serial = serial.Trim().ToUpperInvariant();

            // Format: GSIM-XXXX-XXXX-XXXX
            if (serial.Length != 19)
                return false;

            if (!serial.StartsWith("GSIM-"))
                return false;

            string[] parts = serial.Split('-');
            if (parts.Length != 4)
                return false;

            // parts[0] = "GSIM", parts[1] = 4 hex, parts[2] = 4 hex, parts[3] = 4 hex (checksum)
            string payload = parts[1] + parts[2]; // 8 hex chars
            string checkPart = parts[3];           // 4 hex chars

            if (payload.Length != 8 || checkPart.Length != 4)
                return false;

            // Validate hex characters
            foreach (char c in payload + checkPart)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))
                    return false;
            }

            // Calculate checksum
            int sum = 0;
            foreach (char c in payload)
            {
                sum += (int)c;
            }
            int checksum = (sum * 31) % 0xFFFF;
            string expectedCheck = checksum.ToString("X4");

            return checkPart == expectedCheck;
        }

        #endregion

        #region Updates

        private void AutoUpdateCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.autoUpdateCheck = autoUpdateCheckbox.Checked;
            Properties.Settings.Default.Save();
        }

        private void CheckForUpdatesButton_Click(object sender, EventArgs e)
        {
            var mainForm = Owner as Kneeboard_Server;
            if (mainForm != null)
            {
                mainForm.CheckForGitHubUpdate(manual: true);
            }
        }

        #endregion

        #region exe.xml Status

        /// <summary>
        /// Prüft ob der Kneeboard Server Eintrag in einer der exe.xml Dateien vorhanden ist
        /// </summary>
        private bool IsKneeboardInAnyExeXml()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                var exeXmlPaths = new System.Collections.Generic.List<string>
                {
                    Path.Combine(localAppData, @"Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\exe.xml"),
                    Path.Combine(roamingAppData, @"Microsoft Flight Simulator 2024\exe.xml"),
                    Path.Combine(localAppData, @"Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\exe.xml"),
                    Path.Combine(roamingAppData, @"Microsoft Flight Simulator\exe.xml")
                };

                foreach (string path in exeXmlPaths)
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            var doc = System.Xml.Linq.XDocument.Load(path);
                            bool found = doc.Descendants("Launch.Addon")
                                .Any(e => (string)e.Element("Name") == "Kneeboard Server");
                            if (found) return true;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return false;
        }

        #endregion
    }
}
