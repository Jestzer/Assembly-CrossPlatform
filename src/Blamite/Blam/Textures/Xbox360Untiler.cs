using System;

namespace Blamite.Blam.Textures
{
	/// <summary>
	///     Converts Xbox 360 tiled texture data to linear (row-major) layout.
	///     Xbox 360 GPU stores textures in a tiled format for cache efficiency.
	///     Uses the XGAddress2DTiledOffset algorithm to compute tiled memory addresses.
	/// </summary>
	public static class Xbox360Untiler
	{
		/// <summary>
		///     Untiles Xbox 360 texture data from tiled to linear layout.
		/// </summary>
		/// <param name="tiledData">The tiled texture data.</param>
		/// <param name="width">Texture width in pixels.</param>
		/// <param name="height">Texture height in pixels.</param>
		/// <param name="blockWidth">
		///     Block width: 1 for uncompressed formats, 4 for DXT compressed formats.
		/// </param>
		/// <param name="blockHeight">
		///     Block height: 1 for uncompressed formats, 4 for DXT compressed formats.
		/// </param>
		/// <param name="bytesPerBlock">
		///     Bytes per block: BPP for uncompressed, 8 for DXT1, 16 for DXT3/DXT5.
		/// </param>
		/// <returns>Untiled linear data, or the original data if untiling fails.</returns>
		public static byte[] Untile(byte[] tiledData, int width, int height,
			int blockWidth, int blockHeight, int bytesPerBlock)
		{
			if (tiledData == null || width <= 0 || height <= 0 || bytesPerBlock <= 0)
				return tiledData;

			// Calculate dimensions in blocks (or pixels for uncompressed)
			int blocksWide = (width + blockWidth - 1) / blockWidth;
			int blocksHigh = (height + blockHeight - 1) / blockHeight;

			// Xbox 360 GPU tiling pads textures to 32-block aligned dimensions.
			// The tiled data buffer must be large enough for the aligned dimensions,
			// otherwise edge blocks whose tiled offsets fall in the padded region
			// will fail the bounds check and appear as missing chunks.
			int alignedBlocksWide = (blocksWide + 31) & ~31;
			int alignedBlocksHigh = (blocksHigh + 31) & ~31;
			int alignedTiledSize = alignedBlocksWide * alignedBlocksHigh * bytesPerBlock;

			if (tiledData.Length < alignedTiledSize)
			{
				var padded = new byte[alignedTiledSize];
				Buffer.BlockCopy(tiledData, 0, padded, 0, tiledData.Length);
				tiledData = padded;
			}

			int linearSize = blocksWide * blocksHigh * bytesPerBlock;
			var linearData = new byte[linearSize];

			for (int y = 0; y < blocksHigh; y++)
			{
				for (int x = 0; x < blocksWide; x++)
				{
					// Get the tiled texel index using the Xbox 360 tiling algorithm
					int tiledIndex = XGAddress2DTiledOffset(x, y, blocksWide, bytesPerBlock);
					int tiledOffset = tiledIndex * bytesPerBlock;
					int linearOffset = (y * blocksWide + x) * bytesPerBlock;

					if (tiledOffset >= 0 && tiledOffset + bytesPerBlock <= tiledData.Length &&
					    linearOffset + bytesPerBlock <= linearData.Length)
					{
						Buffer.BlockCopy(tiledData, tiledOffset, linearData, linearOffset, bytesPerBlock);
					}
				}
			}

			return linearData;
		}

		/// <summary>
		///     Convenience method that determines block parameters from BitmapInfo.
		/// </summary>
		public static byte[] Untile(byte[] tiledData, BitmapInfo info)
		{
			int blockWidth, blockHeight, bytesPerBlock;
			GetBlockParameters(info, out blockWidth, out blockHeight, out bytesPerBlock);

			if (bytesPerBlock <= 0)
				return tiledData; // Unknown format, return as-is

			return Untile(tiledData, info.Width, info.Height, blockWidth, blockHeight, bytesPerBlock);
		}

		/// <summary>
		///     Xbox 360 XGAddress2DTiledOffset — computes the texel index in tiled memory
		///     for a given (x, y) coordinate. Multiply the result by texelPitch to get
		///     the byte offset in the tiled data.
		/// </summary>
		/// <param name="x">X coordinate (pixels for uncompressed, blocks for DXT).</param>
		/// <param name="y">Y coordinate (pixels for uncompressed, blocks for DXT).</param>
		/// <param name="width">Texture width in the same units as x.</param>
		/// <param name="texelPitch">Bytes per texel/block (1, 2, 4, 8, or 16).</param>
		/// <returns>Texel index in tiled data. Multiply by texelPitch for byte offset.</returns>
		private static int XGAddress2DTiledOffset(int x, int y, int width, int texelPitch)
		{
			int alignedWidth = (width + 31) & ~31;

			// log2(texelPitch)
			int logBpp;
			switch (texelPitch)
			{
				case 1: logBpp = 0; break;
				case 2: logBpp = 1; break;
				case 4: logBpp = 2; break;
				case 8: logBpp = 3; break;
				case 16: logBpp = 4; break;
				default: logBpp = 0; break;
			}

			int macro = ((x >> 5) + (y >> 5) * (alignedWidth >> 5)) << (logBpp + 7);
			int micro = (((x & 7) + ((y & 6) << 2)) << logBpp);

			int offset = macro +
			             ((micro & ~0xF) << 1) + (micro & 0xF) +
			             ((y & 8) << (3 + logBpp)) +
			             ((y & 1) << 4);

			return (((offset & ~0x1FF) << 3) +
			        ((offset & 448) << 2) +
			        (offset & 63) +
			        ((y & 16) << 7) +
			        (((((y & 8) >> 2) + (x >> 3)) & 3) << 6)) >> logBpp;
		}

		private static void GetBlockParameters(BitmapInfo info,
			out int blockWidth, out int blockHeight, out int bytesPerBlock)
		{
			switch (info.Format)
			{
				case BitmapFormat.DXT1:
				case BitmapFormat.CTX1:
				case BitmapFormat.DXT3a:
				case BitmapFormat.DXT5a:
				case BitmapFormat.DXT3a_alpha:
				case BitmapFormat.DXT3a_mono:
				case BitmapFormat.DXT5a_alpha:
				case BitmapFormat.DXT5a_mono:
					blockWidth = 4;
					blockHeight = 4;
					bytesPerBlock = 8;
					break;

				case BitmapFormat.DXT3:
				case BitmapFormat.DXT5:
				case BitmapFormat.DXN:
				case BitmapFormat.DXN_mono_alpha:
				case BitmapFormat.DXT3a_1111:
					blockWidth = 4;
					blockHeight = 4;
					bytesPerBlock = 16;
					break;

				default:
					blockWidth = 1;
					blockHeight = 1;
					bytesPerBlock = info.BytesPerPixel;
					break;
			}
		}
	}
}
