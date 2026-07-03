using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Allegoria.Controls
{
    public partial class WizardPanel : UserControl
    {
        private Image image;
        private string title;

        public WizardPanel()
        {
            InitializeComponent();

this.BackColor = SystemColors.Window;
            this.Height = 60;
        }

        [Category("Appearance")]
        public Image Image
        {
            get { return image; }
            set { image = value; Invalidate(); }
        }

        [Category("Appearance")]
        public string Title
        {
            get { return title; }
            set { title = value; Invalidate(); }
        }

        public override string Text
        {
            get { return base.Text; }
            set { base.Text = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(this.BackColor);

            int padding = 12;
            int imageSize = 40;
            if (image != null)
            {
                e.Graphics.DrawImage(image, new Rectangle(this.Width - imageSize - padding, 10, imageSize, imageSize));
            }

            string header = !string.IsNullOrEmpty(this.Text) ? this.Text : this.Title;
            using (Font titleFont = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (Brush brush = new SolidBrush(SystemColors.ControlText))
            {
                e.Graphics.DrawString(header ?? string.Empty, titleFont, brush, new RectangleF(20, 18, this.Width - 90, 30));
            }
        }
    }
}
