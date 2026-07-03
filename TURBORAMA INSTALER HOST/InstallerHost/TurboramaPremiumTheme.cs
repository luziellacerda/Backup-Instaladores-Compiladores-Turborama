using System;
using System.Drawing;
using System.Windows.Forms;

namespace InstallerHost
{
    internal static class TurboramaPremiumTheme
    {
        private static readonly Color Background = Color.FromArgb(7, 10, 8);
        private static readonly Color PanelDark = Color.FromArgb(13, 17, 14);
        private static readonly Color PanelMid = Color.FromArgb(22, 28, 24);
        private static readonly Color Green = Color.FromArgb(103, 255, 28);
        private static readonly Color Text = Color.FromArgb(238, 242, 238);
        private static readonly Color Muted = Color.FromArgb(170, 184, 172);

        public static void Apply(Control root)
        {
            if (root == null)
            {
                return;
            }

            try
            {
                root.BackColor = Background;
                root.ForeColor = Text;
                ApplyRecursive(root);
            }
            catch
            {
            }
        }

        private static void ApplyRecursive(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                try
                {
                    if (control is Button)
                    {
                        Button button = (Button)control;
                        button.FlatStyle = FlatStyle.Flat;
                        button.FlatAppearance.BorderColor = Green;
                        button.FlatAppearance.BorderSize = 1;
                        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(31, 62, 22);
                        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(40, 86, 24);
                        button.BackColor = Color.FromArgb(16, 21, 18);
                        button.ForeColor = Text;
                        button.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
                    }
                    else if (control is Label)
                    {
                        Label label = (Label)control;
                        label.BackColor = Color.Transparent;
                        label.ForeColor = Text;
                    }
                    else if (control is CheckBox)
                    {
                        CheckBox checkBox = (CheckBox)control;
                        checkBox.BackColor = PanelDark;
                        checkBox.ForeColor = Text;
                        checkBox.FlatStyle = FlatStyle.Standard;
                    }
                    else if (control is Panel || control is UserControl || control is Form)
                    {
                        control.BackColor = Background;
                        control.ForeColor = Text;
                    }
                    else
                    {
                        control.ForeColor = Text;
                    }

                    if (control.HasChildren)
                    {
                        ApplyRecursive(control);
                    }
                }
                catch
                {
                }
            }
        }

        public static Color PremiumBackground { get { return Background; } }
        public static Color PremiumPanelDark { get { return PanelDark; } }
        public static Color PremiumPanelMid { get { return PanelMid; } }
        public static Color PremiumGreen { get { return Green; } }
        public static Color PremiumText { get { return Text; } }
        public static Color PremiumMuted { get { return Muted; } }
    }
}