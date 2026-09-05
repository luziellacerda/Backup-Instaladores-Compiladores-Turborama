using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace InstallerHost
{
    /// <summary>
    /// Single source of truth for the runtime-only Turborama visual system.
    /// Keeping this outside *.Designer.cs makes future Designer edits safe.
    /// </summary>
    internal static class TurboramaPremiumTheme
    {
        public static readonly Color Background = Color.FromArgb(4, 7, 15);
        public static readonly Color BackgroundDeep = Color.FromArgb(2, 4, 10);
        public static readonly Color Shell = Color.FromArgb(6, 12, 22);
        public static readonly Color Surface = Color.FromArgb(8, 16, 27);
        public static readonly Color SurfaceRaised = Color.FromArgb(12, 25, 39);
        public static readonly Color SurfaceHover = Color.FromArgb(17, 36, 51);
        public static readonly Color SurfaceFocus = Color.FromArgb(10, 31, 43);
        public static readonly Color SurfaceDisabled = Color.FromArgb(13, 18, 26);
        public static readonly Color InputBackground = Color.FromArgb(5, 13, 23);

        public static readonly Color Border = Color.FromArgb(25, 58, 75);
        public static readonly Color BorderStrong = Color.FromArgb(35, 104, 128);
        public static readonly Color Cyan = Color.FromArgb(0, 229, 255);
        public static readonly Color CyanSoft = Color.FromArgb(126, 244, 255);
        public static readonly Color Green = Color.FromArgb(120, 255, 105);
        public static readonly Color GreenSoft = Color.FromArgb(184, 255, 174);
        public static readonly Color Violet = Color.FromArgb(169, 108, 255);
        public static readonly Color VioletSoft = Color.FromArgb(211, 177, 255);
        public static readonly Color Warning = Color.FromArgb(255, 200, 87);
        public static readonly Color Danger = Color.FromArgb(255, 83, 120);

        public static readonly Color Text = Color.FromArgb(242, 247, 255);
        public static readonly Color TextMuted = Color.FromArgb(166, 184, 204);
        public static readonly Color Dim = Color.FromArgb(91, 112, 135);

        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>(StringComparer.Ordinal);
        private static readonly object FontSync = new object();

        public static Font CreateFont(float size, FontStyle style)
        {
            string key = size.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ":" + ((int)style).ToString();
            lock (FontSync)
            {
                Font font;
                if (!FontCache.TryGetValue(key, out font))
                {
                    font = new Font("Segoe UI", size, style, GraphicsUnit.Point);
                    FontCache.Add(key, font);
                }
                return font;
            }
        }

        public static void Apply(Control root)
        {
            if (root == null)
            {
                return;
            }

            root.SuspendLayout();
            try
            {
                try
                {
                    ApplyToControl(root);
                }
                catch
                {
                    // Visual decoration must never stop the installer workflow.
                }
                ApplyRecursive(root);
                root.Invalidate(true);
            }
            finally
            {
                root.ResumeLayout(false);
            }
        }

        private static void ApplyRecursive(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                try
                {
                    ApplyToControl(control);
                }
                catch
                {
                    // Keep theming best-effort for legacy/custom WinForms controls.
                }
                if (control.HasChildren)
                {
                    ApplyRecursive(control);
                }
            }
        }

        private static void ApplyToControl(Control control)
        {
            if (control is NeonSurfacePanel || control is NeonBackdropPanel || control is NeonBrandMark || control is NeonStepRail || control is NeonLedIndicator)
            {
                return;
            }

            Button button = control as Button;
            if (button != null)
            {
                NeonInteraction.StyleButton(button, ResolveButtonKind(button));
                return;
            }

            LinkLabel link = control as LinkLabel;
            if (link != null)
            {
                link.BackColor = Color.Transparent;
                link.ForeColor = Text;
                link.LinkColor = Cyan;
                link.ActiveLinkColor = CyanSoft;
                link.VisitedLinkColor = VioletSoft;
                link.Font = CreateFont(9f, FontStyle.Bold);
                return;
            }

            Label label = control as Label;
            if (label != null)
            {
                label.BackColor = Color.Transparent;
                if (label.ForeColor == SystemColors.ControlText || label.ForeColor == Color.Black)
                {
                    label.ForeColor = Text;
                }
                return;
            }

            CheckBox checkBox = control as CheckBox;
            if (checkBox != null)
            {
                checkBox.BackColor = Color.Transparent;
                checkBox.ForeColor = checkBox.Enabled ? Text : Dim;
                checkBox.FlatStyle = FlatStyle.Flat;
                checkBox.FlatAppearance.BorderColor = Cyan;
                checkBox.FlatAppearance.CheckedBackColor = Color.FromArgb(13, 87, 92);
                checkBox.FlatAppearance.MouseOverBackColor = SurfaceHover;
                checkBox.Font = CreateFont(8.9f, FontStyle.Bold);
                return;
            }

            TextBoxBase textBox = control as TextBoxBase;
            if (textBox != null)
            {
                NeonInteraction.StyleField(textBox);
                return;
            }

            ComboBox comboBox = control as ComboBox;
            if (comboBox != null)
            {
                comboBox.BackColor = InputBackground;
                comboBox.ForeColor = Text;
                comboBox.FlatStyle = FlatStyle.Flat;
                comboBox.Font = CreateFont(9.2f, FontStyle.Regular);
                return;
            }

            ListControl list = control as ListControl;
            if (list != null)
            {
                control.BackColor = InputBackground;
                control.ForeColor = Text;
                control.Font = CreateFont(9f, FontStyle.Regular);
                return;
            }

            ProgressBar progress = control as ProgressBar;
            if (progress != null)
            {
                progress.BackColor = Surface;
                progress.ForeColor = Cyan;
                return;
            }

            Form form = control as Form;
            if (form != null)
            {
                form.BackColor = Background;
                form.ForeColor = Text;
                form.Font = CreateFont(9f, FontStyle.Regular);
                return;
            }

            UserControl userControl = control as UserControl;
            if (userControl != null)
            {
                userControl.BackColor = Background;
                userControl.ForeColor = Text;
                userControl.Font = CreateFont(9f, FontStyle.Regular);
                return;
            }

            Panel panel = control as Panel;
            if (panel != null)
            {
                if (panel.BackColor == SystemColors.Control || panel.BackColor == SystemColors.ControlDark)
                {
                    panel.BackColor = Surface;
                }
                panel.ForeColor = Text;
                return;
            }

            control.ForeColor = Text;
        }

        private static NeonButtonKind ResolveButtonKind(Button button)
        {
            string identity = ((button.Name ?? string.Empty) + " " + (button.Text ?? string.Empty)).ToLowerInvariant();
            if (identity.Contains("cancel") || identity.Contains("cancelar"))
            {
                return NeonButtonKind.Danger;
            }
            if (identity.Contains("next") || identity.Contains("avançar") || identity.Contains("avancar") ||
                identity.Contains("install") || identity.Contains("instalar") || identity.Contains("finish") || identity.Contains("concluir"))
            {
                return NeonButtonKind.Primary;
            }
            if (identity.Contains("back") || identity.Contains("voltar") || identity.Contains("browse") || identity.Contains("procurar"))
            {
                return NeonButtonKind.Secondary;
            }
            return NeonButtonKind.Quiet;
        }

        // Compatibility accessors retained for older InstallerHost screens.
        public static Color PremiumBackground { get { return Background; } }
        public static Color PremiumPanelDark { get { return Surface; } }
        public static Color PremiumPanelMid { get { return SurfaceRaised; } }
        public static Color PremiumGreen { get { return Green; } }
        public static Color PremiumText { get { return Text; } }
        public static Color PremiumMuted { get { return TextMuted; } }
    }
}
