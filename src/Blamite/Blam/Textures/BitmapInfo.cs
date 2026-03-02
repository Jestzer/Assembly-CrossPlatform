using System;

namespace Blamite.Blam.Textures
{
	/// <summary>
	///     Metadata for a single bitmap image entry from a bitm tag's Bitmaps tagblock.
	/// </summary>
	public class BitmapInfo
	{
		public int Width { get; set; }
		public int Height { get; set; }
		public int Depth { get; set; }
		public BitmapType Type { get; set; }
		public BitmapFormat Format { get; set; }
		public BitmapFlags Flags { get; set; }
		public int MipmapCount { get; set; }

		/// <summary>
		///     Raw pixels offset value from the tag (offset 0x18 in the element).
		///     For Halo 2 Xbox, the top 2 bits indicate the source file
		///     (00=local, 01=mainmenu, 10=shared, 11=sp_shared).
		/// </summary>
		public int PixelsOffset { get; set; }

		/// <summary>
		///     LOD1 raw data offset (offset 0x1C in the element). Paired with Lod1Size.
		///     Uses the same 2-bit source file encoding as PixelsOffset.
		/// </summary>
		public int Lod1Offset { get; set; }

		/// <summary>
		///     Size in bytes of the highest-detail LOD pixel data.
		/// </summary>
		public int Lod1Size { get; set; }

		/// <summary>
		///     For ThirdGen: Asset Datum value from Hardware Textures / Resources tagblock.
		///     Used to look up the resource containing pixel data.
		/// </summary>
		public uint ResourceDatumValue { get; set; }

		/// <summary>
		///     For ThirdGen: whether the texture uses Xbox 360 tiled memory layout.
		/// </summary>
		public bool IsTiled { get; set; }

		/// <summary>
		///     Size of the pixel data in bytes (from tag). Used by ThirdGen reader.
		///     For SecondGen, this is 0 (uses CalculatedBaseMipSize instead).
		/// </summary>
		public int PixelsSize { get; set; }

		/// <summary>
		///     Returns the calculated size in bytes for the base mip level based on
		///     the bitmap's dimensions and format. This is independent of any size
		///     fields in the tag and serves as a reliable fallback.
		/// </summary>
		public int CalculatedBaseMipSize
		{
			get
			{
				if (Width <= 0 || Height <= 0) return 0;

				switch (Format)
				{
					case BitmapFormat.DXT1:
					case BitmapFormat.CTX1:
					case BitmapFormat.DXT3a:
					case BitmapFormat.DXT5a:
					case BitmapFormat.DXT3a_alpha:
					case BitmapFormat.DXT3a_mono:
					case BitmapFormat.DXT5a_alpha:
					case BitmapFormat.DXT5a_mono:
						return Math.Max(1, Width / 4) * Math.Max(1, Height / 4) * 8;
					case BitmapFormat.DXT3:
					case BitmapFormat.DXT5:
					case BitmapFormat.DXN:
					case BitmapFormat.DXN_mono_alpha:
					case BitmapFormat.DXT3a_1111:
						return Math.Max(1, Width / 4) * Math.Max(1, Height / 4) * 16;
					default:
						return BytesPerPixel > 0 ? Width * Height * BytesPerPixel : 0;
				}
			}
		}

		/// <summary>
		///     Whether the pixel data is stored in Xbox swizzled (Morton order) layout.
		/// </summary>
		public bool IsSwizzled => (Flags & BitmapFlags.Swizzled) != 0;

		/// <summary>
		///     Whether the pixel data uses block compression (DXT).
		/// </summary>
		public bool IsCompressed => (Flags & BitmapFlags.Compressed) != 0;

		/// <summary>
		///     Returns the number of bytes per pixel for uncompressed formats,
		///     or 0 for compressed/unsupported formats.
		/// </summary>
		public int BytesPerPixel
		{
			get
			{
				switch (Format)
				{
					case BitmapFormat.ABGRFP32:
						return 16;
					case BitmapFormat.A16B16G16R16:
					case BitmapFormat.ABGRFP16:
						return 8;
					case BitmapFormat.A8R8G8B8:
					case BitmapFormat.X8R8G8B8:
					case BitmapFormat.Q8W8V8U8:
					case BitmapFormat.A2R10G10B10:
					case BitmapFormat.V16U16:
					case BitmapFormat.ARGBFP32:
						return 4;
					case BitmapFormat.R5G6B5:
					case BitmapFormat.A1R5G5B5:
					case BitmapFormat.A4R4G4B4:
					case BitmapFormat.A8Y8:
					case BitmapFormat.V8U8:
					case BitmapFormat.G8B8:
					case BitmapFormat.RGBFP16:
						return 2;
					case BitmapFormat.A8:
					case BitmapFormat.Y8:
					case BitmapFormat.AY8:
					case BitmapFormat.R8:
					case BitmapFormat.P8:
					case BitmapFormat.P8Bump:
						return 1;
					default:
						return 0;
				}
			}
		}

		public override string ToString()
		{
			return $"{Width}x{Height} {Format}";
		}
	}
}
