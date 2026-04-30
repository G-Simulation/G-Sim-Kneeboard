using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Kneeboard_Server
{
    /// <summary>
    /// Modaler DSGVO-Zustimmungs-Dialog. Wird beim ersten Start (oder nach Versions-Update der
    /// Datenschutzerklärung) angezeigt. Ohne Zustimmung wird die Anwendung beendet.
    /// </summary>
    public class DsgvoConsentForm : Form
    {
        private const string DatenschutzUrl = "https://www.gsimulations.de/datenschutzerklaerung/";
        private const string ImpressumUrl = "https://www.gsimulations.de/impressum/";

        private CheckBox _acceptCheckbox;
        private Button _acceptButton;
        private Button _declineButton;

        public bool Accepted { get; private set; }

        public DsgvoConsentForm()
        {
            Text = "Datenschutz-Zustimmung / Privacy Consent";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 460);

            BuildControls();
        }

        private void BuildControls()
        {
            var titleLabel = new Label
            {
                Text = "G-Sim Kneeboard verarbeitet personenbezogene Daten",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(20, 20),
                Size = new Size(520, 28),
                AutoSize = false
            };

            var infoText = new Label
            {
                Text =
                    "Damit das Kneeboard funktionieren kann, verarbeitet die Anwendung folgende Daten:\r\n\r\n" +
                    "• VATSIM-CID, IVAO-VID, SimBrief-ID (sofern eingegeben)\r\n" +
                    "• Flugzeug-Position (Lat/Lng/Höhe) vom MSFS\r\n" +
                    "• Flugplan-Daten (Wegpunkte, Routen)\r\n" +
                    "• Verbindungen zu externen APIs: Navigraph, OpenAIP, VATSIM, IVAO, SimBrief, Aviation Weather, OpenWeather\r\n\r\n" +
                    "Es werden keine Daten an Dritte verkauft. Eine vollständige Auflistung der verarbeiteten Daten, " +
                    "Speicherorte, Empfänger und deiner Rechte (Auskunft, Löschung, Widerspruch) findest du in der " +
                    "Datenschutzerklärung. Es gilt das Impressum auf gsimulations.de.\r\n\r\n" +
                    "Mit deiner Zustimmung erklärst du dich mit der Datenverarbeitung gemäß Datenschutzerklärung einverstanden. " +
                    "Du kannst die Zustimmung jederzeit widerrufen, indem du die Anwendung deinstallierst oder den Support kontaktierst.",
                Location = new Point(20, 56),
                Size = new Size(520, 240),
                AutoSize = false
            };

            var datenschutzLink = new LinkLabel
            {
                Text = "Datenschutzerklärung öffnen (gsimulations.de/datenschutzerklaerung)",
                Location = new Point(20, 304),
                Size = new Size(520, 20),
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            datenschutzLink.Click += (s, e) => OpenUrl(DatenschutzUrl);

            var impressumLink = new LinkLabel
            {
                Text = "Impressum öffnen (gsimulations.de/impressum)",
                Location = new Point(20, 326),
                Size = new Size(520, 20),
                LinkBehavior = LinkBehavior.HoverUnderline
            };
            impressumLink.Click += (s, e) => OpenUrl(ImpressumUrl);

            _acceptCheckbox = new CheckBox
            {
                Text = "Ich habe die Datenschutzerklärung gelesen und stimme der Datenverarbeitung zu.",
                Location = new Point(20, 360),
                Size = new Size(520, 24),
                AutoSize = false
            };
            _acceptCheckbox.CheckedChanged += (s, e) => _acceptButton.Enabled = _acceptCheckbox.Checked;

            _acceptButton = new Button
            {
                Text = "Akzeptieren und starten",
                Location = new Point(280, 400),
                Size = new Size(180, 32),
                Enabled = false
            };
            _acceptButton.Click += OnAccept;

            _declineButton = new Button
            {
                Text = "Ablehnen und beenden",
                Location = new Point(80, 400),
                Size = new Size(180, 32)
            };
            _declineButton.Click += OnDecline;

            Controls.Add(titleLabel);
            Controls.Add(infoText);
            Controls.Add(datenschutzLink);
            Controls.Add(impressumLink);
            Controls.Add(_acceptCheckbox);
            Controls.Add(_acceptButton);
            Controls.Add(_declineButton);
        }

        private void OnAccept(object sender, EventArgs e)
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            Properties.Settings.Default.dsgvoAccepted = true;
            Properties.Settings.Default.dsgvoAcceptedDate = DateTime.UtcNow;
            Properties.Settings.Default.dsgvoAcceptedVersion = version;
            Properties.Settings.Default.Save();
            Accepted = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnDecline(object sender, EventArgs e)
        {
            Accepted = false;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Silent fail; falls kein Browser verfügbar
            }
        }
    }
}
