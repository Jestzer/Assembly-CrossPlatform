using System;

namespace Blamite.Blam.Textures
{
	/// <summary>
	///     Flags for a bitmap image entry, matching the flags in bitm tag plugins.
	/// </summary>
	[Flags]
	public enum BitmapFlags : ushort
	{
		None = 0,
		PowerOfTwo = 1 << 0,
		Compressed = 1 << 1,
		Palettized = 1 << 2,
		Swizzled = 1 << 3,
		Linear = 1 << 4,
		V16U16 = 1 << 5,
		MipMapDebugLevel = 1 << 6,
		PreferLowDetail = 1 << 7,
		Interlaced = 1 << 12,
	}
}
