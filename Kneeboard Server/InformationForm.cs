using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;


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
                UpdateCacheButtonText();
                MessageBox.Show("Cache wurde geleert (OpenAIP + Boundaries).", "Cache",
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
    }
}
