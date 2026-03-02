using System;

namespace Blamite.Blam.Textures
{
	/// <summary>
	///     Decodes raw bitmap pixel data to BGRA32, dispatching to the appropriate
	///     decompressor based on the bitmap's format and flags.
	/// </summary>
	public static class BitmapDecoder
	{
		/// <summary>
		///     Decodes raw bitmap pixel data to a BGRA32 byte array.
		/// </summary>
		/// <param name="info">The bitmap metadata.</param>
		/// <param name="rawData">The raw pixel data bytes from the map file.</param>
		/// <param name="bigEndian">Whether the source data is big-endian.</param>
		/// <returns>BGRA32 pixel data, or null if the format is unsupported.</returns>
		public static byte[] Decode(BitmapInfo info, byte[] rawData, bool bigEndian = true)
		{
			if (info == null || rawData == null || rawData.Length == 0)
				return null;

			byte[] data = rawData;

			// Unswizzle if needed (only for uncompressed, swizzled textures)
			if (info.IsSwizzled && !info.IsCompressed && info.BytesPerPixel > 0)
			{
				data = XboxSwizzle.Unswizzle(data, info.Width, info.Height, info.BytesPerPixel);
			}

			switch (info.Format)
			{
				case BitmapFormat.DXT1:
					return DxtDecoder.DecodeDxt1(data, info.Width, info.Height, bigEndian);
				case BitmapFormat.DXT3:
					return DxtDecoder.DecodeDxt3(data, info.Width, info.Height, bigEndian);
				case BitmapFormat.DXT5:
					return DxtDecoder.DecodeDxt5(data, info.Width, info.Height, bigEndian);

				case BitmapFormat.A8R8G8B8:
					return DecodeA8R8G8B8(data, info.Width, info.Height, bigEndian);
				case BitmapFormat.X8R8G8B8:
					return DecodeX8R8G8B8(data, info.Width, info.Height, bigEndian);

				case BitmapFormat.R5G6B5:
					return DecodeR5G6B5(data, info.Width, info.Height, bigEndian);
				case BitmapFormat.A1R5G5B5:
					return DecodeA1R5G5B5(data, info.Width, info.Height, bigEndian);
				case BitmapFormat.A4R4G4B4:
					return DecodeA4R4G4B4(data, info.Width, info.Height, bigEndian);

				case BitmapFormat.A8:
					return DecodeA8(data, info.Width, info.Height);
				case BitmapFormat.Y8:
					return DecodeY8(data, info.Width, info.Height);
				case BitmapFormat.AY8:
					return DecodeAY8(data, info.Width, info.Height);
				case BitmapFormat.A8Y8:
					return DecodeA8Y8(data, info.Width, info.Height, bigEndian);

				case BitmapFormat.V8U8:
					return DecodeV8U8(data, info.Width, info.Height, bigEndian);
				case BitmapFormat.G8B8:
					return DecodeG8B8(data, info.Width, info.Height, bigEndian);

				case BitmapFormat.P8:
				case BitmapFormat.P8Bump:
					return DecodeP8(data, info.Width, info.Height);

				default:
					return null;
			}
		}

		/// <summary>
		///     Returns a human-readable name for the format, or null if unknown.
		/// </summary>
		public static string GetFormatName(BitmapFormat format)
		{
			return format.ToString();
		}

		private static byte[] DecodeA8R8G8B8(byte[] data, int width, int height, bool bigEndian)
		{
			int pixelCount = width * height;
			byte[] output = new byte[pixelCount * 4];

			for (int i = 0; i < pixelCount; i++)
			{
				int src = i * 4;
				if (src + 4 > data.Length) break;

				byte a, r, g, b;
				if (bigEndian)
				{
					a = data[src + 0]; r = data[src + 1]; g = data[src + 2]; b = data[src + 3];
				}
				else
				{
					b = data[src + 0]; g = data[src + 1]; r = data[src + 2]; a = data[src + 3];
				}

				int dst = i * 4;
				output[dst + 0] = b;
				output[dst + 1] = g;
				output[dst + 2] = r;
				output[dst + 3] = a;
			}

			return output;
		}

		private static byte[] DecodeX8R8G8B8(byte[] data, int width, int height, bool bigEndian)
		{
			int pixelCount = width * height;
			byte[] output = new byte[pixelCount * 4];

			for (int i = 0; i < pixelCount; i++)
			{
				int src = i * 4;
				if (src + 4 > data.Length) break;

				byte r, g, b;
				if (bigEndian)
				{
					r = data[src + 1]; g = data[src + 2]; b = data[src + 3];
				}
				else
				{
					b = data[src + 0]; g = data[src + 1]; r = data[src + 2];
				}

				int dst = i * 4;
				output[dst + 0] = b;
				output[dst + 1] = g;
				output[dst + 2] = r;
				output[dst + 3] = 255; // Force alpha to opaque
			}

			return output;
		}

		private static byte[] DecodeR5G6B5(byte[] data, int width, int height, bool bigEndian)
		{
			int pixelCount = width * height;
			byte[] output = new byte[pixelCount * 4];

			for (int i = 0; i < pixelCount; i++)
			{
				int src = i * 2;
				if (src + 2 > data.Length) break;

				ushort pixel = bigEndian
					? (ushort)((data[src] << 8) | data[src + 1])
					: (ushort)(data[src] | (data[src + 1] << 8));

				int r5 = (pixel >> 11) & 0x1F;
				int g6 = (pixel >> 5) & 0x3F;
				int b5 = pixel & 0x1F;

				int dst = i * 4;
				output[dst + 0] = (byte)((b5 << 3) | (b5 >> 2));
				output[dst + 1] = (byte)((g6 << 2) | (g6 >> 4));
				output[dst + 2] = (byte)((r5 << 3) | (r5 >> 2));
				output[dst + 3] = 255;
			}

			return output;
		}

		private static byte[] DecodeA1R5G5B5(byte[] data, int width, int height, bool bigEndian)
		{
			int pixelCount = width * height;
			byte[] output = new byte[pixelCount * 4];

			for (int i = 0; i < pixelCount; i++)
			{
				int src = i * 2;
				if (src + 2 > data.Length) break;

				ushort pixel = bigEndian
					? (ushort)((data[src] << 8) | data[src + 1])
					: (ushort)(data[src] | (data[src + 1] << 8));

				int a = (pixel >> 15) & 0x1;
				int r5 = (pixel >> 10) & 0x1F;
				int g5 = (pixel >> 5) & 0x1F;
				int b5 = pixel & 0x1F;

				int dst = i * 4;
				output[dst + 0] = (byte)((b5 << 3) | (b5 >> 2));
				output[dst + 1] = (byte)((g5 << 3) | (g5 >> 2));
				output[dst + 2] = (byte)((r5 << 3) | (r5 >> 2));
				output[dst + 3] = (byte)(a * 255);
			}

			return output;
		}

		private static byte[] DecodeA4R4G4B4(byte[] data, int width, int height, bool bigEndian)
		{
			int pixelCount = width * height;
			byte[] output = new byte[pixelCount * 4];

			for (int i = 0; i < pixelCount; i++)
			{
				int src = i * 2;
				if (src + 2 > data.Length) break;

				ushort pixel = bigEndian
					? (ushort)((data[src] << 8) | data[src + 1])
					: (ushort)(data[src] | (data[src + 1] << 8));

				int a4 = (pixel >> 12) & 0xF;
				int r4 = (pixel >> 8) & 0xF;
				int g4 = (pixel >> 4) & 0xF;
				int b4 = pixel & 0xF;

				int dst = i * 4;
				output[dst + 0] = (byte)(b4 | (b4 << 4));
				output[dst + 1] = (byte)(g4 | (g4 << 4));
				output[dst + 2] = (byte)(r4 | (r4 << 4));
				output[dst + 3] = (byte)(a4 | (a4 << 4));
			}

			return output;
		}

		private static byte[] DecodeA8(byte[] data, int width, int height)
		{
			int pixelCount = width * height;
			byte[] output = new byte[pixelCount * 4];

			for (int i = 0; i < pixelCount && i < data.Length; i++)
			{
				int dst = i * 4;
				output[dst + 0] = data[i]; // Show alpha as grayscale
				output[dst + 1] = data[i];
				output[dst + 2] = data[i];
				output[dst + 3] = 255;
			}

			return output;
		}

		private static byte[] DecodeY8(byte[] data, int width, int height)
		{
			int pixelCount = width * height;
			byte[] output = new byte[pixelCount * 4];

			for (int i = 0; i < pixelCount && i < data.Length; i++)
			{
				int dst = i * 4;
				output[dst + 0] = data[i];
				output[dst + 1] = data[i];
				output[dst + 2] = data[i];
				output[dst + 3] = 255;
			}

			return output;
		}

		private static byte[] DecodeAY8(byte[] data, int width, int height)
		{
			// AY8: combined alpha + luminance in one byte
			// Upper 4 bits = alpha, lower 4 bits = luminance (or full byte as both)
			int pixelCount = width * height;
			byte[] output = new byte[pixelCount * 4];

			for (int i = 0; i < pixelCount && i < data.Length; i++)
			{
				byte val = data[i];
				int dst = i * 4;
				output[dst + 0] = val;
				output[dst + 1] = val;
				output[dst + 2] = val;
				output[dst + 3] = val;
			}

			return output;
		}

		private static byte[] DecodeA8Y8(byte[] data, int width, int height, bool bigEndian)
		{
			int pixelCount = width * height;
			byte[] output = new byte[pixelCount * 4];

			for (int i = 0; i < pixelCount; i++)
			{
				int src = i * 2;
				if (src + 2 > data.Length) break;

				byte a, y;
				if (bigEndian)
				{
					a = data[src]; y = data[src + 1];
				}
				else
				{
					y = data[src]; a = data[src + 1];
				}

				int dst = i * 4;
				output[dst + 0] = y;
				output[dst + 1] = y;
				output[dst + 2] = y;
				output[dst + 3] = a;
			}

			return output;
		}

		private static byte[] DecodeV8U8(byte[] data, int width, int height, bool bigEndian)
		{
			// V8U8: signed normal map, two signed 8-bit channels
			// Visualize as: R = (u + 128), G = (v + 128), B = 255
			int pixelCount = width * height;
			byte[] output = new byte[pixelCount * 4];

			for (int i = 0; i < pixelCount; i++)
			{
				int src = i * 2;
				if (src + 2 > data.Length) break;

				byte u, v;
				if (bigEndian)
				{
					v = data[src]; u = data[src + 1];
				}
				else
				{
					u = data[src]; v = data[src + 1];
				}

				int dst = i * 4;
				output[dst + 0] = 255;                          // B
				output[dst + 1] = (byte)((sbyte)v + 128);       // G
				output[dst + 2] = (byte)((sbyte)u + 128);       // R
				output[dst + 3] = 255;
			}

			return output;
		}

		private static byte[] DecodeG8B8(byte[] data, int width, int height, bool bigEndian)
		{
			// G8B8: two-channel format, visualize as R=0, G=channel1, B=channel2
			int pixelCount = width * height;
			byte[] output = new byte[pixelCount * 4];

			for (int i = 0; i < pixelCount; i++)
			{
				int src = i * 2;
				if (src + 2 > data.Length) break;

				byte g, b;
				if (bigEndian)
				{
					g = data[src]; b = data[src + 1];
				}
				else
				{
					b = data[src]; g = data[src + 1];
				}

				int dst = i * 4;
				output[dst + 0] = b;
				output[dst + 1] = g;
				output[dst + 2] = 0;
				output[dst + 3] = 255;
			}

			return output;
		}

		private static byte[] DecodeP8(byte[] data, int width, int height)
		{
			// P8/P8Bump: 8-bit palettized. Without the palette, render as grayscale.
			int pixelCount = width * height;
			byte[] output = new byte[pixelCount * 4];

			for (int i = 0; i < pixelCount && i < data.Length; i++)
			{
				int dst = i * 4;
				output[dst + 0] = data[i];
				output[dst + 1] = data[i];
				output[dst + 2] = data[i];
				output[dst + 3] = 255;
			}

			return output;
		}
	}
}
