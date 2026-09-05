using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace InstallerHost
{
    internal static class TurboramaPremiumUi
    {
        public static readonly Color Background = TurboramaPremiumTheme.Background;
        public static readonly Color Shell = TurboramaPremiumTheme.Shell;
        public static readonly Color PanelDark = TurboramaPremiumTheme.Surface;
        public static readonly Color PanelMid = TurboramaPremiumTheme.SurfaceRaised;
        public static readonly Color Card = TurboramaPremiumTheme.Surface;
        public static readonly Color CardHot = TurboramaPremiumTheme.SurfaceRaised;
        public static readonly Color Border = TurboramaPremiumTheme.BorderStrong;
        public static readonly Color BorderSoft = TurboramaPremiumTheme.Border;
        public static readonly Color Cyan = TurboramaPremiumTheme.Cyan;
        public static readonly Color CyanSoft = TurboramaPremiumTheme.CyanSoft;
        public static readonly Color Green = TurboramaPremiumTheme.Green;
        public static readonly Color GreenSoft = TurboramaPremiumTheme.GreenSoft;
        public static readonly Color GreenDeep = Color.FromArgb(18, 104, 76);
        public static readonly Color Violet = TurboramaPremiumTheme.Violet;
        public static readonly Color Text = TurboramaPremiumTheme.Text;
        public static readonly Color Muted = TurboramaPremiumTheme.TextMuted;
        public static readonly Color Dim = TurboramaPremiumTheme.Dim;
        public static readonly Color Warning = TurboramaPremiumTheme.Warning;
        public static readonly Color AccentRed = TurboramaPremiumTheme.Violet;
        public static readonly Color AccentRedSoft = Color.FromArgb(83, 54, 126);

        private const int WindowWidth = 1080;
        private const int WindowHeight = 680;
        private const int MinimumWindowWidth = 920;
        private const int MinimumWindowHeight = 590;
        private const int SidebarWidth = 284;
        private const int CompactSidebarWidth = 246;
        private const int FooterHeight = 76;
        private static readonly ConditionalWeakTable<UserControl, NeonDpiViewport> pendingViewports =
            new ConditionalWeakTable<UserControl, NeonDpiViewport>();
        private static readonly ConditionalWeakTable<UserControl, PendingLayoutState> pendingLayouts =
            new ConditionalWeakTable<UserControl, PendingLayoutState>();
        private static Image cachedFooterBannerImage;
        private static Image cachedSidebarLogoImage;
        private static DateTime lastPrerequisitePolishUtc = DateTime.MinValue;

        static TurboramaPremiumUi()
        {
            try
            {
                // Segurança: se a tela de requisitos não chamar ApplyTheme diretamente,
                // o banner ainda será aplicado automaticamente somente nela.
                Application.Idle += delegate
                {
                    ApplyPrerequisiteBannerToOpenForms();
                };
            }
            catch
            {
            }
        }

        public static void ApplyTheme(Control root)
        {
            if (root == null)
            {
                return;
            }

            try
            {
                TurboramaPremiumTheme.Apply(root);

                // O banner horizontal da LZ deve aparecer SOMENTE na tela 03
                // de requisitos do sistema. Welcome, licença, instalação e conclusão
                // continuam sem esse banner de imagem no rodapé.
                if (IsPrerequisiteScreen(root))
                {
                    PolishPrerequisiteScreen(root);
                    AddPrerequisiteFooterBanner(root);
                }

                BringNavigationButtonsToFront(root);
                root.Invalidate(true);
            }
            catch
            {
            }
        }

        public static void ApplyLicense(Control root)
        {
            ApplyTheme(root);
        }

        public static void ApplyWelcomeV2(UserControl root)
        {
            ApplyWelcomeV3(root);
        }

        public static void ApplyLicenseV2(UserControl root)
        {
            ApplyLicenseV3(root);
        }

        public static void ApplyInstallV2(UserControl root)
        {
            ApplyInstallV3(root);
        }

        public static void ApplyFinishV2(UserControl root, string installPath)
        {
            ApplyFinishV3(root, installPath);
        }

        public static void ApplyWelcomeV3(UserControl root)
        {
            if (root == null)
            {
                return;
            }

            root.SuspendLayout();
            try
            {
                Label title = FindControl(root, "lblWelcomeTitle") as Label;
                Label desc = FindControl(root, "lblWelcomeDesc") as Label;
                Button next = FindControl(root, "btnNext") as Button;
                Button cancel = FindControl(root, "btnCancel") as Button;
                PictureBox banner = FindControl(root, "bannerPictureBox") as PictureBox;

                PreparePremiumShell(root);
                CreateWelcomeSidebar(root, banner);
                Panel content = CreateContent(root, "Bem-vindo ao Sistema Turborama", "LZ Games", "Prazer, diversão e os melhores jogos em uma plataforma organizada, moderna e confiável.");

                PremiumPanel hero = MakeCard("__welcomeHero", 34, 118, 610, 178, CardHot, Border, 16);
                hero.AccentColor = Cyan;
                hero.ShowGrid = true;
                content.Controls.Add(hero);

                if (title != null)
                {
                    title.Parent = hero;
                    title.Text = "Sistema Turborama";
                    title.Font = TurboramaPremiumTheme.CreateFont(20f, FontStyle.Bold);
                    title.ForeColor = Text;
                    title.BackColor = Color.Transparent;
                    title.Location = new Point(26, 22);
                    title.Size = new Size(548, 38);
                }

                if (desc != null)
                {
                    desc.Parent = hero;
                    desc.Text = "A LZ Games e Informática apresenta o Sistema Turborama, desenvolvido para oferecer prazer, diversão e acesso aos melhores jogos em uma experiência premium.\r\n\r\nEste instalador irá preparar o computador e configurar os componentes recomendados para estabilidade, desempenho e compatibilidade.";
                    desc.Font = TurboramaPremiumTheme.CreateFont(9.7f, FontStyle.Regular);
                    desc.ForeColor = Color.FromArgb(220, 232, 222);
                    desc.BackColor = Color.Transparent;
                    desc.Location = new Point(28, 72);
                    desc.Size = new Size(548, 92);
                }

                AddFeatureCard(content, 34, 318, 188, 96, "DIVERSÃO PREMIUM", "Uma experiência pensada para prazer, entretenimento e jogos inesquecíveis.");
                AddFeatureCard(content, 244, 318, 188, 96, "SISTEMA ORGANIZADO", "Instalação clara, componentes preparados e ambiente pronto para uso.");
                AddFeatureCard(content, 454, 318, 188, 96, "PRONTO PARA JOGAR", "Tudo configurado para você aproveitar o Turborama com praticidade.");

                PremiumPanel signature = MakeCard("__welcomeSignature", 34, 436, 610, 58, PanelMid, BorderSoft, 12);
                signature.AccentColor = Violet;
                content.Controls.Add(signature);
                signature.Controls.Add(MakeLabel("LZ Games e Informática", 22, 11, 240, 20, Text, 9.5f, true));
                signature.Controls.Add(MakeLabel("Tecnologia, diversão e os melhores jogos em um só sistema.", 22, 32, 500, 18, GreenSoft, 8.6f, false));

                SetButtonText(next, "Avançar >");
                SetButtonText(cancel, "Cancelar");
                AddFooter(root, null, next, cancel);
                StylePrimaryButton(next);
                StyleDangerButton(cancel);
                FinalizePremiumScreen(root);
            }
            catch
            {
                ApplyTheme(root);
            }
            finally
            {
                root.ResumeLayout(false);
                root.PerformLayout();
            }
        }

        public static void ApplyLicenseV3(UserControl root)
        {
            if (root == null)
            {
                return;
            }

            root.SuspendLayout();
            try
            {
                Control header = FindControl(root, "wizardHeader");
                Control license = FindControl(root, "licenseTextBox");
                CheckBox agree = FindControl(root, "chkAgree") as CheckBox;
                Button back = FindControl(root, "btnBack") as Button;
                Button next = FindControl(root, "btnNext") as Button;
                Button cancel = FindControl(root, "btnCancel") as Button;

                PreparePremiumShell(root);
                CreateSidebar(root, 1, "LICENÇA", "Contrato", null);
                Panel content = CreateContent(root, "Contrato de Licença", "Sistema Turborama", "Leia os termos de uso antes de continuar com a instalação.");

                if (header != null)
                {
                    header.Visible = false;
                }

                PremiumPanel introCard = MakeCard("__licenseIntro", 34, 106, 610, 86, CardHot, Border, 14);
                introCard.AccentColor = Violet;
                content.Controls.Add(introCard);
                introCard.Controls.Add(MakeLabel("LZ Games e Informática", 24, 16, 300, 22, Text, 10.2f, true));
                introCard.Controls.Add(MakeLabel("O Sistema Turborama foi desenvolvido para entregar prazer, diversão e os melhores jogos com uma experiência moderna, organizada e confiável.", 24, 42, 540, 34, Muted, 8.7f, false));

                PremiumPanel licenseCard = MakeCard("__licenseCard", 34, 210, 610, 284, Card, Border, 14);
                licenseCard.AccentColor = Cyan;
                content.Controls.Add(licenseCard);
                licenseCard.Controls.Add(MakeLabel("TERMOS DO SISTEMA TURBORAMA", 22, 14, 360, 22, Text, 9.5f, true));

                if (license != null)
                {
                    license.Parent = licenseCard;
                    license.Location = new Point(22, 44);
                    license.Size = new Size(566, 188);
                    license.Text = BuildLicenseText();
                    StyleLicenseBox(license);
                }

                if (agree != null)
                {
                    agree.Parent = licenseCard;
                    agree.Text = "Li e aceito os termos do contrato de licença";
                    agree.Location = new Point(22, 244);
                    agree.Size = new Size(540, 26);
                    StyleCheckBox(agree);
                    agree.BackColor = Color.Transparent;
                }

                SetButtonText(back, "< Voltar");
                SetButtonText(next, "Avançar >");
                SetButtonText(cancel, "Cancelar");
                AddFooter(root, back, next, cancel);
                StyleSecondaryButton(back);
                StylePrimaryButton(next);
                StyleDangerButton(cancel);
                FinalizePremiumScreen(root);
            }
            catch
            {
                ApplyTheme(root);
            }
            finally
            {
                root.ResumeLayout(false);
                root.PerformLayout();
            }
        }

        public static void ApplyInstallV3(UserControl root)
        {
            if (root == null)
            {
                return;
            }

            root.SuspendLayout();
            try
            {
                Control header = FindControl(root, "wizardHeader");
                Label info = FindControl(root, "txtInfo") as Label;
                Label selectFolder = FindControl(root, "lblSelectFolder") as Label;
                TextBox folder = FindControl(root, "txtFolder") as TextBox;
                Button browse = FindControl(root, "btnBrowse") as Button;
                ProgressBar progress = FindControl(root, "progressBar") as ProgressBar;
                Label hint = FindControl(root, "lblFolderHint") as Label;
                Button back = FindControl(root, "btnBack") as Button;
                Button install = FindControl(root, "btnInstall") as Button;
                Button cancel = FindControl(root, "btnCancel") as Button;

                PreparePremiumShell(root);
                CreateSidebar(root, 3, "INSTALAÇÃO", "Local", null);
                Panel content = CreateContent(root, "Local de Instalação", "Sistema Turborama", "Escolha onde a plataforma será instalada para iniciar sua experiência de jogos.");

                if (header != null)
                {
                    header.Visible = false;
                }

                PremiumPanel pathCard = MakeCard("__installPathCard", 34, 112, 610, 196, CardHot, Border, 16);
                pathCard.AccentColor = Violet;
                content.Controls.Add(pathCard);
                pathCard.Controls.Add(MakeLabel("PASTA DE INSTALAÇÃO", 24, 18, 350, 22, Text, 10f, true));
                pathCard.Controls.Add(MakeLabel("Recomendamos manter a pasta padrão para melhor organização, compatibilidade e funcionamento do Sistema Turborama.", 24, 42, 530, 34, Muted, 8.7f, false));

                if (selectFolder != null)
                {
                    selectFolder.Parent = pathCard;
                    selectFolder.Text = "Selecione a pasta de instalação:";
                    selectFolder.Location = new Point(24, 86);
                    selectFolder.Size = new Size(520, 18);
                    selectFolder.Font = TurboramaPremiumTheme.CreateFont(8.7f, FontStyle.Bold);
                    selectFolder.ForeColor = Text;
                    selectFolder.BackColor = Color.Transparent;
                }

                if (folder != null)
                {
                    folder.Parent = pathCard;
                    folder.Location = new Point(24, 112);
                    folder.Size = new Size(438, 25);
                    StyleTextBox(folder);
                }

                if (browse != null)
                {
                    browse.Parent = pathCard;
                    browse.Text = "Procurar...";
                    browse.Location = new Point(474, 109);
                    browse.Size = new Size(108, 30);
                    StyleSecondaryButton(browse);
                }

                if (hint != null)
                {
                    hint.Parent = pathCard;
                    hint.Text = "Espaço mínimo necessário: 3,38 GB\r\nEvite pastas com caracteres especiais ou caminhos muito longos.";
                    hint.Location = new Point(24, 150);
                    hint.Size = new Size(540, 38);
                    hint.Font = TurboramaPremiumTheme.CreateFont(8.7f, FontStyle.Regular);
                    hint.ForeColor = Color.FromArgb(200, 214, 202);
                    hint.BackColor = Color.Transparent;
                }

                PremiumPanel statusCard = MakeCard("__installStatusCard", 34, 330, 610, 120, PanelMid, BorderSoft, 14);
                statusCard.AccentColor = Green;
                content.Controls.Add(statusCard);
                statusCard.Controls.Add(MakeLabel("STATUS DA PREPARAÇÃO", 24, 16, 380, 22, Text, 9.6f, true));
                statusCard.Controls.Add(MakeLabel("A LZ Games e Informática irá preparar o ambiente, extrair os arquivos e validar o executável principal do Sistema Turborama.", 24, 42, 540, 32, Muted, 8.6f, false));

                if (info != null)
                {
                    info.Parent = statusCard;
                    info.Text = "Pronto para instalar. Clique em Instalar para começar.";
                    info.Location = new Point(24, 84);
                    info.Size = new Size(540, 22);
                    info.Font = TurboramaPremiumTheme.CreateFont(9.2f, FontStyle.Bold);
                    info.ForeColor = Green;
                    info.BackColor = Color.Transparent;
                    info.Visible = true;
                }

                if (progress != null)
                {
                    progress.Parent = statusCard;
                    progress.Location = new Point(24, 84);
                    progress.Size = new Size(540, 20);
                    progress.Visible = false;
                    StyleProgress(progress);

                    NeonProgressMirror progressMirror = new NeonProgressMirror();
                    progressMirror.Name = "__installNeonProgress";
                    progressMirror.Location = progress.Location;
                    progressMirror.Size = progress.Size;
                    progressMirror.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
                    progressMirror.AccessibleName = "Progresso da instalação";
                    progressMirror.Bind(progress);
                    statusCard.Controls.Add(progressMirror);
                    progressMirror.BringToFront();
                }

                SetButtonText(back, "< Voltar");
                SetButtonText(install, "Instalar");
                SetButtonText(cancel, "Cancelar");
                AddFooter(root, back, install, cancel);
                StyleSecondaryButton(back);
                StylePrimaryButton(install);
                StyleDangerButton(cancel);
                FinalizePremiumScreen(root);
            }
            catch
            {
                ApplyTheme(root);
            }
            finally
            {
                root.ResumeLayout(false);
                root.PerformLayout();
            }
        }

        public static void ApplyFinishV3(UserControl root, string installPath)
        {
            if (root == null)
            {
                return;
            }

            root.SuspendLayout();
            try
            {
                Label message = FindControl(root, "lblMessage") as Label;
                Label desc = FindControl(root, "lblWelcomeDesc") as Label;
                CheckBox run = FindControl(root, "chkRunApp") as CheckBox;
                FlowLayoutPanel links = FindControl(root, "linkPanel") as FlowLayoutPanel;
                Button finish = FindControl(root, "btnFinish") as Button;
                Button back = FindControl(root, "btnBack") as Button;
                Button cancel = FindControl(root, "btnCancel") as Button;
                PictureBox banner = FindControl(root, "bannerPictureBox") as PictureBox;

                PreparePremiumShell(root);
                CreateSidebar(root, 5, "CONCLUSÃO", "Pronto", banner);
                Panel content = CreateContent(root, "Instalação concluída", "Sistema Turborama", "Pronto para iniciar e aproveitar prazer, diversão e os melhores jogos.");

                PremiumPanel finishCard = MakeCard("__finishCard", 34, 118, 610, 254, CardHot, Border, 16);
                finishCard.AccentColor = Green;
                finishCard.ShowGrid = true;
                content.Controls.Add(finishCard);

                Label successIcon = MakeLabel("✓", 28, 26, 54, 54, Green, 30f, true);
                successIcon.TextAlign = ContentAlignment.MiddleCenter;
                successIcon.BorderStyle = BorderStyle.FixedSingle;
                finishCard.Controls.Add(successIcon);

                if (message != null)
                {
                    message.Parent = finishCard;
                    message.Text = "Instalação concluída com sucesso";
                    message.Location = new Point(98, 28);
                    message.Size = new Size(460, 36);
                    message.Font = TurboramaPremiumTheme.CreateFont(16.5f, FontStyle.Bold);
                    message.ForeColor = Text;
                    message.BackColor = Color.Transparent;
                }

                if (desc != null)
                {
                    desc.Parent = finishCard;
                    desc.Text = "O Sistema Turborama, da LZ Games e Informática, está pronto para proporcionar prazer, diversão e acesso aos melhores jogos em uma experiência moderna e organizada.";
                    desc.Location = new Point(100, 76);
                    desc.Size = new Size(470, 58);
                    desc.Font = TurboramaPremiumTheme.CreateFont(9.4f, FontStyle.Regular);
                    desc.ForeColor = Muted;
                    desc.BackColor = Color.Transparent;
                }

                finishCard.Controls.Add(MakeLabel("LOCAL INSTALADO", 100, 148, 260, 20, Text, 8.8f, true));
                finishCard.Controls.Add(MakeLabel(string.IsNullOrEmpty(installPath) ? "C:\\Turborama" : installPath, 100, 170, 470, 22, GreenSoft, 8.8f, false));

                if (run != null)
                {
                    run.Parent = finishCard;
                    run.Text = "Executar o Sistema Turborama agora";
                    run.Location = new Point(100, 208);
                    run.Size = new Size(390, 26);
                    StyleCheckBox(run);
                    run.BackColor = Color.Transparent;
                }

                if (links != null)
                {
                    links.Parent = content;
                    links.Location = new Point(34, 386);
                    links.Size = new Size(610, 30);
                    links.BackColor = Color.Transparent;
                }

                Button openFolder = new Button();
                openFolder.Text = "Abrir pasta";
                openFolder.Name = "btnOpenInstallFolder";
                openFolder.Size = new Size(112, 30);
                openFolder.Location = new Point(532, 386);
                openFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                StyleSecondaryButton(openFolder);
                string pathToOpen = installPath;
                openFolder.Click += delegate(object sender, EventArgs e)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(pathToOpen) && Directory.Exists(pathToOpen))
                        {
                            Process.Start(new ProcessStartInfo(pathToOpen) { UseShellExecute = true });
                        }
                    }
                    catch
                    {
                    }
                };
                content.Controls.Add(openFolder);

                SetButtonText(finish, "Concluir");
                AddFooter(root, null, finish, cancel);
                if (back != null)
                {
                    back.Visible = false;
                }
                if (cancel != null)
                {
                    cancel.Visible = false;
                }
                StylePrimaryButton(finish);
                FinalizePremiumScreen(root);
            }
            catch
            {
                ApplyTheme(root);
            }
            finally
            {
                root.ResumeLayout(false);
                root.PerformLayout();
            }
        }

        private static void PreparePremiumShell(UserControl root)
        {
            EnsureLargeHost(root);

            NeonDpiViewport previousViewport = FindControl(root, "__premiumViewport") as NeonDpiViewport;
            if (previousViewport != null)
            {
                PreserveDesignerControls(root, previousViewport);
                root.Controls.Remove(previousViewport);
                previousViewport.Dispose();
            }

            Control[] existing = new Control[root.Controls.Count];
            root.Controls.CopyTo(existing, 0);
            foreach (Control control in existing)
            {
                Panel p = control as Panel;
                if (p != null && (p.Name == "__premiumSidebar" || p.Name == "__premiumContent" || p.Name == "__premiumFooter"))
                {
                    PreserveDesignerControls(root, p);
                    root.Controls.Remove(p);
                    p.Dispose();
                }
            }

            pendingViewports.Remove(root);
            NeonDpiViewport viewport = new NeonDpiViewport();
            viewport.Name = "__premiumViewport";
            viewport.Size = new Size(WindowWidth, WindowHeight);
            viewport.SuspendLayout();
            pendingViewports.Add(root, viewport);

            root.BackColor = Background;
            root.ForeColor = Text;
            root.Dock = DockStyle.Fill;

            Control panel1 = FindControl(root, "panel1");
            if (panel1 != null)
            {
                panel1.Visible = false;
            }

            Control wizardHeader = FindControl(root, "wizardHeader");
            if (wizardHeader != null)
            {
                wizardHeader.Visible = false;
            }
        }

        private static NeonDpiViewport GetPremiumViewport(UserControl root)
        {
            NeonDpiViewport viewport;
            if (root != null && pendingViewports.TryGetValue(root, out viewport))
            {
                return viewport;
            }

            return root == null ? null : FindControl(root, "__premiumViewport") as NeonDpiViewport;
        }

        private static void PreserveDesignerControls(UserControl root, Control premiumContainer)
        {
            string[] names = new string[]
            {
                "lblWelcomeTitle", "lblWelcomeDesc", "lblMessage", "wizardHeader", "licenseTextBox", "chkAgree",
                "txtInfo", "lblSelectFolder", "txtFolder", "btnBrowse", "progressBar", "lblFolderHint", "chkRunApp",
                "linkPanel", "bannerPictureBox", "btnBack", "btnNext", "btnInstall", "btnFinish", "btnCancel"
            };

            foreach (string name in names)
            {
                Control preserved = FindControl(premiumContainer, name);
                if (preserved != null && !preserved.IsDisposed)
                {
                    preserved.Parent = root;
                }
            }
        }

        private static void EnsureLargeHost(UserControl root)
        {
            try
            {
                Form form = root.FindForm();
                if (form != null)
                {
                    Rectangle workArea = Screen.FromControl(form).WorkingArea;
                    Size minimum = ClampMinimumToWorkingArea(form.MinimumSize, workArea);
                    form.MinimumSize = minimum;
                    Rectangle fittedBounds = FitWindowBoundsToWorkingArea(form.Bounds, minimum, workArea);
                    if (form.WindowState == FormWindowState.Normal && form.Bounds != fittedBounds)
                    {
                        form.Bounds = fittedBounds;
                    }
                    form.BackColor = Background;
                    form.ForeColor = Text;
                    form.Text = "LZ Games e Informática - Sistema Turborama";
                    form.MaximizeBox = true;
                    form.SizeGripStyle = SizeGripStyle.Show;
                    root.Dock = DockStyle.Fill;
                }
                else
                {
                    // A root ainda será dimensionada pelo Form host. Não converta
                    // novamente: o próprio AutoScaleMode.Dpi já conhece a escala.
                    root.Dock = DockStyle.Fill;
                }
            }
            catch
            {
                root.Dock = DockStyle.Fill;
            }
        }

        internal static Size ClampMinimumToWorkingArea(Size autoScaledMinimum, Rectangle workingArea)
        {
            int availableWidth = Math.Max(1, workingArea.Width);
            int availableHeight = Math.Max(1, workingArea.Height);
            int minimumWidth = Math.Min(Math.Max(1, autoScaledMinimum.Width), availableWidth);
            int minimumHeight = Math.Min(Math.Max(1, autoScaledMinimum.Height), availableHeight);
            return new Size(minimumWidth, minimumHeight);
        }

        internal static Rectangle FitWindowBoundsToWorkingArea(Rectangle requestedBounds, Size minimumSize, Rectangle workingArea)
        {
            int availableWidth = Math.Max(1, workingArea.Width);
            int availableHeight = Math.Max(1, workingArea.Height);
            int width = Math.Min(Math.Max(Math.Max(1, requestedBounds.Width), minimumSize.Width), availableWidth);
            int height = Math.Min(Math.Max(Math.Max(1, requestedBounds.Height), minimumSize.Height), availableHeight);
            int maximumLeft = workingArea.Right - width;
            int maximumTop = workingArea.Bottom - height;
            int left = Math.Max(workingArea.Left, Math.Min(requestedBounds.Left, maximumLeft));
            int top = Math.Max(workingArea.Top, Math.Min(requestedBounds.Top, maximumTop));
            return new Rectangle(left, top, width, height);
        }

        private static Panel CreateSidebar(UserControl root, int activeIndex, string stage, string status, PictureBox banner)
        {
            NeonDpiViewport viewport = GetPremiumViewport(root);
            NeonSurfacePanel sidebar = new NeonSurfacePanel();
            sidebar.Name = "__premiumSidebar";
            sidebar.Left = 0;
            sidebar.Top = 0;
            sidebar.Width = SidebarWidth;
            sidebar.Height = viewport.Height - FooterHeight;
            sidebar.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;
            sidebar.SurfaceColor = TurboramaPremiumTheme.BackgroundDeep;
            sidebar.SurfaceColor2 = TurboramaPremiumTheme.Shell;
            sidebar.BorderColor = TurboramaPremiumTheme.Border;
            sidebar.AccentColor = Cyan;
            sidebar.CornerRadius = 0;
            sidebar.GlowStrength = 24;
            sidebar.ShowAccent = true;
            sidebar.ShowGrid = true;
            viewport.Controls.Add(sidebar);

            NeonBrandMark logo = new NeonBrandMark();
            logo.Name = "__brandMark";
            logo.Location = new Point(22, 18);
            logo.AccessibleName = "Turborama";
            sidebar.Controls.Add(logo);

            Label brand1 = MakeLabel("TURBORAMA", 96, 22, 164, 24, Text, 12.2f, true);
            sidebar.Controls.Add(brand1);
            Label brand2 = MakeLabel("LZ GAMES • INSTALLER", 96, 48, 166, 20, Cyan, 8.1f, true);
            sidebar.Controls.Add(brand2);

            Label line = new Label();
            line.BackColor = Cyan;
            line.Left = 22;
            line.Top = 88;
            line.Width = 142;
            line.Height = 1;
            sidebar.Controls.Add(line);

            Panel lineAccent = new Panel();
            lineAccent.BackColor = Violet;
            lineAccent.Left = 164;
            lineAccent.Top = 86;
            lineAccent.Width = 78;
            lineAccent.Height = 3;
            sidebar.Controls.Add(lineAccent);

            Label product = MakeLabel("CENTRAL DE PREPARAÇÃO", 22, 104, 230, 21, Text, 9.2f, true);
            sidebar.Controls.Add(product);
            Label slogan = MakeLabel("Ambiente gamer pronto, validado e organizado.", 22, 128, 230, 34, Muted, 8.3f, false);
            sidebar.Controls.Add(slogan);

            Image sidebarLogo = LoadSidebarLogoImage();
            Image artworkImage = sidebarLogo;
            if (artworkImage == null && banner != null && banner.Image != null)
            {
                artworkImage = CloneImageSafe(banner.Image);
            }

            if (banner != null)
            {
                banner.Visible = false;
                banner.Dock = DockStyle.None;
                banner.Anchor = AnchorStyles.None;
            }

            PremiumPanel artworkBox = MakeCard("__sidebarArtwork", 22, 174, 240, 126, PanelDark, BorderSoft, 12);
            artworkBox.AccentColor = Violet;
            artworkBox.ShowGrid = false;
            sidebar.Controls.Add(artworkBox);
            if (artworkImage != null)
            {
                PictureBox artworkPicture = new PictureBox();
                artworkPicture.Name = "__turboramaSidebarArtworkImage";
                artworkPicture.Image = artworkImage;
                artworkPicture.Dock = DockStyle.Fill;
                artworkPicture.SizeMode = PictureBoxSizeMode.Zoom;
                artworkPicture.BackColor = PanelDark;
                artworkPicture.BorderStyle = BorderStyle.None;
                artworkPicture.Margin = new Padding(0);
                artworkPicture.Padding = new Padding(0);
                artworkPicture.TabStop = false;
                artworkBox.Controls.Add(artworkPicture);
            }
            else
            {
                artworkBox.Controls.Add(MakeLabel("TURBORAMA", 24, 36, 180, 28, Cyan, 15f, true));
                artworkBox.Controls.Add(MakeLabel("NEXT-GEN GAME SYSTEM", 26, 70, 180, 22, Muted, 8f, false));
            }

            PremiumPanel stageCard = MakeCard("__stageCard", 22, 316, 240, 62, PanelMid, BorderSoft, 11);
            stageCard.AccentColor = Green;
            stageCard.GlowStrength = 20;
            sidebar.Controls.Add(stageCard);

            NeonLedIndicator statusLed = new NeonLedIndicator();
            statusLed.Name = "__stageLed";
            statusLed.LedColor = Green;
            statusLed.Location = new Point(13, 17);
            statusLed.Size = new Size(14, 14);
            stageCard.Controls.Add(statusLed);
            stageCard.Controls.Add(MakeLabel(stage, 34, 9, 184, 20, Text, 8.9f, true));
            stageCard.Controls.Add(MakeLabel(status.ToUpperInvariant(), 34, 31, 184, 18, Green, 7.6f, true));

            NeonStepRail stepRail = new NeonStepRail();
            stepRail.Name = "__stepRail";
            stepRail.ActiveIndex = activeIndex;
            stepRail.Left = 22;
            stepRail.Top = 392;
            stepRail.Width = 240;
            stepRail.Height = Math.Max(154, sidebar.Height - 408);
            stepRail.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
            stepRail.AccessibleName = "Progresso da instalação";
            sidebar.Controls.Add(stepRail);

            sidebar.BringToFront();
            return sidebar;
        }
        private static Panel CreateWelcomeSidebar(UserControl root, PictureBox banner)
        {
            NeonDpiViewport viewport = GetPremiumViewport(root);
            NeonSurfacePanel sidebar = new NeonSurfacePanel();
            sidebar.Name = "__premiumSidebar";
            sidebar.Left = 0;
            sidebar.Top = 0;
            sidebar.Width = SidebarWidth;
            sidebar.Height = viewport.Height - FooterHeight;
            sidebar.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;
            sidebar.SurfaceColor = TurboramaPremiumTheme.BackgroundDeep;
            sidebar.SurfaceColor2 = TurboramaPremiumTheme.Shell;
            sidebar.BorderColor = TurboramaPremiumTheme.Border;
            sidebar.AccentColor = Cyan;
            sidebar.CornerRadius = 0;
            sidebar.GlowStrength = 24;
            sidebar.ShowAccent = true;
            sidebar.ShowGrid = true;
            viewport.Controls.Add(sidebar);

            NeonBrandMark logo = new NeonBrandMark();
            logo.Name = "__brandMark";
            logo.Location = new Point(22, 18);
            logo.AccessibleName = "Turborama";
            sidebar.Controls.Add(logo);

            Label brand1 = MakeLabel("TURBORAMA", 96, 22, 164, 24, Text, 12.2f, true);
            sidebar.Controls.Add(brand1);
            Label brand2 = MakeLabel("LZ GAMES • INSTALLER", 96, 48, 166, 20, Cyan, 8.1f, true);
            sidebar.Controls.Add(brand2);

            Label line = new Label();
            line.BackColor = Cyan;
            line.Left = 22;
            line.Top = 88;
            line.Width = 142;
            line.Height = 1;
            sidebar.Controls.Add(line);

            Panel lineAccent = new Panel();
            lineAccent.BackColor = Violet;
            lineAccent.Left = 164;
            lineAccent.Top = 86;
            lineAccent.Width = 78;
            lineAccent.Height = 3;
            sidebar.Controls.Add(lineAccent);

            Label product = MakeLabel("NEXT-GEN GAME SYSTEM", 22, 104, 230, 21, Text, 9.2f, true);
            sidebar.Controls.Add(product);
            Label slogan = MakeLabel("Emuladores, runtimes e jogos em uma experiência única.", 22, 128, 230, 38, Muted, 8.3f, false);
            sidebar.Controls.Add(slogan);

            Image artworkImage = LoadSidebarLogoImage();
            if (artworkImage == null && banner != null && banner.Image != null)
            {
                artworkImage = CloneImageSafe(banner.Image);
            }

            if (banner != null)
            {
                banner.Visible = false;
                banner.Dock = DockStyle.None;
                banner.Anchor = AnchorStyles.None;
            }

            int artworkTop = 184;
            int artworkHeight = Math.Max(320, sidebar.Height - artworkTop - 16);
            Panel artworkHost = new Panel();
            artworkHost.Name = "__welcomeSidebarArtworkHost";
            artworkHost.Left = 18;
            artworkHost.Top = artworkTop;
            artworkHost.Width = sidebar.Width - 36;
            artworkHost.Height = artworkHeight;
            artworkHost.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
            artworkHost.BackColor = Color.Black;
            sidebar.Controls.Add(artworkHost);

            if (artworkImage != null)
            {
                PictureBox artworkPicture = new PictureBox();
                artworkPicture.Name = "__turboramaSidebarArtworkImage";
                artworkPicture.Image = artworkImage;
                artworkPicture.Dock = DockStyle.Fill;
                artworkPicture.SizeMode = PictureBoxSizeMode.Zoom;
                artworkPicture.BackColor = Color.Black;
                artworkPicture.BorderStyle = BorderStyle.None;
                artworkPicture.Margin = new Padding(0);
                artworkPicture.Padding = new Padding(0);
                artworkPicture.TabStop = false;
                artworkHost.Controls.Add(artworkPicture);
            }
            else
            {
                artworkHost.Controls.Add(MakeLabel("TURBORAMA", 24, Math.Max(96, (artworkHeight / 2) - 24), 180, 28, Green, 15f, true));
                artworkHost.Controls.Add(MakeLabel("Premium Games System", 26, Math.Max(128, (artworkHeight / 2) + 8), 180, 22, Muted, 8.5f, false));
            }

            PremiumPanel welcomeStatus = MakeCard("__welcomeStatus", 22, Math.Max(188, sidebar.Height - 76), 240, 54, PanelMid, BorderSoft, 11);
            welcomeStatus.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            welcomeStatus.AccentColor = Cyan;
            welcomeStatus.GlowStrength = 28;
            sidebar.Controls.Add(welcomeStatus);

            NeonLedIndicator readyLed = new NeonLedIndicator();
            readyLed.LedColor = Green;
            readyLed.Location = new Point(13, 17);
            readyLed.Size = new Size(14, 14);
            welcomeStatus.Controls.Add(readyLed);
            welcomeStatus.Controls.Add(MakeLabel("SISTEMA ONLINE", 34, 8, 170, 18, Green, 7.5f, true));
            welcomeStatus.Controls.Add(MakeLabel("ETAPA 01  •  BOAS-VINDAS", 34, 27, 184, 18, Text, 7.8f, true));

            sidebar.BringToFront();
            return sidebar;
        }
        private static Image CloneImageSafe(Image source)
        {
            if (source == null)
            {
                return null;
            }

            try
            {
                return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }

        private static Image LoadFooterBannerImage()
        {
            if (cachedFooterBannerImage != null)
            {
                return cachedFooterBannerImage;
            }

            try
            {
                string baseDir = Application.StartupPath;
                string[] candidates = new string[]
                {
                    Path.Combine(baseDir, "resources", "lz_footer_banner.png"),
                    Path.Combine(baseDir, "resources", "lz_footer_banner.jpg"),
                    Path.Combine(baseDir, "resources", "lz_footer_banner.jpeg"),
                    Path.Combine(baseDir, "lz_footer_banner.png"),
                    Path.Combine(baseDir, "lz_footer_banner.jpg"),
                    Path.Combine(baseDir, "lz_footer_banner.jpeg"),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "resources", "lz_footer_banner.png")),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "resources", "lz_footer_banner.jpg")),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "resources", "lz_footer_banner.jpeg"))
                };

                foreach (string candidate in candidates)
                {
                    if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                    {
                        byte[] bytes = File.ReadAllBytes(candidate);
                        using (MemoryStream ms = new MemoryStream(bytes))
                        {
                            using (Image original = Image.FromStream(ms))
                            {
                                cachedFooterBannerImage = new Bitmap(original);
                                return cachedFooterBannerImage;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static Image LoadSidebarLogoImage()
        {
            if (cachedSidebarLogoImage != null)
            {
                return cachedSidebarLogoImage;
            }

            try
            {
                string baseDir = Application.StartupPath;
                string[] candidates = new string[]
                {
                    Path.Combine(baseDir, "resources", "sidebarlogo.png"),
                    Path.Combine(baseDir, "resources", "turborama_sidebar_logo.png"),
                    Path.Combine(baseDir, "resources", "turborama_sidebar_logo.jpg"),
                    Path.Combine(baseDir, "resources", "turborama_sidebar_logo.jpeg"),
                    Path.Combine(baseDir, "turborama_sidebar_logo.png"),
                    Path.Combine(baseDir, "turborama_sidebar_logo.jpg"),
                    Path.Combine(baseDir, "turborama_sidebar_logo.jpeg"),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "resources", "sidebarlogo.png")),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "resources", "turborama_sidebar_logo.png")),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "resources", "turborama_sidebar_logo.jpg")),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "resources", "turborama_sidebar_logo.jpeg"))
                };

                foreach (string candidate in candidates)
                {
                    if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                    {
                        byte[] bytes = File.ReadAllBytes(candidate);
                        using (MemoryStream ms = new MemoryStream(bytes))
                        {
                            using (Image original = Image.FromStream(ms))
                            {
                                cachedSidebarLogoImage = new Bitmap(original);
                                return cachedSidebarLogoImage;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static Panel CreateContent(UserControl root, string title, string subtitle, string description)
        {
            NeonDpiViewport viewport = GetPremiumViewport(root);
            NeonBackdropPanel content = new NeonBackdropPanel();
            content.Name = "__premiumContent";
            content.Left = SidebarWidth;
            content.Top = 0;
            content.Width = viewport.Width - SidebarWidth;
            content.Height = viewport.Height - FooterHeight;
            content.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
            content.BackColor = Background;
            viewport.Controls.Add(content);

            Label titleLabel = MakeLabel(title, 34, 27, content.Width - 68, 38, Text, 19.5f, true);
            titleLabel.Name = "__pageTitle";
            titleLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            content.Controls.Add(titleLabel);

            Label subtitleLabel = MakeLabel(subtitle.ToUpperInvariant(), 36, 68, 150, 20, Cyan, 8.7f, true);
            subtitleLabel.Name = "__pageSubtitle";
            subtitleLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            subtitleLabel.AutoEllipsis = true;
            content.Controls.Add(subtitleLabel);

            Label underline = new Label();
            underline.BackColor = Cyan;
            underline.Location = new Point(36, 96);
            underline.Size = new Size(122, 2);
            content.Controls.Add(underline);

            Panel underlineAccent = new Panel();
            underlineAccent.BackColor = Violet;
            underlineAccent.Location = new Point(158, 92);
            underlineAccent.Size = new Size(12, 4);
            content.Controls.Add(underlineAccent);

            Label desc = MakeLabel(description, 190, 62, content.Width - 230, 42, Muted, 8.8f, false);
            desc.Name = "__pageDescription";
            desc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            content.Controls.Add(desc);

            return content;
        }
        private static void AddFooter(UserControl root, Button back, Button primary, Button cancel)
        {
            NeonDpiViewport viewport = GetPremiumViewport(root);
            NeonSurfacePanel footer = new NeonSurfacePanel();
            footer.Name = "__premiumFooter";
            footer.Left = 0;
            footer.Top = viewport.Height - FooterHeight;
            footer.Width = viewport.Width;
            footer.Height = FooterHeight;
            footer.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            footer.SurfaceColor = TurboramaPremiumTheme.Shell;
            footer.SurfaceColor2 = TurboramaPremiumTheme.BackgroundDeep;
            footer.BorderColor = BorderSoft;
            footer.AccentColor = Violet;
            footer.CornerRadius = 0;
            footer.GlowStrength = 18;
            footer.ShowAccent = true;
            viewport.Controls.Add(footer);

            Label line = new Label();
            line.BackColor = BorderSoft;
            line.Left = 0;
            line.Top = 0;
            line.Width = viewport.Width;
            line.Height = 1;
            line.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            footer.Controls.Add(line);

            Panel footerAccent = new Panel();
            footerAccent.BackColor = Violet;
            footerAccent.Left = 24;
            footerAccent.Top = 0;
            footerAccent.Width = 72;
            footerAccent.Height = 2;
            footer.Controls.Add(footerAccent);

            Label footerText = MakeLabel("LZ GAMES  /  TURBORAMA", 24, 17, 300, 20, Muted, 8.1f, true);
            footerText.Name = "__footerBrand";
            footer.Controls.Add(footerText);
            Label footerStatus = MakeLabel("●  AMBIENTE DE INSTALAÇÃO SEGURO", 24, 38, 340, 18, Green, 7.5f, true);
            footerStatus.Name = "__footerStatus";
            footer.Controls.Add(footerStatus);

            int right = viewport.Width - 24;
            if (cancel != null)
            {
                cancel.Parent = footer;
                cancel.Size = new Size(104, 36);
                cancel.Location = new Point(right - 104, 20);
                right -= 116;
                cancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
                cancel.AccessibleDescription = "Cancela e fecha o instalador";
                StyleDangerButton(cancel);
                cancel.Visible = true;
            }

            if (primary != null)
            {
                primary.Parent = footer;
                primary.Size = new Size(132, 36);
                primary.Location = new Point(right - 132, 20);
                right -= 144;
                primary.Anchor = AnchorStyles.Right | AnchorStyles.Top;
                primary.AccessibleDescription = "Continua para a próxima etapa";
                StylePrimaryButton(primary);
                primary.Visible = true;
            }

            if (back != null)
            {
                back.Parent = footer;
                back.Size = new Size(104, 36);
                back.Location = new Point(right - 104, 20);
                back.Anchor = AnchorStyles.Right | AnchorStyles.Top;
                back.AccessibleDescription = "Retorna para a etapa anterior";
                StyleSecondaryButton(back);
                back.Visible = true;
            }

            footer.BringToFront();
        }

        private static void FinalizePremiumScreen(UserControl root)
        {
            if (root == null)
            {
                return;
            }

            NeonDpiViewport viewport = GetPremiumViewport(root);
            if (viewport != null && viewport.Parent == null)
            {
                viewport.ResumeLayout(false);
                viewport.Dock = DockStyle.Fill;
                root.Controls.Add(viewport);
                viewport.BringToFront();
                viewport.CreateControl();
                viewport.PerformAutoScale();
                pendingViewports.Remove(root);
            }

            root.Resize -= PremiumRoot_Resize;
            root.Resize += PremiumRoot_Resize;

            TurboramaPremiumTheme.Apply(root);
            LayoutPremiumRoot(root);
            ConfigureKeyboardNavigation(root);
            BringNavigationButtonsToFront(root);
        }

        private static float GetDpiScale(Control control)
        {
            try
            {
                if (control != null && control.DeviceDpi > 0)
                {
                    return Math.Max(1f, control.DeviceDpi / 96f);
                }
            }
            catch
            {
            }
            return 1f;
        }

        private static int ScaleMetric(int value, float scale)
        {
            return Math.Max(1, (int)Math.Round(value * scale, MidpointRounding.AwayFromZero));
        }

        private static void PremiumRoot_Resize(object sender, EventArgs e)
        {
            UserControl root = sender as UserControl;
            if (root != null && !root.IsDisposed)
            {
                PendingLayoutState state = pendingLayouts.GetOrCreateValue(root);
                if (state.IsPending)
                {
                    return;
                }

                state.IsPending = true;
                try
                {
                    root.BeginInvoke(new MethodInvoker(delegate
                    {
                        state.IsPending = false;
                        if (!root.IsDisposed)
                        {
                            LayoutPremiumRoot(root);
                        }
                    }));
                }
                catch
                {
                    state.IsPending = false;
                    LayoutPremiumRoot(root);
                }
            }
        }

        private sealed class PendingLayoutState
        {
            public bool IsPending;
        }

        private static void LayoutPremiumRoot(UserControl root)
        {
            LayoutPremiumRoot(root, GetDpiScale(root));
        }

        internal static void LayoutPremiumRootForDpi(UserControl root, int dpi)
        {
            float scale = Math.Max(1f, dpi / 96f);
            LayoutPremiumRoot(root, scale);
        }

        private static void LayoutPremiumRoot(UserControl root, float scale)
        {
            if (root == null || root.Width <= 0 || root.Height <= 0)
            {
                return;
            }

            root.SuspendLayout();
            try
            {
                int sidebarWidth = root.Width < ScaleMetric(1020, scale)
                    ? ScaleMetric(CompactSidebarWidth, scale)
                    : ScaleMetric(SidebarWidth, scale);
                int footerHeight = Math.Min(ScaleMetric(FooterHeight, scale), Math.Max(ScaleMetric(64, scale), root.Height / 7));

                Control sidebar = FindControl(root, "__premiumSidebar");
                Control content = FindControl(root, "__premiumContent");
                Control footer = FindControl(root, "__premiumFooter");

                if (sidebar != null)
                {
                    sidebar.SetBounds(0, 0, sidebarWidth, Math.Max(0, root.Height - footerHeight));
                    LayoutSidebar(sidebar, scale);
                }

                if (content != null)
                {
                    content.SetBounds(sidebarWidth, 0, Math.Max(0, root.Width - sidebarWidth), Math.Max(0, root.Height - footerHeight));
                    LayoutContent(content, scale);
                }

                if (footer != null)
                {
                    footer.SetBounds(0, Math.Max(0, root.Height - footerHeight), root.Width, footerHeight);
                    LayoutFooter(footer, scale);
                }
            }
            finally
            {
                root.ResumeLayout(false);
            }
        }

        private static void LayoutSidebar(Control sidebar, float scale)
        {
            int leftMargin = ScaleMetric(22, scale);
            int innerWidth = Math.Max(ScaleMetric(170, scale), sidebar.Width - ScaleMetric(44, scale));
            Control artwork = FindControl(sidebar, "__sidebarArtwork");
            Control stageCard = FindControl(sidebar, "__stageCard");
            Control stepRail = FindControl(sidebar, "__stepRail");
            Control welcomeArtwork = FindControl(sidebar, "__welcomeSidebarArtworkHost");
            Control welcomeStatus = FindControl(sidebar, "__welcomeStatus");

            if (artwork != null)
            {
                int artworkHeight = sidebar.Height < ScaleMetric(560, scale) ? ScaleMetric(104, scale) : ScaleMetric(126, scale);
                artwork.SetBounds(leftMargin, ScaleMetric(174, scale), innerWidth, artworkHeight);
            }

            if (stageCard != null)
            {
                int stageTop = artwork == null ? ScaleMetric(292, scale) : artwork.Bottom + ScaleMetric(16, scale);
                stageCard.SetBounds(leftMargin, stageTop, innerWidth, ScaleMetric(62, scale));
                foreach (Control child in stageCard.Controls)
                {
                    Label label = child as Label;
                    if (label != null)
                    {
                        label.Width = Math.Max(ScaleMetric(70, scale), stageCard.Width - label.Left - ScaleMetric(10, scale));
                    }
                }
            }

            if (stepRail != null)
            {
                int railTop = stageCard == null ? ScaleMetric(350, scale) : stageCard.Bottom + ScaleMetric(12, scale);
                stepRail.SetBounds(leftMargin, railTop, innerWidth, Math.Max(ScaleMetric(96, scale), sidebar.Height - railTop - ScaleMetric(12, scale)));
            }

            if (welcomeArtwork != null)
            {
                int top = ScaleMetric(180, scale);
                welcomeArtwork.SetBounds(ScaleMetric(18, scale), top, Math.Max(ScaleMetric(170, scale), sidebar.Width - ScaleMetric(36, scale)), Math.Max(ScaleMetric(160, scale), sidebar.Height - top - ScaleMetric(16, scale)));
            }

            if (welcomeStatus != null)
            {
                welcomeStatus.SetBounds(leftMargin, Math.Max(ScaleMetric(188, scale), sidebar.Height - ScaleMetric(76, scale)), innerWidth, ScaleMetric(54, scale));
                welcomeStatus.BringToFront();
            }
        }

        private static void LayoutContent(Control content, float scale)
        {
            int margin = ScaleMetric(34, scale);
            int available = Math.Max(300, content.Width - (margin * 2));

            Control title = FindControl(content, "__pageTitle");
            Control subtitle = FindControl(content, "__pageSubtitle");
            Control description = FindControl(content, "__pageDescription");
            if (title != null) title.Width = available;
            if (subtitle != null) subtitle.Width = Math.Min(ScaleMetric(150, scale), available);
            if (description != null) description.Width = Math.Max(180, content.Width - description.Left - margin);

            string[] fullWidthCards = new string[]
            {
                "__welcomeHero",
                "__welcomeSignature",
                "__licenseIntro",
                "__licenseCard",
                "__installPathCard",
                "__installStatusCard",
                "__finishCard"
            };
            foreach (string cardName in fullWidthCards)
            {
                Control card = FindControl(content, cardName);
                if (card != null)
                {
                    card.Width = available;
                }
            }

            LayoutFeatureCards(content, margin, available, scale);
            LayoutLicenseCard(content, scale);
            LayoutInstallCards(content, scale);
            LayoutFinishCard(content, scale);
        }

        private static void LayoutFeatureCards(Control content, int left, int available, float scale)
        {
            Control[] cards = FindControls(content, "__feature");
            if (cards.Length == 0)
            {
                return;
            }

            int gap = ScaleMetric(18, scale);
            int width = Math.Max(ScaleMetric(120, scale), (available - (gap * (cards.Length - 1))) / cards.Length);
            for (int index = 0; index < cards.Length; index++)
            {
                cards[index].Left = left + (index * (width + gap));
                cards[index].Width = width;
                foreach (Control child in cards[index].Controls)
                {
                    Label label = child as Label;
                    if (label != null)
                    {
                        label.Width = Math.Max(ScaleMetric(40, scale), width - label.Left - ScaleMetric(12, scale));
                    }
                }
            }
        }

        private static void LayoutLicenseCard(Control content, float scale)
        {
            Control card = FindControl(content, "__licenseCard");
            if (card == null)
            {
                return;
            }

            Control license = FindControl(card, "licenseTextBox");
            Control agree = FindControl(card, "chkAgree");
            if (license != null) license.Width = Math.Max(ScaleMetric(220, scale), card.Width - ScaleMetric(44, scale));
            if (agree != null) agree.Width = Math.Max(ScaleMetric(220, scale), card.Width - ScaleMetric(44, scale));
        }

        private static void LayoutInstallCards(Control content, float scale)
        {
            Control pathCard = FindControl(content, "__installPathCard");
            if (pathCard != null)
            {
                Control browse = FindControl(pathCard, "btnBrowse");
                Control folder = FindControl(pathCard, "txtFolder");
                if (browse != null)
                {
                    browse.Left = Math.Max(ScaleMetric(260, scale), pathCard.Width - browse.Width - ScaleMetric(24, scale));
                }
                if (folder != null)
                {
                    int right = browse == null ? pathCard.Width - ScaleMetric(24, scale) : browse.Left - ScaleMetric(12, scale);
                    folder.Width = Math.Max(ScaleMetric(180, scale), right - folder.Left);
                }
            }

            Control statusCard = FindControl(content, "__installStatusCard");
            if (statusCard != null)
            {
                Control info = FindControl(statusCard, "txtInfo");
                Control progress = FindControl(statusCard, "progressBar");
                Control progressMirror = FindControl(statusCard, "__installNeonProgress");
                if (info != null) info.Width = Math.Max(ScaleMetric(180, scale), statusCard.Width - info.Left - ScaleMetric(24, scale));
                if (progress != null) progress.Width = Math.Max(ScaleMetric(180, scale), statusCard.Width - progress.Left - ScaleMetric(24, scale));
                if (progressMirror != null) progressMirror.Width = Math.Max(ScaleMetric(180, scale), statusCard.Width - progressMirror.Left - ScaleMetric(24, scale));
            }
        }

        private static void LayoutFinishCard(Control content, float scale)
        {
            Control card = FindControl(content, "__finishCard");
            if (card != null)
            {
                Control message = FindControl(card, "lblMessage");
                Control description = FindControl(card, "lblWelcomeDesc");
                Control run = FindControl(card, "chkRunApp");
                if (message != null) message.Width = Math.Max(ScaleMetric(180, scale), card.Width - message.Left - ScaleMetric(24, scale));
                if (description != null) description.Width = Math.Max(ScaleMetric(180, scale), card.Width - description.Left - ScaleMetric(24, scale));
                if (run != null) run.Width = Math.Max(ScaleMetric(180, scale), card.Width - run.Left - ScaleMetric(24, scale));
            }

            Control openFolder = FindControl(content, "btnOpenInstallFolder");
            if (openFolder != null)
            {
                openFolder.Left = Math.Max(ScaleMetric(34, scale), content.Width - openFolder.Width - ScaleMetric(48, scale));
            }
        }

        private static void LayoutFooter(Control footer, float scale)
        {
            Button back = FindControl(footer, "btnBack") as Button;
            Button primary = FindControl(footer, "btnNext") as Button;
            if (primary == null) primary = FindControl(footer, "btnInstall") as Button;
            if (primary == null) primary = FindControl(footer, "btnFinish") as Button;
            Button cancel = FindControl(footer, "btnCancel") as Button;

            int right = footer.Width - ScaleMetric(24, scale);
            if (cancel != null && cancel.Visible)
            {
                int width = ScaleMetric(104, scale);
                cancel.SetBounds(right - width, ScaleMetric(20, scale), width, ScaleMetric(36, scale));
                right -= ScaleMetric(116, scale);
            }
            if (primary != null && primary.Visible)
            {
                int width = ScaleMetric(132, scale);
                primary.SetBounds(right - width, ScaleMetric(20, scale), width, ScaleMetric(36, scale));
                right -= ScaleMetric(144, scale);
            }
            if (back != null && back.Visible)
            {
                int width = ScaleMetric(104, scale);
                back.SetBounds(right - width, ScaleMetric(20, scale), width, ScaleMetric(36, scale));
            }
        }

        private static void ConfigureKeyboardNavigation(UserControl root)
        {
            Form form = root.FindForm();
            if (form == null)
            {
                return;
            }

            Button primary = FindControl(root, "btnNext") as Button;
            if (primary == null) primary = FindControl(root, "btnInstall") as Button;
            if (primary == null) primary = FindControl(root, "btnFinish") as Button;
            Button back = FindControl(root, "btnBack") as Button;
            Button cancel = FindControl(root, "btnCancel") as Button;

            if (back != null) back.TabIndex = 100;
            if (primary != null) primary.TabIndex = 101;
            if (cancel != null) cancel.TabIndex = 102;

            if (primary != null && primary.Visible)
            {
                form.AcceptButton = primary;
                if (string.IsNullOrEmpty(primary.AccessibleName)) primary.AccessibleName = primary.Text;
            }
            else
            {
                form.AcceptButton = null;
            }
            if (cancel != null && cancel.Visible)
            {
                form.CancelButton = cancel;
                if (string.IsNullOrEmpty(cancel.AccessibleName)) cancel.AccessibleName = cancel.Text;
            }
            else
            {
                form.CancelButton = null;
            }
            form.KeyPreview = true;
        }

        private static Control[] FindControls(Control root, string name)
        {
            System.Collections.Generic.List<Control> result = new System.Collections.Generic.List<Control>();
            CollectControls(root, name, result);
            return result.ToArray();
        }

        private static void CollectControls(Control root, string name, System.Collections.Generic.List<Control> result)
        {
            if (root == null)
            {
                return;
            }

            foreach (Control child in root.Controls)
            {
                if (child.Name == name)
                {
                    result.Add(child);
                }
                if (child.HasChildren)
                {
                    CollectControls(child, name, result);
                }
            }
        }

        private static void PolishPrerequisiteScreen(Control root)
        {
            if (root == null || !IsPrerequisiteScreen(root))
            {
                return;
            }

            Control premiumPanel = FindControl(root, "turboramaPremiumPanel");
            if (premiumPanel == null)
            {
                return;
            }

            if (FindControl(premiumPanel, "__prereqNeonRail") != null)
            {
                return;
            }

            root.BackColor = Background;
            root.ForeColor = Text;
            premiumPanel.BackColor = Background;
            premiumPanel.ForeColor = Text;
            premiumPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            premiumPanel.SetBounds(0, 0, root.Width, Math.Max(280, root.Height - FooterHeight));

            Control left = FindPrerequisiteSidebar(premiumPanel);
            if (left != null)
            {
                left.BackColor = TurboramaPremiumTheme.BackgroundDeep;
                left.ForeColor = Text;
                left.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;

                Control brand = FindControl(left, "__prereqBrandMark");
                if (brand == null)
                {
                    NeonBrandMark mark = new NeonBrandMark();
                    mark.Name = "__prereqBrandMark";
                    mark.Location = new Point(18, 14);
                    mark.AccessibleName = "Turborama";
                    left.Controls.Add(mark);
                    mark.BringToFront();
                }

                NeonStepRail rail = FindControl(left, "__prereqNeonRail") as NeonStepRail;
                if (rail == null)
                {
                    rail = new NeonStepRail();
                    rail.Name = "__prereqNeonRail";
                    rail.ActiveIndex = 2;
                    rail.AccessibleName = "Progresso da instalação";
                    left.Controls.Add(rail);
                }
                rail.SetBounds(16, 210, Math.Max(130, left.Width - 28), Math.Max(120, left.Height - 224));
                rail.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                rail.BringToFront();
            }

            PolishPrerequisiteTree(premiumPanel, left);
            TurboramaPremiumTheme.Apply(root);
            BringNavigationButtonsToFront(root);
        }

        private static Control FindPrerequisiteSidebar(Control premiumPanel)
        {
            foreach (Control child in premiumPanel.Controls)
            {
                Panel panel = child as Panel;
                if (panel != null && panel.Left <= 2 && panel.Width <= 230 && panel.Height >= premiumPanel.Height - 12)
                {
                    return panel;
                }
            }
            return null;
        }

        private static void PolishPrerequisiteTree(Control parent, Control sidebar)
        {
            foreach (Control child in parent.Controls)
            {
                Panel panel = child as Panel;
                if (panel != null && panel != sidebar && !(panel is NeonSurfacePanel))
                {
                    bool isStripe = panel.Width <= 6 || panel.Height <= 4;
                    if (isStripe)
                    {
                        panel.BackColor = panel.Enabled ? Cyan : Dim;
                    }
                    else if (panel.Height <= 48)
                    {
                        panel.BackColor = PanelMid;
                        panel.ForeColor = Text;
                        panel.BorderStyle = BorderStyle.None;
                    }
                    else if (panel.BackColor != Color.Transparent)
                    {
                        panel.BackColor = PanelDark;
                    }
                }

                Label label = child as Label;
                if (label != null)
                {
                    label.BackColor = Color.Transparent;
                    if (label.ForeColor.G > 205 && label.ForeColor.R < 155)
                    {
                        label.ForeColor = Cyan;
                    }
                }

                CheckBox checkBox = child as CheckBox;
                if (checkBox != null)
                {
                    StyleCheckBox(checkBox);
                }

                if (child.HasChildren)
                {
                    PolishPrerequisiteTree(child, sidebar);
                }
            }
        }

        private static bool IsPrerequisiteScreen(Control root)
        {
            if (root == null)
            {
                return false;
            }

            try
            {
                string typeName = root.GetType().Name ?? string.Empty;
                string controlName = root.Name ?? string.Empty;
                return typeName.IndexOf("Prerequisite", StringComparison.OrdinalIgnoreCase) >= 0
                    || controlName.IndexOf("Prerequisite", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        public static void AddPrerequisiteFooterBanner(Control root)
        {
            if (root == null)
            {
                return;
            }

            // Nunca aplicar em Welcome, Licença, Instalação ou Conclusão.
            // Essa função é exclusiva da tela 03: PrerequisiteControl / Requisitos.
            if (!IsPrerequisiteScreen(root))
            {
                RemovePrerequisiteFooterBanner(root);
                return;
            }

            try
            {
                Image footerImage = LoadFooterBannerImage();
                if (footerImage == null)
                {
                    return;
                }

                PictureBox footerBanner = FindControl(root, "__lzPrerequisiteFooterBanner") as PictureBox;

                if (footerBanner == null)
                {
                    footerBanner = new PictureBox();
                    footerBanner.Name = "__lzPrerequisiteFooterBanner";
                    footerBanner.BackColor = Color.Black;
                    footerBanner.BorderStyle = BorderStyle.None;
                    footerBanner.Margin = new Padding(0);
                    footerBanner.Padding = new Padding(0);
                    footerBanner.TabStop = false;
                    footerBanner.SizeMode = PictureBoxSizeMode.Zoom;
                    root.Controls.Add(footerBanner);
                }
                else if (footerBanner.Image != null && !object.ReferenceEquals(footerBanner.Image, footerImage))
                {
                    try
                    {
                        footerBanner.Image.Dispose();
                    }
                    catch
                    {
                    }
                }

                footerBanner.Image = footerImage;

                // Tela 03 / Requisitos: o banner deve ficar no espaço vazio,
                // ACIMA dos botões (< Back / Next / Cancel), sem cobrir a navegação.
                // A posição é calculada pelo rodapé reservado do layout, mantendo
                // o banner organizado e separado dos botões.
                float dpiScale = GetDpiScale(root);
                int bannerHeight = root.Height < ScaleMetric(640, dpiScale) ? ScaleMetric(40, dpiScale) : ScaleMetric(60, dpiScale);
                int gapAboveButtons = ScaleMetric(8, dpiScale);
                int top = root.Height - ScaleMetric(FooterHeight, dpiScale) - gapAboveButtons - bannerHeight;

                Control premiumPanel = FindControl(root, "turboramaPremiumPanel");
                Control prerequisiteSidebar = premiumPanel == null ? null : FindPrerequisiteSidebar(premiumPanel);
                int contentLeft = prerequisiteSidebar == null ? 0 : prerequisiteSidebar.Right;

                footerBanner.Left = contentLeft;
                footerBanner.Top = top;
                footerBanner.Width = Math.Max(ScaleMetric(180, dpiScale), root.Width - contentLeft);
                footerBanner.Height = bannerHeight;
                footerBanner.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                footerBanner.Visible = true;

                footerBanner.BringToFront();
                BringNavigationButtonsToFront(root);
            }
            catch
            {
            }
        }

        private static void RemovePrerequisiteFooterBanner(Control root)
        {
            if (root == null)
            {
                return;
            }

            try
            {
                Control old = FindControl(root, "__lzPrerequisiteFooterBanner");
                if (old != null && old.Parent != null)
                {
                    old.Parent.Controls.Remove(old);
                    old.Dispose();
                }
            }
            catch
            {
            }
        }

        private static void ApplyPrerequisiteBannerToOpenForms()
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                if ((now - lastPrerequisitePolishUtc).TotalMilliseconds < 600d)
                {
                    return;
                }
                lastPrerequisitePolishUtc = now;

                foreach (Form form in Application.OpenForms)
                {
                    ApplyPrerequisiteBannerRecursive(form);
                }
            }
            catch
            {
            }
        }

        private static void ApplyPrerequisiteBannerRecursive(Control control)
        {
            if (control == null)
            {
                return;
            }

            try
            {
                UserControl userControl = control as UserControl;
                if (userControl != null && IsPrerequisiteScreen(userControl))
                {
                    PolishPrerequisiteScreen(userControl);
                    AddPrerequisiteFooterBanner(userControl);
                    return;
                }

                foreach (Control child in control.Controls)
                {
                    ApplyPrerequisiteBannerRecursive(child);
                }
            }
            catch
            {
            }
        }

        private static void AddStep(Panel sidebar, int index, int top, string number, string text, int activeIndex)
        {
            bool active = index == activeIndex;
            bool done = index < activeIndex;

            Panel marker = new Panel();
            marker.Left = 24;
            marker.Top = top + 6;
            marker.Width = 4;
            marker.Height = 10;
            marker.BackColor = active ? AccentRed : (done ? Green : Color.FromArgb(36, 54, 40));
            sidebar.Controls.Add(marker);

            Label num = MakeLabel(done ? "✓" : number, 34, top, 24, 20, active || done ? Green : Dim, 8f, true);
            sidebar.Controls.Add(num);
            Label label = MakeLabel(text, 64, top, 160, 20, active ? Text : (done ? Color.FromArgb(194, 214, 194) : Dim), 8.5f, active);
            sidebar.Controls.Add(label);
        }
        private static void AddFeatureCard(Control parent, int left, int top, int width, int height, string title, string description)
        {
            PremiumPanel card = MakeCard("__feature", left, top, width, height, PanelMid, BorderSoft, 12);
            card.AccentColor = title.IndexOf("DIVERSÃO", StringComparison.OrdinalIgnoreCase) >= 0
                ? Violet
                : (title.IndexOf("ORGANIZADO", StringComparison.OrdinalIgnoreCase) >= 0 ? Cyan : Green);
            card.Interactive = true;
            card.AccessibleName = title;
            card.AccessibleDescription = description;
            parent.Controls.Add(card);
            Panel accent = new Panel();
            accent.BackColor = card.AccentColor;
            accent.Left = width - 44;
            accent.Top = 0;
            accent.Width = 28;
            accent.Height = 2;
            accent.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            card.Controls.Add(accent);
            card.Controls.Add(MakeLabel(title, 16, 14, width - 28, 18, Green, 7.9f, true));
            card.Controls.Add(MakeLabel(description, 16, 40, width - 30, height - 44, Muted, 7.8f, false));
        }
        private static PremiumPanel MakeCard(string name, int left, int top, int width, int height, Color fill, Color border, int radius)
        {
            PremiumPanel panel = new PremiumPanel(fill, border, radius, true);
            panel.Name = name;
            panel.Left = left;
            panel.Top = top;
            panel.Width = width;
            panel.Height = height;
            panel.BackColor = Color.Transparent;
            panel.Interactive = true;
            return panel;
        }

        private static void ApplyThemeRecursive(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                try
                {
                    StyleControl(control);
                    if (control.HasChildren)
                    {
                        ApplyThemeRecursive(control);
                    }
                }
                catch
                {
                }
            }
        }

        private static void StyleControl(Control control)
        {
            if (control == null)
            {
                return;
            }

            Button button = control as Button;
            if (button != null)
            {
                StyleButton(button);
                return;
            }

            Label label = control as Label;
            if (label != null)
            {
                StyleLabel(label);
                return;
            }

            CheckBox checkBox = control as CheckBox;
            if (checkBox != null)
            {
                StyleCheckBox(checkBox);
                return;
            }

            TextBox textBox = control as TextBox;
            if (textBox != null)
            {
                StyleTextBox(textBox);
                return;
            }

            RichTextBox richTextBox = control as RichTextBox;
            if (richTextBox != null)
            {
                StyleRichTextBox(richTextBox);
                return;
            }

            LinkLabel linkLabel = control as LinkLabel;
            if (linkLabel != null)
            {
                linkLabel.BackColor = Color.Transparent;
                linkLabel.ForeColor = Text;
                linkLabel.LinkColor = Green;
                linkLabel.ActiveLinkColor = GreenSoft;
                linkLabel.VisitedLinkColor = Green;
                return;
            }

            PictureBox pictureBox = control as PictureBox;
            if (pictureBox != null)
            {
                pictureBox.BackColor = Color.Transparent;
                return;
            }

            ProgressBar progressBar = control as ProgressBar;
            if (progressBar != null)
            {
                StyleProgress(progressBar);
                return;
            }

            control.BackColor = Background;
            control.ForeColor = Text;
        }

        public static void StyleButton(Button button)
        {
            StyleSecondaryButton(button);
        }

        public static void StylePrimaryButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.Visible = true;
            NeonInteraction.StyleButton(button, NeonButtonKind.Primary);
            button.BringToFront();
        }

        public static void StyleSecondaryButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.Visible = true;
            NeonInteraction.StyleButton(button, NeonButtonKind.Secondary);
            button.BringToFront();
        }

        public static void StyleDangerButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.Visible = true;
            NeonInteraction.StyleButton(button, NeonButtonKind.Danger);
            button.BringToFront();
        }

        public static void StyleLabel(Label label)
        {
            if (label == null)
            {
                return;
            }

            label.BackColor = Color.Transparent;
            label.ForeColor = Text;
        }

        public static void StyleCheckBox(CheckBox checkBox)
        {
            if (checkBox == null)
            {
                return;
            }

            checkBox.BackColor = Color.Transparent;
            checkBox.ForeColor = checkBox.Enabled ? Text : Dim;
            checkBox.FlatStyle = FlatStyle.Flat;
            checkBox.FlatAppearance.BorderColor = Cyan;
            checkBox.FlatAppearance.CheckedBackColor = Color.FromArgb(13, 87, 92);
            checkBox.FlatAppearance.MouseOverBackColor = TurboramaPremiumTheme.SurfaceHover;
            checkBox.Font = TurboramaPremiumTheme.CreateFont(8.9f, FontStyle.Bold);
        }

        public static void StyleTextBox(TextBox textBox)
        {
            if (textBox == null)
            {
                return;
            }

            NeonInteraction.StyleField(textBox);
        }

        public static void StyleRichTextBox(RichTextBox richTextBox)
        {
            if (richTextBox == null)
            {
                return;
            }

            richTextBox.BackColor = TurboramaPremiumTheme.InputBackground;
            richTextBox.ForeColor = Text;
            richTextBox.BorderStyle = BorderStyle.None;
            richTextBox.Font = TurboramaPremiumTheme.CreateFont(9f, FontStyle.Regular);
            richTextBox.ReadOnly = true;
            richTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;
            NeonInteraction.StyleField(richTextBox);
        }

        private static void StyleLicenseBox(Control control)
        {
            if (control == null)
            {
                return;
            }

            RichTextBox rich = control as RichTextBox;
            if (rich != null)
            {
                StyleRichTextBox(rich);
                return;
            }

            TextBox text = control as TextBox;
            if (text != null)
            {
                text.Multiline = true;
                text.ReadOnly = true;
                text.ScrollBars = ScrollBars.Vertical;
                text.BorderStyle = BorderStyle.None;
                text.BackColor = TurboramaPremiumTheme.InputBackground;
                text.ForeColor = Text;
                text.Font = TurboramaPremiumTheme.CreateFont(9f, FontStyle.Regular);
                NeonInteraction.StyleField(text);
                return;
            }

            control.BackColor = TurboramaPremiumTheme.InputBackground;
            control.ForeColor = Text;
            control.Font = TurboramaPremiumTheme.CreateFont(9f, FontStyle.Regular);
        }

        public static void StyleProgress(ProgressBar progressBar)
        {
            if (progressBar == null)
            {
                return;
            }

            progressBar.ForeColor = Cyan;
            progressBar.BackColor = PanelDark;
        }

        public static Label MakeLabel(string text, int left, int top, int width, int height, Color color, float size, bool bold)
        {
            Label label = new Label();
            label.AutoSize = false;
            label.Left = left;
            label.Top = top;
            label.Width = width;
            label.Height = height;
            label.Text = text;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.Font = TurboramaPremiumTheme.CreateFont(size, bold ? FontStyle.Bold : FontStyle.Regular);
            return label;
        }

        public static void BringNavigationButtonsToFront(Control root)
        {
            if (root == null)
            {
                return;
            }

            foreach (Control control in root.Controls)
            {
                Button button = control as Button;
                if (button != null)
                {
                    string text = (button.Text ?? string.Empty).ToLowerInvariant();
                    if (text.Contains("back") || text.Contains("next") || text.Contains("cancel") || text.Contains("install") || text.Contains("finish") || text.Contains("voltar") || text.Contains("avancar") || text.Contains("avançar") || text.Contains("cancelar") || text.Contains("instalar") || text.Contains("concluir"))
                    {
                        if (text.Contains("cancel") || text.Contains("cancelar"))
                        {
                            StyleDangerButton(button);
                        }
                        else if (text.Contains("next") || text.Contains("avancar") || text.Contains("avançar") || text.Contains("install") || text.Contains("instalar") || text.Contains("finish") || text.Contains("concluir"))
                        {
                            StylePrimaryButton(button);
                        }
                        else
                        {
                            StyleSecondaryButton(button);
                        }
                        button.BringToFront();
                    }
                }

                if (control.HasChildren)
                {
                    BringNavigationButtonsToFront(control);
                }
            }
        }

        private static void SetButtonText(Button button, string text)
        {
            if (button != null)
            {
                button.Text = text;
            }
        }

        private static Control FindControl(Control root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (root.Name == name)
            {
                return root;
            }

            foreach (Control child in root.Controls)
            {
                Control result = FindControl(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static string BuildLicenseText()
        {
            return "-- CONTRATO DE LICENÇA DO SISTEMA TURBORAMA --\r\n\r\n" +
                "LZ Games e Informática apresenta o Sistema Turborama, uma plataforma voltada para jogos, entretenimento, prazer e diversão.\r\n\r\n" +
                "O Sistema Turborama foi preparado para organizar e executar uma experiência moderna com os melhores jogos, mantendo foco em praticidade, compatibilidade e estabilidade.\r\n\r\n" +
                "Ao continuar, você declara que leu e aceita os termos de uso do sistema, reconhecendo que componentes de terceiros podem possuir suas próprias licenças e condições.\r\n\r\n" +
                "A LZ Games e Informática recomenda manter os componentes do Windows atualizados, utilizar drivers oficiais e instalar o sistema em uma pasta limpa, sem caracteres especiais ou caminhos muito longos.\r\n\r\n" +
                "Este instalador prepara o ambiente, extrai os arquivos necessários e configura o Sistema Turborama para uso no computador selecionado.\r\n\r\n" +
                "O uso do sistema deve respeitar as leis aplicáveis, licenças de software, direitos autorais e regras de distribuição dos conteúdos utilizados pelo usuário.\r\n\r\n" +
                "Produto: Sistema Turborama\r\n" +
                "Empresa: LZ Games e Informática\r\n" +
                "Mensagem: prazer e diversão com os melhores jogos.";
        }

        private class PremiumPanel : NeonSurfacePanel
        {
            public PremiumPanel(Color fillColor, Color borderColor, int cornerRadius, bool glow)
            {
                this.SurfaceColor = fillColor;
                this.SurfaceColor2 = NeonDrawing.Blend(fillColor, TurboramaPremiumTheme.BackgroundDeep, 0.22f);
                this.BorderColor = borderColor;
                this.CornerRadius = cornerRadius;
                this.AccentColor = Cyan;
                this.GlowStrength = glow ? 34 : 0;
                this.ShowAccent = glow;
                this.ShowGrid = false;
                this.BackColor = Color.Transparent;
            }
        }
    }
}
