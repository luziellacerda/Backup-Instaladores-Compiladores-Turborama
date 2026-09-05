using System.Drawing;
using System.Windows.Forms;
using TurboRama.Next;

namespace InstallerHost
{
    public partial class BaseForm : Form
    {
        public BaseForm()
        {
            InitializeComponent();
            if (DesignMode) return;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Ui.Font(10);
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            MaximizeBox = true;
            SizeGripStyle = SizeGripStyle.Show;
            KeyPreview = true;
            Text = "TurboRama — Instalador / LZ Games";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
    }
}
