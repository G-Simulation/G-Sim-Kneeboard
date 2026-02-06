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

            if (Properties.Settings.Default.simStart == true)
            {
                simStart.Checked = true;
            }
            else
            {
                simStart.Checked = false;
            }

            if (Properties.Settings.Default.minimized == true)
            {
                minimized.Checked = true;
            }
            else
            {
                minimized.Checked = false;
            }

            if (Properties.Settings.Default.exeXmlPath != "")
            {
                folderpathInput.Text = Properties.Settings.Default.exeXmlPath;
            }

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
                if (simStart.Checked == true && Properties.Settings.Default.exeXmlPath != "")
                {
                    Properties.Settings.Default.simStart = true;
                    Properties.Settings.Default.Save();
                    Kneeboard_Server.WriteExeXML();
                }
                else
                {
                    Properties.Settings.Default.simStart = false;
                    Properties.Settings.Default.Save();
                    Kneeboard_Server.WriteExeXML();
                }
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

        private void folderpathInput_MouseDown(object sender, MouseEventArgs e)
        {
            try
            {
                OpenFileDialog openFileDialog1 = new OpenFileDialog
                {
                    Filter = "XML files (*.xml)|*.xml|All files (*.*)|*.*"
                };

                if (openFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    if (folderpathInput.Text != openFileDialog1.FileName && (openFileDialog1.FileName.EndsWith("exe.xml") == true))
                    {
                        folderpathInput.Text = openFileDialog1.FileName;
                        Properties.Settings.Default.exeXmlPath = folderpathInput.Text;
                        Properties.Settings.Default.Save();
                    }
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
        }

        private void VatsimCidInput_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.vatsimCid = VatsimCidInput.Text;
            Properties.Settings.Default.Save();
        }

        private void IvaoVidInput_TextChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ivaoVid = IvaoVidInput.Text;
            Properties.Settings.Default.Save();
        }

        private void ClearCacheButton_Click(object sender, EventArgs e)
        {
            try
            {
                SimpleHTTPServer.ClearOpenAipCache();
                SimpleHTTPServer.ClearBoundariesCache();
                SimpleHTTPServer.ClearElevationCache();
                UpdateCacheButtonText();
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

                // Use first detected installation (or saved path)
                var install = installations[0];
                var info = MsfsPathDetector.GetInstalledPackageInfo(install.CommunityPath);

                if (info.IsInstalled)
                {
                    if (PanelDeploymentService.NeedsUpdate(install.CommunityPath))
                    {
                        panelStatusLabel.Text = $"Update available (v{info.Version})";
                        panelStatusLabel.ForeColor = Color.Orange;
                        installPanelButton.Text = "Update Panel";
                    }
                    else
                    {
                        panelStatusLabel.Text = $"Installed (v{info.Version})";
                        panelStatusLabel.ForeColor = Color.Green;
                        installPanelButton.Text = "Reinstall Panel";
                    }
                }
                else
                {
                    panelStatusLabel.Text = "Not installed";
                    panelStatusLabel.ForeColor = Color.Gray;
                    installPanelButton.Text = "Install Panel";
                }

                panelPathLabel.Text = $"{install.Version} {install.Variant}: {install.CommunityPath}";
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
                string communityPath;

                if (installations.Count == 0)
                {
                    // No MSFS detected → let user browse
                    communityPath = MsfsPathDetector.BrowseForCommunityFolder();
                    if (string.IsNullOrEmpty(communityPath))
                        return;
                }
                else if (installations.Count == 1)
                {
                    communityPath = installations[0].CommunityPath;
                }
                else
                {
                    // Multiple installations → let user pick
                    var items = installations.Select(i => $"{i.Version} ({i.Variant}): {i.CommunityPath}").ToArray();
                    string selected = ShowSelectionDialog("Select MSFS Installation", items);
                    if (selected == null) return;
                    int idx = Array.IndexOf(items, selected);
                    communityPath = installations[idx].CommunityPath;
                }

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

                await Task.Run(() => PanelDeploymentService.DeployPanel(communityPath, progress));

                // Save the path
                Properties.Settings.Default.communityFolderPath = communityPath;
                Properties.Settings.Default.Save();

                UpdatePanelStatus();
                MessageBox.Show("Kneeboard Panel successfully installed!", "MSFS Panel",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        private string ShowSelectionDialog(string title, string[] items)
        {
            using (var form = new Form())
            {
                form.Text = title;
                form.Size = new Size(450, 180);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                var combo = new System.Windows.Forms.ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Location = new Point(12, 20),
                    Size = new Size(410, 21)
                };
                combo.Items.AddRange(items);
                combo.SelectedIndex = 0;

                var okButton = new System.Windows.Forms.Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Location = new Point(266, 60),
                    Size = new Size(75, 23)
                };

                var cancelButton = new System.Windows.Forms.Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(347, 60),
                    Size = new Size(75, 23)
                };

                form.Controls.AddRange(new Control[] { combo, okButton, cancelButton });
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                return form.ShowDialog(this) == DialogResult.OK ? combo.SelectedItem?.ToString() : null;
            }
        }

        #endregion

        #region SRTM Elevation Data

        private void UpdateSrtmStatus()
        {
            try
            {
                int fileCount = SimpleHTTPServer.GetSrtmFileCount();
                if (fileCount > 0)
                {
                    elevationStatusLabel.Text = $"{fileCount} files";
                    elevationStatusLabel.ForeColor = System.Drawing.Color.Green;
                }
                else
                {
                    elevationStatusLabel.Text = "No data";
                    elevationStatusLabel.ForeColor = System.Drawing.Color.Gray;
                }
            }
            catch
            {
                elevationStatusLabel.Text = "Error";
                elevationStatusLabel.ForeColor = System.Drawing.Color.Red;
            }
        }

        private void UseSrtmCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.useSrtmElevation = useSrtmCheckbox.Checked;
            Properties.Settings.Default.Save();
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
                downloadSrtmButton.Enabled = true;
                srtmRegionComboBox.Enabled = true;
                downloadSrtmButton.Text = "Download";
                downloadSrtmButton.ForeColor = System.Drawing.SystemColors.Highlight;
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
    }
}
