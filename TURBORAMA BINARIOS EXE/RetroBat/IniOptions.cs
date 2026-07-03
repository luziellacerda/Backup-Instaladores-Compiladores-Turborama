using System;

namespace RetroBat
{
	// Token: 0x02000003 RID: 3
	[Flags]
	public enum IniOptions
	{
		// Token: 0x0400000D RID: 13
		UseSpaces = 1,
		// Token: 0x0400000E RID: 14
		KeepEmptyValues = 2,
		// Token: 0x0400000F RID: 15
		AllowDuplicateValues = 4,
		// Token: 0x04000010 RID: 16
		KeepEmptyLines = 8,
		// Token: 0x04000011 RID: 17
		UseDoubleEqual = 16,
		// Token: 0x04000012 RID: 18
		ManageKeysWithQuotes = 32
	}
}
