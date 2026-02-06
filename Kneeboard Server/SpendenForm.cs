using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Kneeboard_Server
{
    public partial class SpendenForm : Form
    {
        private const string PayPalDonateUrl = "https://www.paypal.com/donate/?hosted_button_id=AJAFN6YQACS3S";

        private const string QrBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAJYAAACWAQMAAAAGz+OhAAAABlBMVEX///8AAABVwtN+AAAACXBI" +
            "WXMAAA7EAAAOxAGVKw4bAAABvElEQVRIibWWMa6DMBBEB1G49A3gIlG4FgWSLVHkWnxxEXMDlxSW" +
            "988a/egXqbDjIiIvUoxnZ9YLfFhORGD2bA0fon6cVWwGRvvaRjF+4o8bUMlcHoE+W+4W7GuX+B1m" +
            "ZT8E/TY2YnoYyCohmu8w1eqcAozvhKK99bvJtJZ8/SOey5PneNe3JbsMlzq+/iTxvwnvMSd5lPNB" +
            "XfwTfBhsewb3U2zClRHd1uk5KphJGIUepy6gEaF7tGZYpmC9erxoNR2xjpV4O9bSFF36jPbMrRus" +
            "58NJs0dm355VzKz58jhA//FH1aoxo0TBSmJZF/ar+dHFOua8RiXRf+lZNrPt2XzVV4QHArUaqFUN" +
            "M+umvWkUoAsMaPFkcybhskn/EyKKVjWMHpc49/Sfhsf3wbZnbmX2hVaEy2BAh+LT+0x7tmgJtZ9q" +
            "p9JatmaUiA2FPdaVi137bBXjX0eXYDU8pQmezZku3r/8liZRc8Y6Vu4tr/5bmJvXni2aM9W+zCA6" +
            "54juW8l0BvH9oVcOhxHRfvoVpqMCmP84T2XWq2VXS2J4TBr+9mjJ9PV9ibw27rd+d9lVS50PNOey" +
            "DbY9+7B+AXV7C8g+U0CuAAAAAElFTkSuQmCC";

        public SpendenForm()
        {
            InitializeComponent();
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
