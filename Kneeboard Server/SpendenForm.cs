using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Kneeboard_Server
{
    public partial class SpendenForm : Form
    {
        private const string PayPalDonateUrl = "https://www.paypal.com/donate/?hosted_button_id=AJAFN6YQACS3S";

        public SpendenForm()
        {
            InitializeComponent();
            ApplyLocalization();
        }

        private void ApplyLocalization()
        {
            closeBigButton.Text = Properties.Strings.Spenden_Close;
        }

        private void SpendenForm_Paint(object sender, PaintEventArgs e)
        {

        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void DonateLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start(PayPalDonateUrl);
            }
            catch { }
        }

        private void FooterEmailLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start("mailto:support@gsimulations.com");
            }
            catch { }
        }
    }
}
