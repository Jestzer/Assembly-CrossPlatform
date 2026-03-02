using System;

namespace Blamite.Blam.Textures
{
	/// <summary>
	///     Handles Xbox texture unswizzling (Morton order / Z-order curve).
	///     Xbox GPU stores uncompressed textures in a swizzled layout for cache locality.
	///     This class converts from swizzled to linear row-major layout.
	/// </summary>
	public static class XboxSwizzle
	{
		/// <summary>
		///     Converts swizzled (Morton order) texture data to linear row-major layout.
		/// </summary>
		/// <param name="data">The swizzled pixel data.</param>
		/// <param name="width">Texture width in pixels (must be power of 2).</param>
		/// <param name="height">Texture height in pixels (must be power of 2).</param>
		/// <param name="bytesPerPixel">Bytes per pixel (1, 2, or 4).</param>
		/// <returns>Linear row-major pixel data.</returns>
		public static byte[] Unswizzle(byte[] data, int width, int height, int bytesPerPixel)
		{
			if (width <= 0 || height <= 0 || bytesPerPixel <= 0)
				throw new ArgumentException("Invalid dimensions or bytes per pixel.");

			int totalPixels = width * height;
			int expectedSize = totalPixels * bytesPerPixel;

			// If data is smaller than expected, work with what we have
			if (data.Length < expectedSize)
				expectedSize = data.Length;

			byte[] output = new byte[totalPixels * bytesPerPixel];

			// Build lookup tables for separating interleaved bits
			int[] xMask = BuildMaskTable(width, height, true);
			int[] yMask = BuildMaskTable(width, height, false);

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					int swizzledIndex = xMask[x] | yMask[y];
					int linearIndex = y * width + x;

					int srcOffset = swizzledIndex * bytesPerPixel;
					int dstOffset = linearIndex * bytesPerPixel;

					if (srcOffset + bytesPerPixel <= data.Length && dstOffset + bytesPerPixel <= output.Length)
					{
						Buffer.BlockCopy(data, srcOffset, output, dstOffset, bytesPerPixel);
					}
				}
			}

			return output;
		}

		/// <summary>
		///     Builds a mask lookup table for one axis of the Morton code.
		///     For the X axis, bits are placed at even positions (0, 2, 4, ...).
		///     For the Y axis, bits are placed at odd positions (1, 3, 5, ...).
		///     When width != height, the smaller dimension's bits are fully interleaved
		///     and the larger dimension's extra bits are concatenated above.
		/// </summary>
		private static int[] BuildMaskTable(int width, int height, bool isXAxis)
		{
			int size = isXAxis ? width : height;
			int[] table = new int[size];

			int minDim = Math.Min(width, height);
			int minBits = Log2(minDim);

			for (int i = 0; i < size; i++)
			{
				int result = 0;
				int bit = 0;

				// Interleave the lower bits (up to the smaller dimension)
				for (int b = 0; b < minBits; b++)
				{
					if (((i >> b) & 1) != 0)
					{
						int shift = isXAxis ? (b * 2) : (b * 2 + 1);
						result |= 1 << shift;
					}
				}

				// For the larger dimension, append remaining high bits above the interleaved region
				if (isXAxis && width > height)
				{
					int totalInterleavedBits = minBits * 2;
					for (int b = minBits; b < Log2(width); b++)
					{
						if (((i >> b) & 1) != 0)
							result |= 1 << (totalInterleavedBits + (b - minBits));
					}
				}
				else if (!isXAxis && height > width)
				{
					int totalInterleavedBits = minBits * 2;
					for (int b = minBits; b < Log2(height); b++)
					{
						if (((i >> b) & 1) != 0)
							result |= 1 << (totalInterleavedBits + (b - minBits));
					}
				}

				table[i] = result;
			}

			return table;
		}

		private static int Log2(int value)
		{
			int result = 0;
			while ((1 << result) < value)
				result++;
			return result;
		}
	}
}
