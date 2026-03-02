using System;

namespace Blamite.Blam.Textures
{
	/// <summary>
	///     Decodes DXT1/DXT3/DXT5 (S3TC) block-compressed texture data to BGRA32.
	///     Xbox 360 stores DXT blocks with 8-in-16 byte swap (every pair of bytes
	///     is swapped). ReadUInt16 with bigEndian naturally handles this for 16-bit
	///     values; 32-bit values and packed byte fields need explicit pair-swapping.
	/// </summary>
	public static class DxtDecoder
	{
		/// <summary>
		///     Decodes DXT1 compressed data to BGRA32.
		/// </summary>
		/// <param name="data">The DXT1 compressed data (8 bytes per 4x4 block).</param>
		/// <param name="width">Texture width in pixels.</param>
		/// <param name="height">Texture height in pixels.</param>
		/// <param name="bigEndian">Whether the data is big-endian (Xbox 360 with 8-in-16 swap).</param>
		/// <returns>BGRA32 pixel data (4 bytes per pixel).</returns>
		public static byte[] DecodeDxt1(byte[] data, int width, int height, bool bigEndian = true)
		{
			byte[] output = new byte[width * height * 4];
			int blocksX = (width + 3) / 4;
			int blocksY = (height + 3) / 4;
			int offset = 0;

			for (int by = 0; by < blocksY; by++)
			{
				for (int bx = 0; bx < blocksX; bx++)
				{
					if (offset + 8 > data.Length)
						break;

					// Read two 16-bit color endpoints (BE uint16 matches 8-in-16 swap)
					ushort c0 = ReadUInt16(data, offset, bigEndian);
					ushort c1 = ReadUInt16(data, offset + 2, bigEndian);
					// Color indices: read as two 16-bit words to handle 8-in-16 swap
					uint indices = ReadUInt32_8in16(data, offset + 4, bigEndian);
					offset += 8;

					// Decode RGB565 colors
					DecodeRgb565(c0, out byte r0, out byte g0, out byte b0);
					DecodeRgb565(c1, out byte r1, out byte g1, out byte b1);

					// Build 4-color palette
					byte[] palette = new byte[16]; // 4 colors x BGRA
					palette[0] = b0; palette[1] = g0; palette[2] = r0; palette[3] = 255;
					palette[4] = b1; palette[5] = g1; palette[6] = r1; palette[7] = 255;

					if (c0 > c1)
					{
						// 4-color mode: c2 = 2/3*c0 + 1/3*c1, c3 = 1/3*c0 + 2/3*c1
						palette[8]  = (byte)((2 * b0 + b1 + 1) / 3);
						palette[9]  = (byte)((2 * g0 + g1 + 1) / 3);
						palette[10] = (byte)((2 * r0 + r1 + 1) / 3);
						palette[11] = 255;
						palette[12] = (byte)((b0 + 2 * b1 + 1) / 3);
						palette[13] = (byte)((g0 + 2 * g1 + 1) / 3);
						palette[14] = (byte)((r0 + 2 * r1 + 1) / 3);
						palette[15] = 255;
					}
					else
					{
						// 3-color + transparent mode: c2 = 1/2*c0 + 1/2*c1, c3 = transparent
						palette[8]  = (byte)((b0 + b1) / 2);
						palette[9]  = (byte)((g0 + g1) / 2);
						palette[10] = (byte)((r0 + r1) / 2);
						palette[11] = 255;
						palette[12] = 0; palette[13] = 0; palette[14] = 0; palette[15] = 0;
					}

					// Write 4x4 block
					for (int py = 0; py < 4; py++)
					{
						for (int px = 0; px < 4; px++)
						{
							int x = bx * 4 + px;
							int y = by * 4 + py;
							if (x >= width || y >= height)
							{
								indices >>= 2;
								continue;
							}

							int idx = (int)(indices & 0x3);
							indices >>= 2;

							int dst = (y * width + x) * 4;
							output[dst + 0] = palette[idx * 4 + 0];
							output[dst + 1] = palette[idx * 4 + 1];
							output[dst + 2] = palette[idx * 4 + 2];
							output[dst + 3] = palette[idx * 4 + 3];
						}
					}
				}
			}

			return output;
		}

		/// <summary>
		///     Decodes DXT3 compressed data to BGRA32.
		/// </summary>
		/// <param name="data">The DXT3 compressed data (16 bytes per 4x4 block).</param>
		/// <param name="width">Texture width in pixels.</param>
		/// <param name="height">Texture height in pixels.</param>
		/// <param name="bigEndian">Whether the data is big-endian (Xbox 360 with 8-in-16 swap).</param>
		/// <returns>BGRA32 pixel data.</returns>
		public static byte[] DecodeDxt3(byte[] data, int width, int height, bool bigEndian = true)
		{
			byte[] output = new byte[width * height * 4];
			int blocksX = (width + 3) / 4;
			int blocksY = (height + 3) / 4;
			int offset = 0;

			for (int by = 0; by < blocksY; by++)
			{
				for (int bx = 0; bx < blocksX; bx++)
				{
					if (offset + 16 > data.Length)
						break;

					// First 8 bytes: explicit 4-bit alpha for each pixel (row by row)
					// Xbox 360 8-in-16 swap: swap pairs of bytes within each 16-bit word
					byte[] alphaBytes = new byte[8];
					if (bigEndian)
					{
						for (int i = 0; i < 8; i += 2)
						{
							alphaBytes[i] = data[offset + i + 1];
							alphaBytes[i + 1] = data[offset + i];
						}
					}
					else
					{
						Array.Copy(data, offset, alphaBytes, 0, 8);
					}
					offset += 8;

					// Next 8 bytes: DXT1 color block
					ushort c0 = ReadUInt16(data, offset, bigEndian);
					ushort c1 = ReadUInt16(data, offset + 2, bigEndian);
					uint indices = ReadUInt32_8in16(data, offset + 4, bigEndian);
					offset += 8;

					DecodeRgb565(c0, out byte r0, out byte g0, out byte b0);
					DecodeRgb565(c1, out byte r1, out byte g1, out byte b1);

					// DXT3 always uses 4-color mode regardless of c0/c1 ordering
					byte[] palette = new byte[12]; // 4 colors x BGR (alpha comes from alpha block)
					palette[0] = b0; palette[1] = g0; palette[2] = r0;
					palette[3] = b1; palette[4] = g1; palette[5] = r1;
					palette[6] = (byte)((2 * b0 + b1 + 1) / 3);
					palette[7] = (byte)((2 * g0 + g1 + 1) / 3);
					palette[8] = (byte)((2 * r0 + r1 + 1) / 3);
					palette[9]  = (byte)((b0 + 2 * b1 + 1) / 3);
					palette[10] = (byte)((g0 + 2 * g1 + 1) / 3);
					palette[11] = (byte)((r0 + 2 * r1 + 1) / 3);

					for (int py = 0; py < 4; py++)
					{
						for (int px = 0; px < 4; px++)
						{
							int x = bx * 4 + px;
							int y = by * 4 + py;
							if (x >= width || y >= height)
							{
								indices >>= 2;
								continue;
							}

							int idx = (int)(indices & 0x3);
							indices >>= 2;

							// Extract 4-bit alpha from the alpha block
							// Each row is 2 bytes (16 bits for 4 pixels, 4 bits each)
							// Byte order: pixels are stored left-to-right within each 16-bit row
							int alphaByteIndex = py * 2 + (px / 2);
							int alphaShift = (px % 2) * 4;
							byte alpha4 = (byte)((alphaBytes[alphaByteIndex] >> alphaShift) & 0xF);
							byte alpha = (byte)(alpha4 | (alpha4 << 4)); // Expand 4-bit to 8-bit

							int dst = (y * width + x) * 4;
							output[dst + 0] = palette[idx * 3 + 0];
							output[dst + 1] = palette[idx * 3 + 1];
							output[dst + 2] = palette[idx * 3 + 2];
							output[dst + 3] = alpha;
						}
					}
				}
			}

			return output;
		}

		/// <summary>
		///     Decodes DXT5 compressed data to BGRA32.
		/// </summary>
		/// <param name="data">The DXT5 compressed data (16 bytes per 4x4 block).</param>
		/// <param name="width">Texture width in pixels.</param>
		/// <param name="height">Texture height in pixels.</param>
		/// <param name="bigEndian">Whether the data is big-endian (Xbox 360 with 8-in-16 swap).</param>
		/// <returns>BGRA32 pixel data.</returns>
		public static byte[] DecodeDxt5(byte[] data, int width, int height, bool bigEndian = true)
		{
			byte[] output = new byte[width * height * 4];
			int blocksX = (width + 3) / 4;
			int blocksY = (height + 3) / 4;
			int offset = 0;

			for (int by = 0; by < blocksY; by++)
			{
				for (int bx = 0; bx < blocksX; bx++)
				{
					if (offset + 16 > data.Length)
						break;

					// First 8 bytes: interpolated alpha block
					// Xbox 360 8-in-16 swap: bytes 0,1 are swapped
					byte alpha0, alpha1;
					if (bigEndian)
					{
						alpha0 = data[offset + 1];
						alpha1 = data[offset];
					}
					else
					{
						alpha0 = data[offset];
						alpha1 = data[offset + 1];
					}

					// 6 bytes of 3-bit alpha indices (48 bits for 16 pixels)
					// Xbox 360 8-in-16 swap: pairs of bytes are swapped
					ulong alphaBits = 0;
					if (bigEndian)
					{
						// Read with 8-in-16 pair swap: bytes [3,2,5,4,7,6]
						alphaBits = (ulong)data[offset + 3]
						          | ((ulong)data[offset + 2] << 8)
						          | ((ulong)data[offset + 5] << 16)
						          | ((ulong)data[offset + 4] << 24)
						          | ((ulong)data[offset + 7] << 32)
						          | ((ulong)data[offset + 6] << 40);
					}
					else
					{
						for (int i = 0; i < 6; i++)
							alphaBits |= (ulong)data[offset + 2 + i] << (8 * i);
					}
					offset += 8;

					// Build alpha palette
					byte[] alphaPalette = new byte[8];
					alphaPalette[0] = alpha0;
					alphaPalette[1] = alpha1;
					if (alpha0 > alpha1)
					{
						alphaPalette[2] = (byte)((6 * alpha0 + 1 * alpha1 + 3) / 7);
						alphaPalette[3] = (byte)((5 * alpha0 + 2 * alpha1 + 3) / 7);
						alphaPalette[4] = (byte)((4 * alpha0 + 3 * alpha1 + 3) / 7);
						alphaPalette[5] = (byte)((3 * alpha0 + 4 * alpha1 + 3) / 7);
						alphaPalette[6] = (byte)((2 * alpha0 + 5 * alpha1 + 3) / 7);
						alphaPalette[7] = (byte)((1 * alpha0 + 6 * alpha1 + 3) / 7);
					}
					else
					{
						alphaPalette[2] = (byte)((4 * alpha0 + 1 * alpha1 + 2) / 5);
						alphaPalette[3] = (byte)((3 * alpha0 + 2 * alpha1 + 2) / 5);
						alphaPalette[4] = (byte)((2 * alpha0 + 3 * alpha1 + 2) / 5);
						alphaPalette[5] = (byte)((1 * alpha0 + 4 * alpha1 + 2) / 5);
						alphaPalette[6] = 0;
						alphaPalette[7] = 255;
					}

					// Next 8 bytes: DXT1 color block
					ushort c0 = ReadUInt16(data, offset, bigEndian);
					ushort c1 = ReadUInt16(data, offset + 2, bigEndian);
					uint indices = ReadUInt32_8in16(data, offset + 4, bigEndian);
					offset += 8;

					DecodeRgb565(c0, out byte r0, out byte g0, out byte b0);
					DecodeRgb565(c1, out byte r1, out byte g1, out byte b1);

					// DXT5 always uses 4-color mode
					byte[] palette = new byte[12];
					palette[0] = b0; palette[1] = g0; palette[2] = r0;
					palette[3] = b1; palette[4] = g1; palette[5] = r1;
					palette[6] = (byte)((2 * b0 + b1 + 1) / 3);
					palette[7] = (byte)((2 * g0 + g1 + 1) / 3);
					palette[8] = (byte)((2 * r0 + r1 + 1) / 3);
					palette[9]  = (byte)((b0 + 2 * b1 + 1) / 3);
					palette[10] = (byte)((g0 + 2 * g1 + 1) / 3);
					palette[11] = (byte)((r0 + 2 * r1 + 1) / 3);

					for (int py = 0; py < 4; py++)
					{
						for (int px = 0; px < 4; px++)
						{
							int x = bx * 4 + px;
							int y = by * 4 + py;
							if (x >= width || y >= height)
							{
								indices >>= 2;
								alphaBits >>= 3;
								continue;
							}

							int colorIdx = (int)(indices & 0x3);
							indices >>= 2;

							int alphaIdx = (int)(alphaBits & 0x7);
							alphaBits >>= 3;

							int dst = (y * width + x) * 4;
							output[dst + 0] = palette[colorIdx * 3 + 0];
							output[dst + 1] = palette[colorIdx * 3 + 1];
							output[dst + 2] = palette[colorIdx * 3 + 2];
							output[dst + 3] = alphaPalette[alphaIdx];
						}
					}
				}
			}

			return output;
		}

		private static void DecodeRgb565(ushort color, out byte r, out byte g, out byte b)
		{
			// RGB565: RRRRRGGGGGGBBBBB
			int r5 = (color >> 11) & 0x1F;
			int g6 = (color >> 5) & 0x3F;
			int b5 = color & 0x1F;

			// Expand to 8-bit by replicating high bits into low bits
			r = (byte)((r5 << 3) | (r5 >> 2));
			g = (byte)((g6 << 2) | (g6 >> 4));
			b = (byte)((b5 << 3) | (b5 >> 2));
		}

		private static ushort ReadUInt16(byte[] data, int offset, bool bigEndian)
		{
			if (bigEndian)
				return (ushort)((data[offset] << 8) | data[offset + 1]);
			return (ushort)(data[offset] | (data[offset + 1] << 8));
		}

		/// <summary>
		///     Reads a 32-bit value accounting for Xbox 360's 8-in-16 byte swap.
		///     On Xbox 360, DXT data uses 16-bit word swap, so 32-bit values must
		///     be reconstructed from two big-endian 16-bit words (low word first).
		///     For little-endian, reads as a standard LE uint32.
		/// </summary>
		private static uint ReadUInt32_8in16(byte[] data, int offset, bool bigEndian)
		{
			if (bigEndian)
			{
				// Read two BE uint16 words: first word = low 16 bits, second = high 16
				uint lo = (uint)((data[offset] << 8) | data[offset + 1]);
				uint hi = (uint)((data[offset + 2] << 8) | data[offset + 3]);
				return lo | (hi << 16);
			}
			return (uint)(data[offset] | (data[offset + 1] << 8) |
						  (data[offset + 2] << 16) | (data[offset + 3] << 24));
		}
	}
}
