using System;

namespace InstallerHost
{
	// Token: 0x0200000C RID: 12
	public class InstallerInfo
	{
		// Token: 0x17000005 RID: 5
		// (get) Token: 0x06000051 RID: 81 RVA: 0x00006BAE File Offset: 0x00004DAE
		// (set) Token: 0x06000052 RID: 82 RVA: 0x00006BB6 File Offset: 0x00004DB6
		public string Url { get; set; }

		// Token: 0x17000006 RID: 6
		// (get) Token: 0x06000053 RID: 83 RVA: 0x00006BBF File Offset: 0x00004DBF
		// (set) Token: 0x06000054 RID: 84 RVA: 0x00006BC7 File Offset: 0x00004DC7
		public string Arguments { get; set; }

		// Token: 0x06000055 RID: 85 RVA: 0x00006BD0 File Offset: 0x00004DD0
		public InstallerInfo(string url, string arguments)
		{
			this.Url = url;
			this.Arguments = arguments;
		}
	}
}
