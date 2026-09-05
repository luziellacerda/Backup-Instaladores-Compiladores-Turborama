using System;
using System.Drawing;
using System.Runtime.InteropServices;
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
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeWindowTheme.Apply(Handle);
        }
        protected override void OnSystemColorsChanged(EventArgs e)
        {
            base.OnSystemColorsChanged(e);
            if (IsHandleCreated) NativeWindowTheme.Apply(Handle);
        }
    }

    // Keep the native Windows caption and its standard system buttons. DWM
    // attributes modernize only its colors; unsupported attributes safely fail.
    internal static class NativeWindowTheme
    {
        private const int UseImmersiveDarkMode = 20;
        private const int UseImmersiveDarkModeLegacy = 19;
        private const int BorderColor = 34;
        private const int CaptionColor = 35;
        private const int TextColor = 36;
        internal static int ColorRef(Color color) { return color.R | color.G << 8 | color.B << 16; }
        internal static void Apply(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            try
            {
                int enabled = SystemInformation.HighContrast ? 0 : 1;
                if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, UseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
                int caption = SystemInformation.HighContrast ? -1 : ColorRef(Palette.Background);
                int border = SystemInformation.HighContrast ? -1 : ColorRef(Palette.Line);
                int text = SystemInformation.HighContrast ? -1 : ColorRef(Palette.Text);
                DwmSetWindowAttribute(handle, CaptionColor, ref caption, sizeof(int));
                DwmSetWindowAttribute(handle, BorderColor, ref border, sizeof(int));
                DwmSetWindowAttribute(handle, TextColor, ref text, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
