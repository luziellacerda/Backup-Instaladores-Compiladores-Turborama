using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using InstallerHost.Properties;

namespace InstallerHost
{
	// Token: 0x0200000E RID: 14
	public partial class WelcomeControl : UserControl
	{
		// Token: 0x06000058 RID: 88 RVA: 0x00008234 File Offset: 0x00006434
		public WelcomeControl(MainForm main)
		{
			this.mainForm = main;
			this.InitializeComponent();

			this.lblWelcomeTitle.Text = Texts.GetString("Welcome", Array.Empty<object>());
			this.lblWelcomeDesc.Text = Texts.GetString("WelcomeText", new object[]
			{
				BaseForm.branch,
				BaseForm.version
			});
			this.btnCancel.Text = Texts.GetString("Cancel", Array.Empty<object>());
			this.btnNext.Text = Texts.GetString("Next >", Array.Empty<object>());

			this.Resize += delegate(object s, EventArgs e)
			{
				this.BeginInvoke(new MethodInvoker(this.AdjustWelcomeBannerLayout));
			};
		}

		// Token: 0x06000059 RID: 89 RVA: 0x000082CD File Offset: 0x000064CD
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			TurboramaPremiumUi.ApplyWelcomeV3(this);

			this.AdjustWelcomeBannerLayout();
			this.BeginInvoke(new MethodInvoker(this.AdjustWelcomeBannerLayout));

			base.ActiveControl = this.btnNext;
		}

		private void AdjustWelcomeBannerLayout()
		{
			try
			{
				Control leftPanel = this.FindLeftBannerHost();
				if (leftPanel == null)
				{
					return;
				}

				leftPanel.Padding = Padding.Empty;
				leftPanel.Margin = Padding.Empty;

				PictureBox picture = this.FindBestBannerPicture(leftPanel);

				if (picture == null)
				{
					Image backgroundImage = this.FindBestBackgroundImage(leftPanel);
					if (backgroundImage != null)
					{
						picture = new PictureBox();
						picture.Name = "turboramaFullSideBanner";
						picture.Image = backgroundImage;
						leftPanel.Controls.Add(picture);
					}
				}

				if (picture == null)
				{
					return;
				}

				Image originalImage = picture.Image;
				if (originalImage != null && !object.ReferenceEquals(picture.Tag, originalImage))
				{
					Image cropped = this.CropBlackBorder(originalImage);
					if (cropped != null)
					{
						picture.Image = cropped;
					}
					picture.Tag = originalImage;
				}

				picture.Margin = Padding.Empty;
				picture.Padding = Padding.Empty;
				picture.Left = 0;
				picture.Top = 0;
				picture.Width = leftPanel.ClientSize.Width;
				picture.Height = leftPanel.ClientSize.Height;
				picture.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
				picture.SizeMode = PictureBoxSizeMode.StretchImage;
				picture.BackColor = Color.Transparent;
				picture.Visible = true;
				picture.SendToBack();

				foreach (Control control in leftPanel.Controls)
				{
					if (!object.ReferenceEquals(control, picture))
					{
						control.BringToFront();
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Log("AdjustWelcomeBannerLayout failed: " + ex.ToString());
			}
		}

		private Control FindLeftBannerHost()
		{
			List<Control> controls = new List<Control>();
			this.CollectControls(this, controls);

			Control host = controls
				.Where(c => c.Visible && c.Width >= 140 && c.Height >= 220)
				.Where(c => c.Left <= 260 || (c.Parent != null && c.Parent.Left <= 260))
				.OrderBy(c => this.GetAbsoluteLeft(c))
				.ThenByDescending(c => c.Width * c.Height)
				.FirstOrDefault(c => this.ContainsImage(c));

			if (host != null)
			{
				return host;
			}

			return controls
				.Where(c => c.Visible && c.Width >= 180 && c.Height >= 300)
				.OrderBy(c => this.GetAbsoluteLeft(c))
				.ThenByDescending(c => c.Height)
				.FirstOrDefault();
		}

		private bool ContainsImage(Control control)
		{
			PictureBox pictureBox = control as PictureBox;
			if (pictureBox != null && pictureBox.Image != null)
			{
				return true;
			}

			if (control.BackgroundImage != null)
			{
				return true;
			}

			foreach (Control child in control.Controls)
			{
				if (this.ContainsImage(child))
				{
					return true;
				}
			}

			return false;
		}

		private PictureBox FindBestBannerPicture(Control parent)
		{
			List<PictureBox> pictures = new List<PictureBox>();
			this.CollectPictureBoxes(parent, pictures);

			return pictures
				.Where(pb => pb.Image != null)
				.OrderBy(pb => this.GetAbsoluteLeft(pb))
				.ThenByDescending(pb => pb.Width * pb.Height)
				.FirstOrDefault();
		}

		private Image FindBestBackgroundImage(Control parent)
		{
			List<Control> controls = new List<Control>();
			this.CollectControls(parent, controls);

			Control imageControl = controls
				.Where(c => c.BackgroundImage != null)
				.OrderBy(c => this.GetAbsoluteLeft(c))
				.ThenByDescending(c => c.Width * c.Height)
				.FirstOrDefault();

			return imageControl != null ? imageControl.BackgroundImage : null;
		}

		private int GetAbsoluteLeft(Control control)
		{
			int left = 0;
			Control current = control;
			while (current != null)
			{
				left += current.Left;
				current = current.Parent;
			}
			return left;
		}

		private void CollectControls(Control parent, List<Control> result)
		{
			foreach (Control control in parent.Controls)
			{
				result.Add(control);
				if (control.HasChildren)
				{
					this.CollectControls(control, result);
				}
			}
		}

		private void CollectPictureBoxes(Control parent, List<PictureBox> result)
		{
			foreach (Control control in parent.Controls)
			{
				PictureBox pictureBox = control as PictureBox;
				if (pictureBox != null)
				{
					result.Add(pictureBox);
				}

				if (control.HasChildren)
				{
					this.CollectPictureBoxes(control, result);
				}
			}
		}

		private Image CropBlackBorder(Image source)
		{
			Bitmap bitmap = source as Bitmap;
			if (bitmap == null)
			{
				bitmap = new Bitmap(source);
			}

			int minX = bitmap.Width;
			int minY = bitmap.Height;
			int maxX = -1;
			int maxY = -1;

			for (int y = 0; y < bitmap.Height; y++)
			{
				for (int x = 0; x < bitmap.Width; x++)
				{
					Color pixel = bitmap.GetPixel(x, y);

					if (pixel.A > 16 && (pixel.R > 28 || pixel.G > 28 || pixel.B > 28))
					{
						if (x < minX) minX = x;
						if (y < minY) minY = y;
						if (x > maxX) maxX = x;
						if (y > maxY) maxY = y;
					}
				}
			}

			if (maxX <= minX || maxY <= minY)
			{
				return source;
			}

			int padX = Math.Max(0, (maxX - minX + 1) / 18);
			int padY = Math.Max(0, (maxY - minY + 1) / 18);

			minX = Math.Max(0, minX - padX);
			minY = Math.Max(0, minY - padY);
			maxX = Math.Min(bitmap.Width - 1, maxX + padX);
			maxY = Math.Min(bitmap.Height - 1, maxY + padY);

			Rectangle crop = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);

			if (crop.Width >= bitmap.Width * 0.96 && crop.Height >= bitmap.Height * 0.96)
			{
				return source;
			}

			Bitmap result = new Bitmap(crop.Width, crop.Height);
			using (Graphics graphics = Graphics.FromImage(result))
			{
				graphics.DrawImage(bitmap, new Rectangle(0, 0, result.Width, result.Height), crop, GraphicsUnit.Pixel);
			}

			return result;
		}

		// Token: 0x0600005B RID: 91 RVA: 0x00008796 File Offset: 0x00006996
		private void BtnNext_Click(object sender, EventArgs e)
		{
			Logger.Log("Welcome screen, user clicked NEXT");
			this.mainForm.ShowLicense();
		}

		// Token: 0x0600005C RID: 92 RVA: 0x000087AD File Offset: 0x000069AD
		private void BtnCancel_Click(object sender, EventArgs e)
		{
			if (MessageBox.Show("Tem certeza que deseja cancelar a instalação?", "Cancelar instalação", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				Application.Exit();
			}
		}

		// Token: 0x04000050 RID: 80
		private MainForm mainForm;
	}
}
