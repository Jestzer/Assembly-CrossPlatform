namespace Blamite.Blam.Textures
{
	/// <summary>
	///     Pixel format for a bitmap image, matching the format enum in bitm tag plugins.
	///     Values 0x00-0x17 are shared across SecondGen and ThirdGen.
	///     Values 0x18+ are ThirdGen (Halo 3/ODST/Reach/H4) only.
	/// </summary>
	public enum BitmapFormat : ushort
	{
		A8 = 0x0,
		Y8 = 0x1,
		AY8 = 0x2,
		A8Y8 = 0x3,
		R8 = 0x4,
		R5G6B5 = 0x6,
		A1R5G5B5 = 0x8,
		A4R4G4B4 = 0x9,
		X8R8G8B8 = 0xA,
		A8R8G8B8 = 0xB,
		DXT1 = 0xE,
		DXT3 = 0xF,
		DXT5 = 0x10,
		P8Bump = 0x11,
		P8 = 0x12,
		ARGBFP32 = 0x13,
		RGBFP32 = 0x14,
		RGBFP16 = 0x15,
		V8U8 = 0x16,
		G8B8 = 0x17,

		// ThirdGen formats (Halo 3+)
		ABGRFP32 = 0x18,
		ABGRFP16 = 0x19,
		Q8W8V8U8 = 0x1A,
		A2R10G10B10 = 0x1B,
		A16B16G16R16 = 0x1C,
		V16U16 = 0x1D,
		DXT3a = 0x1E,
		DXT5a = 0x1F,
		DXT3a_1111 = 0x20,
		DXN = 0x21,
		CTX1 = 0x22,
		DXT3a_alpha = 0x23,
		DXT3a_mono = 0x24,
		DXT5a_alpha = 0x25,
		DXT5a_mono = 0x26,
		DXN_mono_alpha = 0x27,
	}
}
