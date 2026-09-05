using System.Drawing.Drawing2D;

namespace TurboRama.EmulationStation.Access;

// Presentation only. This control never reads a license, a key or the network.
internal sealed class LicenseAccessView : UserControl
{
    internal static readonly Color Canvas = Color.FromArgb(15, 21, 32);
    internal static readonly Color PrimaryText = Color.FromArgb(235, 241, 250);
    private static readonly Color SecondaryText = Color.FromArgb(157, 170, 190);
    private static readonly Color Accent = Color.FromArgb(81, 209, 232);
    private static readonly Color ErrorText = Color.FromArgb(255, 196, 120);
    private readonly Label _title;
    private readonly Label _intro;
    private readonly Label _fieldLabel;
    private readonly InputSurface _inputSurface;
    private readonly Label _status;

    internal TextBox LicenseInput { get; }
    internal Button OpenButton { get; }
    internal Button CancelAccessButton { get; }

    internal LicenseAccessView()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(540, 340);
        BackColor = Canvas;
        ForeColor = PrimaryText;
        Font = new Font("Segoe UI", 10F);
        DoubleBuffered = true;

        var brand = MakeLabel("TURBORAMA", new Rectangle(28, 22, 112, 22),
            9F, FontStyle.Bold, PrimaryText);
        var edition = MakeLabel("SUITE", new Rectangle(143, 22, 58, 21),
            8F, FontStyle.Bold, Accent);
        edition.BackColor = Color.FromArgb(24, 48, 61);
        edition.TextAlign = ContentAlignment.MiddleCenter;
        _title = MakeLabel("Acessar EmulationStation", new Rectangle(26, 57, 490, 36),
            20F, FontStyle.Bold, PrimaryText);
        _intro = MakeLabel("Informações básicas de rede ajudam no diagnóstico.\n"
            + "Trocar de rede não bloqueia o acesso.",
            new Rectangle(28, 99, 484, 36), 9F, FontStyle.Regular, SecondaryText);
        _fieldLabel = MakeLabel("Licença Suite", new Rectangle(28, 140, 484, 21),
            9.5F, FontStyle.Bold, PrimaryText);

        _inputSurface = new InputSurface
        {
            Bounds = new Rectangle(28, 166, 484, 44),
            BackColor = Canvas,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TabStop = false
        };
        LicenseInput = new TextBox
        {
            Bounds = new Rectangle(13, 10, 458, 24),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.None,
            BackColor = InputSurface.Fill,
            ForeColor = PrimaryText,
            Font = new Font("Segoe UI", 11F),
            MaxLength = 64,
            PlaceholderText = "TS-…",
            AccessibleName = "Licença Suite",
            AccessibleDescription = "Identificador da licença já ativada no TurboRama Suite.",
            TabIndex = 0
        };
        LicenseInput.Enter += (_, _) => _inputSurface.SetFocusBorder(true);
        LicenseInput.Leave += (_, _) => _inputSurface.SetFocusBorder(false);
        _inputSurface.Controls.Add(LicenseInput);

        _status = MakeLabel("Usa a ativação existente deste PC e desta conta do Windows.\n"
            + "Não ativa outro computador.", new Rectangle(28, 225, 484, 44),
            9F, FontStyle.Regular, SecondaryText);
        _status.AccessibleName = "Estado do acesso";
        _status.AccessibleRole = AccessibleRole.StaticText;

        CancelAccessButton = MakeButton("Sair", new Rectangle(28, 281, 100, 38), false);
        CancelAccessButton.TabIndex = 2;
        OpenButton = MakeButton("Entrar", new Rectangle(356, 281, 156, 38), true);
        OpenButton.TabIndex = 1;

        Controls.AddRange([brand, edition, _title, _intro, _fieldLabel,
            _inputSurface, _status, CancelAccessButton, OpenButton]);
    }

    internal void SetStatus(string text, bool isError = false)
    {
        _status.Text = text;
        _status.ForeColor = isError ? ErrorText : SecondaryText;
        _status.AccessibleDescription = text;
    }

    internal void SetBusy(bool busy)
    {
        LicenseInput.Enabled = !busy;
        OpenButton.Enabled = !busy;
        OpenButton.Text = busy ? "Conferindo…" : "Entrar";
        UseWaitCursor = busy;
        CancelAccessButton.UseWaitCursor = false;
    }

    internal void PresentUnavailable(string message)
    {
        _title.Text = "Acesso indisponível";
        _intro.Text = "Não foi possível abrir o EmulationStation agora.";
        _fieldLabel.Visible = false;
        _inputSurface.Visible = false;
        _status.SetBounds(28, 151, 484, 104);
        _status.Font = new Font("Segoe UI", 10F);
        SetStatus(message, isError: true);
        OpenButton.Visible = false;
        CancelAccessButton.Text = "Fechar";
        CancelAccessButton.SetBounds(356, 281, 156, 38);
    }

    private static Label MakeLabel(string text, Rectangle bounds, float size,
        FontStyle style, Color color) => new()
    {
        Text = text,
        Bounds = bounds,
        AutoSize = false,
        Font = new Font("Segoe UI", size, style),
        ForeColor = color,
        BackColor = Color.Transparent,
        UseMnemonic = false,
        TextAlign = ContentAlignment.MiddleLeft,
        Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
    };

    private static Button MakeButton(string text, Rectangle bounds, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Bounds = bounds,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            BackColor = primary ? Accent : Color.FromArgb(24, 33, 47),
            ForeColor = primary ? Color.FromArgb(12, 29, 39) : PrimaryText,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Bottom | (primary ? AnchorStyles.Right : AnchorStyles.Left)
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(49, 64, 85);
        button.FlatAppearance.MouseOverBackColor = primary
            ? Color.FromArgb(111, 221, 239) : Color.FromArgb(35, 46, 64);
        button.FlatAppearance.MouseDownBackColor = primary
            ? Color.FromArgb(49, 184, 209) : Color.FromArgb(42, 56, 76);
        return button;
    }

    private sealed class InputSurface : Panel
    {
        internal static readonly Color Fill = Color.FromArgb(24, 33, 48);
        private bool _focused;

        internal InputSurface() => DoubleBuffered = true;

        internal void SetFocusBorder(bool focused)
        {
            _focused = focused;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float inset = Math.Max(1F, DeviceDpi / 96F);
            float radius = 8F * DeviceDpi / 96F;
            var rectangle = new RectangleF(inset, inset, Width - 2 * inset, Height - 2 * inset);
            using var path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, radius, radius, 180, 90);
            path.AddArc(rectangle.Right - radius, rectangle.Top, radius, radius, 270, 90);
            path.AddArc(rectangle.Right - radius, rectangle.Bottom - radius, radius, radius, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - radius, radius, radius, 90, 90);
            path.CloseFigure();
            using var fill = new SolidBrush(Fill);
            using var border = new Pen(_focused ? Accent : Color.FromArgb(52, 68, 89), inset);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }
    }
}
