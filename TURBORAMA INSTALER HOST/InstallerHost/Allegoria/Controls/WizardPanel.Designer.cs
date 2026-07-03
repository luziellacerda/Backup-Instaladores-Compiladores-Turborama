namespace Allegoria.Controls
{
    public partial class WizardPanel
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

        private void InitializeComponent()
        {
            this.SuspendLayout();
            this.Name = "WizardPanel";
            this.Size = new System.Drawing.Size(548, 60);
            this.ResumeLayout(false);
        }
    }
}
