using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace InstallerHost
{
    internal static class TurboramaPremiumUi
    {
        public static readonly Color Background = Color.FromArgb(3, 7, 6);
        public static readonly Color Shell = Color.FromArgb(6, 12, 9);
        public static readonly Color PanelDark = Color.FromArgb(8, 15, 11);
        public static readonly Color PanelMid = Color.FromArgb(13, 23, 17);
        public static readonly Color Card = Color.FromArgb(12, 22, 16);
        public static readonly Color CardHot = Color.FromArgb(16, 32, 22);
        public static readonly Color Border = Color.FromArgb(52, 92, 58);
        public static readonly Color BorderSoft = Color.FromArgb(30, 54, 36);
        public static readonly Color Green = Color.FromArgb(112, 255, 32);
        public static readonly Color GreenSoft = Color.FromArgb(170, 255, 120);
        public static readonly Color GreenDeep = Color.FromArgb(26, 105, 26);
        public static readonly Color Text = Color.FromArgb(242, 248, 242);
        public static readonly Color Muted = Color.FromArgb(170, 184, 174);
        public static readonly Color Dim = Color.FromArgb(91, 110, 96);
        public static readonly Color Warning = Color.FromArgb(255, 190, 80);
        public static readonly Color AccentRed = Color.FromArgb(214, 48, 49);
        public static readonly Color AccentRedSoft = Color.FromArgb(108, 28, 30);

        private const int WindowWidth = 980;
        private const int WindowHeight = 620;
        private const int SidebarWidth = 288;
        private const int FooterHeight = 68;
        private static Image cachedFooterBannerImage;
        private static Image cachedSidebarLogoImage;

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
                StyleControl(root);
                ApplyThemeRecursive(root);

                // O banner horizontal da LZ deve aparecer SOMENTE na tela 03
                // de requisitos do sistema. Welcome, licença, instalação e conclusão
                // continuam sem esse banner de imagem no rodapé.
                if (IsPrerequisiteScreen(root))
                {
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
                Panel content = CreateContent(root, "Bem-vindo ao Sistema Turborama", "LZ Games e Informática", "Prazer, diversão e os melhores jogos em uma plataforma organizada, moderna e confiável.");

                PremiumPanel hero = MakeCard("__welcomeHero", 34, 118, 610, 178, CardHot, Border, 16);
                content.Controls.Add(hero);

                if (title != null)
                {
                    title.Parent = hero;
                    title.Text = "Sistema Turborama";
                    title.Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold);
                    title.ForeColor = Text;
                    title.BackColor = Color.Transparent;
                    title.Location = new Point(26, 22);
                    title.Size = new Size(548, 38);
                }

                if (desc != null)
                {
                    desc.Parent = hero;
                    desc.Text = "A LZ Games e Informática apresenta o Sistema Turborama, desenvolvido para oferecer prazer, diversão e acesso aos melhores jogos em uma experiência premium.\r\n\r\nEste instalador irá preparar o computador e configurar os componentes recomendados para estabilidade, desempenho e compatibilidade.";
                    desc.Font = new Font("Segoe UI", 9.7f, FontStyle.Regular);
                    desc.ForeColor = Color.FromArgb(220, 232, 222);
                    desc.BackColor = Color.Transparent;
                    desc.Location = new Point(28, 72);
                    desc.Size = new Size(548, 92);
                }

                AddFeatureCard(content, 34, 318, 188, 96, "DIVERSÃO PREMIUM", "Uma experiência pensada para prazer, entretenimento e jogos inesquecíveis.");
                AddFeatureCard(content, 244, 318, 188, 96, "SISTEMA ORGANIZADO", "Instalação clara, componentes preparados e ambiente pronto para uso.");
                AddFeatureCard(content, 454, 318, 188, 96, "PRONTO PARA JOGAR", "Tudo configurado para você aproveitar o Turborama com praticidade.");

                PremiumPanel signature = MakeCard("__welcomeSignature", 34, 436, 610, 58, PanelMid, BorderSoft, 12);
                content.Controls.Add(signature);
                signature.Controls.Add(MakeLabel("LZ Games e Informática", 22, 11, 240, 20, Text, 9.5f, true));
                signature.Controls.Add(MakeLabel("Tecnologia, diversão e os melhores jogos em um só sistema.", 22, 32, 500, 18, GreenSoft, 8.6f, false));

                SetButtonText(next, "Avançar >");
                SetButtonText(cancel, "Cancelar");
                AddFooter(root, null, next, cancel);
                StylePrimaryButton(next);
                StyleDangerButton(cancel);
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
                content.Controls.Add(introCard);
                introCard.Controls.Add(MakeLabel("LZ Games e Informática", 24, 16, 300, 22, Text, 10.2f, true));
                introCard.Controls.Add(MakeLabel("O Sistema Turborama foi desenvolvido para entregar prazer, diversão e os melhores jogos com uma experiência moderna, organizada e confiável.", 24, 42, 540, 34, Muted, 8.7f, false));

                PremiumPanel licenseCard = MakeCard("__licenseCard", 34, 210, 610, 284, Card, Border, 14);
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
                content.Controls.Add(pathCard);
                pathCard.Controls.Add(MakeLabel("PASTA DE INSTALAÇÃO", 24, 18, 350, 22, Text, 10f, true));
                pathCard.Controls.Add(MakeLabel("Recomendamos manter a pasta padrão para melhor organização, compatibilidade e funcionamento do Sistema Turborama.", 24, 42, 530, 34, Muted, 8.7f, false));

                if (selectFolder != null)
                {
                    selectFolder.Parent = pathCard;
                    selectFolder.Text = "Selecione a pasta de instalação:";
                    selectFolder.Location = new Point(24, 86);
                    selectFolder.Size = new Size(520, 18);
                    selectFolder.Font = new Font("Segoe UI Semibold", 8.7f, FontStyle.Bold);
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
                    hint.Font = new Font("Segoe UI", 8.7f, FontStyle.Regular);
                    hint.ForeColor = Color.FromArgb(200, 214, 202);
                    hint.BackColor = Color.Transparent;
                }

                PremiumPanel statusCard = MakeCard("__installStatusCard", 34, 330, 610, 120, PanelMid, BorderSoft, 14);
                content.Controls.Add(statusCard);
                statusCard.Controls.Add(MakeLabel("STATUS DA PREPARAÇÃO", 24, 16, 380, 22, Text, 9.6f, true));
                statusCard.Controls.Add(MakeLabel("A LZ Games e Informática irá preparar o ambiente, extrair os arquivos e validar o executável principal do Sistema Turborama.", 24, 42, 540, 32, Muted, 8.6f, false));

                if (info != null)
                {
                    info.Parent = statusCard;
                    info.Text = "Pronto para instalar. Clique em Instalar para começar.";
                    info.Location = new Point(24, 84);
                    info.Size = new Size(540, 22);
                    info.Font = new Font("Segoe UI Semibold", 9.2f, FontStyle.Bold);
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
                }

                SetButtonText(back, "< Voltar");
                SetButtonText(install, "Instalar");
                SetButtonText(cancel, "Cancelar");
                AddFooter(root, back, install, cancel);
                StyleSecondaryButton(back);
                StylePrimaryButton(install);
                StyleDangerButton(cancel);
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
                Panel content = CreateContent(root, "Instalação concluída", "Sistema Turborama pronto para uso", "Agora é só iniciar e aproveitar prazer, diversão e os melhores jogos.");

                PremiumPanel finishCard = MakeCard("__finishCard", 34, 118, 610, 254, CardHot, Border, 16);
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
                    message.Font = new Font("Segoe UI Semibold", 16.5f, FontStyle.Bold);
                    message.ForeColor = Text;
                    message.BackColor = Color.Transparent;
                }

                if (desc != null)
                {
                    desc.Parent = finishCard;
                    desc.Text = "O Sistema Turborama, da LZ Games e Informática, está pronto para proporcionar prazer, diversão e acesso aos melhores jogos em uma experiência moderna e organizada.";
                    desc.Location = new Point(100, 76);
                    desc.Size = new Size(470, 58);
                    desc.Font = new Font("Segoe UI", 9.4f, FontStyle.Regular);
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

            Control[] existing = new Control[root.Controls.Count];
            root.Controls.CopyTo(existing, 0);
            foreach (Control control in existing)
            {
                Panel p = control as Panel;
                if (p != null && (p.Name == "__premiumSidebar" || p.Name == "__premiumContent" || p.Name == "__premiumFooter"))
                {
                    root.Controls.Remove(p);
                    p.Dispose();
                }
            }

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

        private static void EnsureLargeHost(UserControl root)
        {
            try
            {
                Form form = root.FindForm();
                if (form != null)
                {
                    form.ClientSize = new Size(WindowWidth, WindowHeight);
                    form.MinimumSize = new Size(WindowWidth, WindowHeight);
                    form.BackColor = Background;
                    form.ForeColor = Text;
                    form.Text = "LZ Games e Informática - Sistema Turborama";
                    root.Size = form.ClientSize;
                }
                else
                {
                    root.Size = new Size(WindowWidth, WindowHeight);
                    root.MinimumSize = new Size(WindowWidth, WindowHeight);
                }
            }
            catch
            {
                root.Size = new Size(WindowWidth, WindowHeight);
            }
        }

        private static Panel CreateSidebar(UserControl root, int activeIndex, string stage, string status, PictureBox banner)
        {
            PremiumPanel sidebar = new PremiumPanel(Color.FromArgb(4, 13, 8), Color.FromArgb(32, 68, 38), 0, false);
            sidebar.Name = "__premiumSidebar";
            sidebar.Left = 0;
            sidebar.Top = 0;
            sidebar.Width = SidebarWidth;
            sidebar.Height = root.Height - FooterHeight;
            sidebar.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;
            root.Controls.Add(sidebar);

            Label logo = MakeLabel("LZ", 24, 20, 58, 48, Green, 18.5f, true);
            logo.TextAlign = ContentAlignment.MiddleCenter;
            logo.BorderStyle = BorderStyle.FixedSingle;
            sidebar.Controls.Add(logo);

            Label brand1 = MakeLabel("LZ GAMES", 96, 22, 160, 24, Text, 12.5f, true);
            sidebar.Controls.Add(brand1);
            Label brand2 = MakeLabel("INFORMÁTICA", 96, 48, 160, 20, GreenSoft, 8.8f, true);
            sidebar.Controls.Add(brand2);

            Label line = new Label();
            line.BackColor = Green;
            line.Left = 24;
            line.Top = 86;
            line.Width = 218;
            line.Height = 2;
            sidebar.Controls.Add(line);

            Label product = MakeLabel("SISTEMA TURBORAMA", 24, 104, 220, 24, Text, 11f, true);
            sidebar.Controls.Add(product);
            Label slogan = MakeLabel("Prazer e diversão\r\ncom os melhores jogos", 24, 132, 220, 42, GreenSoft, 8.8f, false);
            sidebar.Controls.Add(slogan);

            Image sidebarLogo = LoadSidebarLogoImage();
            Image artworkImage = sidebarLogo;

            if (artworkImage == null && banner != null && banner.Image != null)
            {
                artworkImage = CloneImageSafe(banner.Image);
            }

            if (banner != null)
            {
                // IMPORTANTE: o PictureBox original do Designer vem com DockStyle.Left.
                // Se ele for reaproveitado sem limpar o Dock, a imagem sai do esquadro.
                banner.Visible = false;
                banner.Dock = DockStyle.None;
                banner.Anchor = AnchorStyles.None;
            }

            PremiumPanel artworkBox = MakeCard("__sidebarArtwork", 24, 186, 220, 160, Color.FromArgb(3, 8, 5), Color.FromArgb(38, 72, 42), 0);
            sidebar.Controls.Add(artworkBox);

            if (artworkImage != null)
            {
                PictureBox artworkPicture = new PictureBox();
                artworkPicture.Name = "__turboramaSidebarArtworkImage";
                artworkPicture.Image = artworkImage;
                artworkPicture.Location = new Point(0, 0);
                artworkPicture.Size = new Size(220, 160);
                artworkPicture.SizeMode = PictureBoxSizeMode.Zoom;
                artworkPicture.BackColor = Color.Black;
                artworkPicture.BorderStyle = BorderStyle.None;
                artworkPicture.Margin = new Padding(0);
                artworkPicture.Padding = new Padding(0);
                artworkPicture.TabStop = false;
                artworkBox.Controls.Add(artworkPicture);
                artworkPicture.BringToFront();
            }
            else
            {
                artworkBox.Controls.Add(MakeLabel("TURBORAMA", 24, 50, 170, 28, Green, 15f, true));
                artworkBox.Controls.Add(MakeLabel("Premium Games System", 26, 84, 170, 22, Muted, 8.5f, false));
            }

            Label stageLabel = MakeLabel(stage, 24, 360, 220, 22, Text, 9.5f, true);
            sidebar.Controls.Add(stageLabel);
            Label statusLabel = MakeLabel(status, 24, 384, 220, 20, Green, 8.5f, true);
            sidebar.Controls.Add(statusLabel);

            int stepTop = 416;
            AddStep(sidebar, 0, stepTop + 0, "01", "Boas-vindas", activeIndex);
            AddStep(sidebar, 1, stepTop + 23, "02", "Licença", activeIndex);
            AddStep(sidebar, 2, stepTop + 46, "03", "Requisitos", activeIndex);
            AddStep(sidebar, 3, stepTop + 69, "04", "Instalação", activeIndex);
            AddStep(sidebar, 4, stepTop + 92, "05", "Progresso", activeIndex);
            AddStep(sidebar, 5, stepTop + 115, "06", "Conclusão", activeIndex);

            sidebar.BringToFront();
            return sidebar;
        }

        private static Panel CreateWelcomeSidebar(UserControl root, PictureBox banner)
        {
            PremiumPanel sidebar = new PremiumPanel(Color.FromArgb(4, 13, 8), Color.FromArgb(32, 68, 38), 0, false);
            sidebar.Name = "__premiumSidebar";
            sidebar.Left = 0;
            sidebar.Top = 0;
            sidebar.Width = SidebarWidth;
            sidebar.Height = root.Height - FooterHeight;
            sidebar.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;
            root.Controls.Add(sidebar);

            Label logo = MakeLabel("LZ", 24, 20, 58, 48, Green, 18.5f, true);
            logo.TextAlign = ContentAlignment.MiddleCenter;
            logo.BorderStyle = BorderStyle.FixedSingle;
            sidebar.Controls.Add(logo);

            Label brand1 = MakeLabel("LZ GAMES", 96, 22, 160, 24, Text, 12.5f, true);
            sidebar.Controls.Add(brand1);
            Label brand2 = MakeLabel("INFORMÁTICA", 96, 48, 160, 20, GreenSoft, 8.8f, true);
            sidebar.Controls.Add(brand2);

            Label line = new Label();
            line.BackColor = Green;
            line.Left = 24;
            line.Top = 86;
            line.Width = 218;
            line.Height = 2;
            sidebar.Controls.Add(line);

            Panel lineAccent = new Panel();
            lineAccent.BackColor = AccentRed;
            lineAccent.Left = 206;
            lineAccent.Top = 82;
            lineAccent.Width = 12;
            lineAccent.Height = 4;
            sidebar.Controls.Add(lineAccent);

            Label product = MakeLabel("SISTEMA TURBORAMA", 24, 104, 220, 24, Text, 11f, true);
            sidebar.Controls.Add(product);
            Label slogan = MakeLabel("Prazer e diversão\r\ncom os melhores jogos", 24, 132, 220, 42, GreenSoft, 8.8f, false);
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

            int artworkTop = 184;
            int artworkHeight = Math.Max(300, sidebar.Height - artworkTop - 18);
            PremiumPanel artworkBox = MakeCard("__welcomeSidebarArtwork", 24, artworkTop, 220, artworkHeight, Color.Black, Color.FromArgb(38, 72, 42), 0);
            artworkBox.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom;
            sidebar.Controls.Add(artworkBox);

            Panel accentTopLeft = new Panel();
            accentTopLeft.BackColor = AccentRed;
            accentTopLeft.Left = 0;
            accentTopLeft.Top = 0;
            accentTopLeft.Width = 38;
            accentTopLeft.Height = 2;
            artworkBox.Controls.Add(accentTopLeft);

            Panel accentLeft = new Panel();
            accentLeft.BackColor = AccentRed;
            accentLeft.Left = 0;
            accentLeft.Top = 0;
            accentLeft.Width = 2;
            accentLeft.Height = 22;
            artworkBox.Controls.Add(accentLeft);

            Panel accentTopRight = new Panel();
            accentTopRight.BackColor = AccentRed;
            accentTopRight.Left = artworkBox.Width - 38;
            accentTopRight.Top = 0;
            accentTopRight.Width = 38;
            accentTopRight.Height = 2;
            accentTopRight.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            artworkBox.Controls.Add(accentTopRight);

            Panel accentRight = new Panel();
            accentRight.BackColor = AccentRed;
            accentRight.Left = artworkBox.Width - 2;
            accentRight.Top = 0;
            accentRight.Width = 2;
            accentRight.Height = 22;
            accentRight.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            artworkBox.Controls.Add(accentRight);

            Panel accentBottom = new Panel();
            accentBottom.BackColor = AccentRedSoft;
            accentBottom.Left = 0;
            accentBottom.Top = artworkBox.Height - 2;
            accentBottom.Width = 54;
            accentBottom.Height = 2;
            accentBottom.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            artworkBox.Controls.Add(accentBottom);

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
                artworkBox.Controls.Add(artworkPicture);
                artworkPicture.SendToBack();
            }
            else
            {
                artworkBox.Controls.Add(MakeLabel("TURBORAMA", 24, Math.Max(96, (artworkHeight / 2) - 24), 170, 28, Green, 15f, true));
                artworkBox.Controls.Add(MakeLabel("Premium Games System", 26, Math.Max(128, (artworkHeight / 2) + 8), 170, 22, Muted, 8.5f, false));
            }

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
                    Path.Combine(baseDir, "resources", "turborama_sidebar_logo.png"),
                    Path.Combine(baseDir, "resources", "turborama_sidebar_logo.jpg"),
                    Path.Combine(baseDir, "resources", "turborama_sidebar_logo.jpeg"),
                    Path.Combine(baseDir, "turborama_sidebar_logo.png"),
                    Path.Combine(baseDir, "turborama_sidebar_logo.jpg"),
                    Path.Combine(baseDir, "turborama_sidebar_logo.jpeg"),
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
            Panel content = new Panel();
            content.Name = "__premiumContent";
            content.Left = SidebarWidth;
            content.Top = 0;
            content.Width = root.Width - SidebarWidth;
            content.Height = root.Height - FooterHeight;
            content.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
            content.BackColor = Background;
            root.Controls.Add(content);

            Label titleLabel = MakeLabel(title, 34, 28, content.Width - 68, 38, Text, 18.5f, true);
            titleLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            content.Controls.Add(titleLabel);

            Label subtitleLabel = MakeLabel(subtitle, 36, 68, content.Width - 72, 20, Green, 9.3f, true);
            subtitleLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            content.Controls.Add(subtitleLabel);

            Label underline = new Label();
            underline.BackColor = Green;
            underline.Location = new Point(36, 96);
            underline.Size = new Size(122, 2);
            content.Controls.Add(underline);

            Label desc = MakeLabel(description, 190, 62, content.Width - 230, 42, Muted, 8.8f, false);
            desc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            content.Controls.Add(desc);

            return content;
        }

        private static void AddFooter(UserControl root, Button back, Button primary, Button cancel)
        {
            PremiumPanel footer = new PremiumPanel(Color.FromArgb(6, 12, 9), Color.FromArgb(28, 52, 32), 0, false);
            footer.Name = "__premiumFooter";
            footer.Left = 0;
            footer.Top = root.Height - FooterHeight;
            footer.Width = root.Width;
            footer.Height = FooterHeight;
            footer.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            root.Controls.Add(footer);

            // Importante: o banner de imagem NÃO deve aparecer nas telas 01 e 02.
            // Esse rodapé dos layouts V3 fica limpo e discreto.
            Label line = new Label();
            line.BackColor = BorderSoft;
            line.Left = 0;
            line.Top = 0;
            line.Width = root.Width;
            line.Height = 1;
            line.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            footer.Controls.Add(line);

            Label footerText = MakeLabel("LZ Games e Informática  •  Sistema Turborama", 24, 21, 360, 24, Dim, 8.4f, false);
            footer.Controls.Add(footerText);

            int right = root.Width - 20;
            if (cancel != null)
            {
                cancel.Parent = footer;
                cancel.Size = new Size(96, 32);
                cancel.Location = new Point(right - 96, 18);
                right -= 108;
                cancel.Anchor = AnchorStyles.Right | AnchorStyles.Top;
                StyleDangerButton(cancel);
                cancel.Visible = true;
            }

            if (primary != null)
            {
                primary.Parent = footer;
                primary.Size = new Size(116, 32);
                primary.Location = new Point(right - 116, 18);
                right -= 128;
                primary.Anchor = AnchorStyles.Right | AnchorStyles.Top;
                StylePrimaryButton(primary);
                primary.Visible = true;
            }

            if (back != null)
            {
                back.Parent = footer;
                back.Size = new Size(96, 32);
                back.Location = new Point(right - 96, 18);
                back.Anchor = AnchorStyles.Right | AnchorStyles.Top;
                StyleSecondaryButton(back);
                back.Visible = true;
            }

            footer.BringToFront();
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
                    footerBanner.SizeMode = PictureBoxSizeMode.StretchImage;
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
                int bannerHeight = 112;
                int gapAboveButtons = 8;
                int top = root.Height - FooterHeight - gapAboveButtons - bannerHeight;

                if (top < 360)
                {
                    top = 360;
                }

                footerBanner.Left = 0;
                footerBanner.Top = top;
                footerBanner.Width = root.Width;
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
            Label num = MakeLabel(done ? "✓" : number, 24, top, 34, 20, active || done ? Green : Dim, 8f, true);
            sidebar.Controls.Add(num);
            Label label = MakeLabel(text, 64, top, 160, 20, active ? Text : (done ? Color.FromArgb(194, 214, 194) : Dim), 8.5f, active);
            sidebar.Controls.Add(label);
        }

        private static void AddFeatureCard(Control parent, int left, int top, int width, int height, string title, string description)
        {
            PremiumPanel card = MakeCard("__feature", left, top, width, height, PanelMid, BorderSoft, 12);
            parent.Controls.Add(card);
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
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderColor = Green;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(56, 148, 36);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(76, 184, 44);
            button.BackColor = Color.FromArgb(26, 92, 24);
            button.ForeColor = Text;
            button.Font = new Font("Segoe UI Semibold", 9.4f, FontStyle.Bold);
            button.BringToFront();
        }

        public static void StyleSecondaryButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.Visible = true;
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderColor = Green;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 58, 22);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(40, 86, 24);
            button.BackColor = Color.FromArgb(12, 21, 16);
            button.ForeColor = Text;
            button.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            button.BringToFront();
        }

        public static void StyleDangerButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.Visible = true;
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderColor = Color.FromArgb(70, 88, 74);
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 42, 36);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(46, 52, 47);
            button.BackColor = Color.FromArgb(15, 19, 16);
            button.ForeColor = Color.FromArgb(214, 222, 214);
            button.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
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

            checkBox.BackColor = PanelMid;
            checkBox.ForeColor = Text;
            checkBox.FlatStyle = FlatStyle.Standard;
            checkBox.Font = new Font("Segoe UI Semibold", 8.9f, FontStyle.Bold);
        }

        public static void StyleTextBox(TextBox textBox)
        {
            if (textBox == null)
            {
                return;
            }

            textBox.BackColor = Color.FromArgb(8, 13, 10);
            textBox.ForeColor = Color.FromArgb(235, 242, 235);
            textBox.BorderStyle = BorderStyle.FixedSingle;
            textBox.Font = new Font("Segoe UI Semibold", 9.4f, FontStyle.Bold);
        }

        public static void StyleRichTextBox(RichTextBox richTextBox)
        {
            if (richTextBox == null)
            {
                return;
            }

            richTextBox.BackColor = Color.FromArgb(7, 12, 9);
            richTextBox.ForeColor = Color.FromArgb(232, 240, 232);
            richTextBox.BorderStyle = BorderStyle.None;
            richTextBox.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            richTextBox.ReadOnly = true;
            richTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;
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
                text.BackColor = Color.FromArgb(7, 12, 9);
                text.ForeColor = Color.FromArgb(232, 240, 232);
                text.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
                return;
            }

            control.BackColor = Color.FromArgb(7, 12, 9);
            control.ForeColor = Color.FromArgb(232, 240, 232);
            control.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
        }

        public static void StyleProgress(ProgressBar progressBar)
        {
            if (progressBar == null)
            {
                return;
            }

            progressBar.ForeColor = Green;
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
            label.Font = new Font("Segoe UI" + (bold ? " Semibold" : string.Empty), size, bold ? FontStyle.Bold : FontStyle.Regular);
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
                        StyleButton(button);
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

        private class PremiumPanel : Panel
        {
            private Color fill;
            private Color border;
            private int radius;
            private bool drawGlow;

            public PremiumPanel(Color fillColor, Color borderColor, int cornerRadius, bool glow)
            {
                this.fill = fillColor;
                this.border = borderColor;
                this.radius = cornerRadius;
                this.drawGlow = glow;
                this.DoubleBuffered = true;
                this.ResizeRedraw = true;
                this.BackColor = Color.Transparent;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

                Rectangle rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
                if (this.radius <= 0)
                {
                    using (SolidBrush brush = new SolidBrush(this.fill))
                    {
                        e.Graphics.FillRectangle(brush, rect);
                    }
                    using (Pen pen = new Pen(this.border))
                    {
                        e.Graphics.DrawRectangle(pen, rect);
                    }
                    return;
                }

                using (GraphicsPath path = RoundedRect(rect, this.radius))
                {
                    using (SolidBrush brush = new SolidBrush(this.fill))
                    {
                        e.Graphics.FillPath(brush, path);
                    }
                    if (this.drawGlow)
                    {
                        using (Pen glowPen = new Pen(Color.FromArgb(55, Green), 2f))
                        {
                            e.Graphics.DrawPath(glowPen, path);
                        }
                    }
                    using (Pen pen = new Pen(this.border))
                    {
                        e.Graphics.DrawPath(pen, path);
                    }
                }
            }

            private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
            {
                int diameter = radius * 2;
                GraphicsPath path = new GraphicsPath();
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                return path;
            }
        }
    }
}
