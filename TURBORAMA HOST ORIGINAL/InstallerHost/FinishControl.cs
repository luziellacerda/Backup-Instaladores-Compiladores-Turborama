using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using InstallerHost.Properties;

namespace InstallerHost
{
	// Token: 0x02000005 RID: 5
	public partial class FinishControl : UserControl
	{
		// Token: 0x06000011 RID: 17 RVA: 0x000024F0 File Offset: 0x000006F0
		public FinishControl(MainForm main, string path)
		{
			this.mainForm = main;
			this.installPath = path;
			this.InitializeComponent();
			this.lblMessage.Text = Texts.GetString("InstallComplete", Array.Empty<object>());
			this.chkRunApp.Text = Texts.GetString("RunRetroBat", Array.Empty<object>());
			this.btnFinish.Text = Texts.GetString("Finish", Array.Empty<object>());
			this.lblWelcomeDesc.Text = Texts.GetString("InstallCompleteDescription", Array.Empty<object>());
			this.AddLink("Forum", "https://forum.retrobat.org/", Resources.forum);
			this.AddLink("Discord", "https://discord.gg/retrobat", Resources.discord);
			this.AddLink("Website", "https://www.retrobat.org/", Resources.website);
			this.AddLink("Wiki", "https://wiki.retrobat.org/", Resources.wiki);
		}

		// Token: 0x06000013 RID: 19 RVA: 0x00002BEC File Offset: 0x00000DEC
		private void AddLink(string text, string url, Image icon)
		{
			PictureBox pictureBox = new PictureBox
			{
				Image = icon,
				SizeMode = PictureBoxSizeMode.Zoom,
				Width = 16,
				Height = 16,
				Margin = new Padding(0, 2, 4, 0)
			};
			LinkLabel linkLabel = new LinkLabel
			{
				Text = text,
				Tag = url,
				AutoSize = true,
				LinkColor = Color.RoyalBlue,
				ActiveLinkColor = Color.DodgerBlue,
				VisitedLinkColor = Color.Purple,
				Font = new Font("Segoe UI", 9f, FontStyle.Regular)
			};
			linkLabel.LinkClicked += this.LinkLabel_LinkClicked;
			FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				FlowDirection = FlowDirection.LeftToRight,
				Margin = new Padding(0, 0, 15, 0)
			};
			flowLayoutPanel.Controls.Add(pictureBox);
			flowLayoutPanel.Controls.Add(linkLabel);
			this.linkPanel.Controls.Add(flowLayoutPanel);
		}

		// Token: 0x06000014 RID: 20 RVA: 0x00002CE4 File Offset: 0x00000EE4
		private void BtnFinish_Click(object sender, EventArgs e)
		{
			if (this.chkRunApp.Checked)
			{
				string text = Path.Combine(this.installPath, "RetroBat.exe");
				if (File.Exists(text))
				{
					try
					{
						Process.Start(text);
						Logger.Log("Launched installed app: " + text);
						goto IL_00A5;
					}
					catch (Exception ex)
					{
						MessageBox.Show(Texts.GetString("LaunchFail", Array.Empty<object>()) + ex.Message);
						Logger.Log("Failed to launch application: " + ex.ToString());
						goto IL_00A5;
					}
				}
				MessageBox.Show(Texts.GetString("ExeNotFound", Array.Empty<object>()) + text);
				Logger.Log("Executable not found: " + text);
			}
			IL_00A5:
			Application.Exit();
		}

		// Token: 0x06000015 RID: 21 RVA: 0x00002DAC File Offset: 0x00000FAC
		private void LinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			LinkLabel linkLabel = sender as LinkLabel;
			if (linkLabel != null)
			{
				string text = linkLabel.Tag as string;
				if (text != null)
				{
					try
					{
						Process.Start(new ProcessStartInfo(text)
						{
							UseShellExecute = true
						});
					}
					catch (Exception ex)
					{
						MessageBox.Show("Unable to open link: " + ex.Message);
					}
				}
			}
		}

		// Token: 0x0400000C RID: 12
		private MainForm mainForm;

		// Token: 0x0400000D RID: 13
		private string installPath;
	}
}
