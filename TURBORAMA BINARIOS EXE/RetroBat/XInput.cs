using System;
using System.Runtime.InteropServices;

namespace RetroBat
{
	// Token: 0x0200000D RID: 13
	public static class XInput
	{
		// Token: 0x060000A1 RID: 161
		[DllImport("xinput1_4.dll")]
		private static extern uint XInputGetState(uint dwUserIndex, out XInput.XINPUT_STATE pState);

		// Token: 0x060000A2 RID: 162 RVA: 0x0000595C File Offset: 0x00003B5C
		public static bool IsFaceButtonPressed()
		{
			for (uint num = 0U; num < 4U; num += 1U)
			{
				XInput.XINPUT_STATE xinput_STATE;
				if (XInput.XInputGetState(num, out xinput_STATE) == 0U && (xinput_STATE.Gamepad.wButtons & 816) != 0)
				{
					return true;
				}
			}
			return false;
		}

		// Token: 0x0400005B RID: 91
		private const ushort XINPUT_GAMEPAD_DPAD_UP = 1;

		// Token: 0x0400005C RID: 92
		private const ushort XINPUT_GAMEPAD_DPAD_DOWN = 2;

		// Token: 0x0400005D RID: 93
		private const ushort XINPUT_GAMEPAD_DPAD_LEFT = 4;

		// Token: 0x0400005E RID: 94
		private const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 8;

		// Token: 0x0400005F RID: 95
		private const ushort XINPUT_GAMEPAD_START = 16;

		// Token: 0x04000060 RID: 96
		private const ushort XINPUT_GAMEPAD_BACK = 32;

		// Token: 0x04000061 RID: 97
		private const ushort XINPUT_GAMEPAD_LEFT_THUMB = 64;

		// Token: 0x04000062 RID: 98
		private const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 128;

		// Token: 0x04000063 RID: 99
		private const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 256;

		// Token: 0x04000064 RID: 100
		private const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 512;

		// Token: 0x04000065 RID: 101
		private const ushort XINPUT_GAMEPAD_A = 4096;

		// Token: 0x04000066 RID: 102
		private const ushort XINPUT_GAMEPAD_B = 8192;

		// Token: 0x04000067 RID: 103
		private const ushort XINPUT_GAMEPAD_X = 16384;

		// Token: 0x04000068 RID: 104
		private const ushort XINPUT_GAMEPAD_Y = 32768;

		// Token: 0x02000025 RID: 37
		private struct XINPUT_GAMEPAD
		{
			// Token: 0x040000A3 RID: 163
			public ushort wButtons;

			// Token: 0x040000A4 RID: 164
			public byte bLeftTrigger;

			// Token: 0x040000A5 RID: 165
			public byte bRightTrigger;

			// Token: 0x040000A6 RID: 166
			public short sThumbLX;

			// Token: 0x040000A7 RID: 167
			public short sThumbLY;

			// Token: 0x040000A8 RID: 168
			public short sThumbRX;

			// Token: 0x040000A9 RID: 169
			public short sThumbRY;
		}

		// Token: 0x02000026 RID: 38
		private struct XINPUT_STATE
		{
			// Token: 0x040000AA RID: 170
			public uint dwPacketNumber;

			// Token: 0x040000AB RID: 171
			public XInput.XINPUT_GAMEPAD Gamepad;
		}
	}
}
