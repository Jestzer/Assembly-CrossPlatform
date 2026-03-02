using System;
using System.Collections.Generic;
using System.IO;
using Blamite.IO;

namespace Blamite.Blam.Textures
{
	/// <summary>
	///     Reads bitmap tag metadata and pixel data from Halo 2 Xbox/Vista (SecondGen) cache files.
	/// </summary>
	public class SecondGenBitmapTagReader
	{
		// Offset of the Bitmaps tagblock reflexive within the bitm tag (from bitm.xml)
		private const int BitmapsTagBlockOffset = 0x44;

		// Size of each bitmap element in the Bitmaps tagblock
		private const int BitmapElementSize = 0x74;

		// Field offsets within each bitmap element (from Halo2Xbox/bitm.xml)
		private const int WidthOffset = 0x04;
		private const int HeightOffset = 0x06;
		private const int DepthOffset = 0x08;
		private const int TypeOffset = 0x0A;
		private const int FormatOffset = 0x0C;
		private const int FlagsOffset = 0x0E;
		private const int MipmapCountOffset = 0x14;
		private const int PixelsOffsetField = 0x18;
		private const int Lod1OffsetField = 0x1C;
		private const int Lod1SizeOffset = 0x34;

		/// <summary>
		///     Reads all bitmap entries from a bitm tag.
		/// </summary>
		/// <param name="tag">The bitm tag to read.</param>
		/// <param name="reader">A reader positioned on the cache file.</param>
		/// <param name="cacheFile">The cache file containing the tag.</param>
		/// <returns>List of bitmap info entries, or empty list if none.</returns>
		public List<BitmapInfo> ReadBitmapTag(ITag tag, IReader reader, ICacheFile cacheFile)
		{
			var results = new List<BitmapInfo>();

			if (tag?.MetaLocation == null || cacheFile?.MetaArea == null)
				return results;

			// Seek to the tag's metadata
			uint tagOffset = (uint)tag.MetaLocation.AsOffset();

			// Read the Bitmaps tagblock reflexive at offset 0x44
			reader.SeekTo(tagOffset + BitmapsTagBlockOffset);
			int count = reader.ReadInt32();
			uint address = reader.ReadUInt32();

			if (count <= 0 || count > 4096) // Sanity check
				return results;

			// Convert the pointer to a file offset
			if (!cacheFile.MetaArea.ContainsPointer(address))
				return results;

			uint elementsOffset = cacheFile.MetaArea.PointerToOffset(address);

			// Read each bitmap element
			for (int i = 0; i < count; i++)
			{
				uint elementStart = elementsOffset + (uint)(i * BitmapElementSize);
				reader.SeekTo(elementStart + WidthOffset);
				short width = reader.ReadInt16();

				reader.SeekTo(elementStart + HeightOffset);
				short height = reader.ReadInt16();

				reader.SeekTo(elementStart + DepthOffset);
				byte depth = reader.ReadByte();

				reader.SeekTo(elementStart + TypeOffset);
				ushort type = reader.ReadUInt16();

				reader.SeekTo(elementStart + FormatOffset);
				ushort format = reader.ReadUInt16();

				reader.SeekTo(elementStart + FlagsOffset);
				ushort flags = reader.ReadUInt16();

				reader.SeekTo(elementStart + MipmapCountOffset);
				short mipmapCount = reader.ReadInt16();

				reader.SeekTo(elementStart + PixelsOffsetField);
				int pixelsOffset = reader.ReadInt32();

				reader.SeekTo(elementStart + Lod1OffsetField);
				int lod1Offset = reader.ReadInt32();

				reader.SeekTo(elementStart + Lod1SizeOffset);
				int lod1Size = reader.ReadInt32();

				if (width <= 0 || height <= 0)
					continue;

				results.Add(new BitmapInfo
				{
					Width = width,
					Height = height,
					Depth = depth > 0 ? depth : 1,
					Type = (BitmapType)type,
					Format = (BitmapFormat)format,
					Flags = (BitmapFlags)flags,
					MipmapCount = mipmapCount,
					PixelsOffset = pixelsOffset,
					Lod1Offset = lod1Offset,
					Lod1Size = lod1Size,
				});
			}

			return results;
		}

		/// <summary>
		///     Reads raw pixel data for a bitmap entry from the appropriate source file.
		///     Tries multiple offset/size strategies to handle both stock and modded maps:
		///     1. LOD1 Offset (0x1C) + calculated size (standard maps)
		///     2. Pixels Offset (0x18) + calculated size (modded maps that update 0x18)
		///     3. LOD1 Offset (0x1C) + LOD1 Size (0x34) (fallback with tag-stored size)
		///     4. Pixels Offset (0x18) + LOD1 Size (0x34) (last resort)
		///     For Halo 2 Xbox, the top 2 bits of the offset indicate the source file:
		///     00 = current map, 01 = mainmenu.map, 10 = shared.map, 11 = single_player_shared.map.
		/// </summary>
		/// <param name="info">The bitmap info entry.</param>
		/// <param name="mapFilePath">Path to the currently loaded map file.</param>
		/// <param name="endianness">The endianness to use when reading.</param>
		/// <returns>Raw pixel data bytes, or null if the data cannot be read.</returns>
		/// <inheritdoc cref="ReadPixelData(BitmapInfo, string, Endian, Dictionary{int, string})"/>
		public byte[] ReadPixelData(BitmapInfo info, string mapFilePath, Endian endianness)
		{
			return ReadPixelData(info, mapFilePath, endianness, null);
		}

		/// <summary>
		///     Reads raw pixel data for a bitmap entry from the appropriate source file.
		///     Tries multiple offset/size strategies to handle both stock and modded maps.
		///     For Halo 2 Xbox, the top 2 bits of the offset indicate the source file:
		///     00 = current map, 01 = mainmenu.map, 10 = shared.map, 11 = single_player_shared.map.
		/// </summary>
		/// <param name="info">The bitmap info entry.</param>
		/// <param name="mapFilePath">Path to the currently loaded map file.</param>
		/// <param name="endianness">The endianness to use when reading.</param>
		/// <param name="sourceOverrides">
		///     Optional dictionary mapping source index (1=mainmenu, 2=shared, 3=sp_shared)
		///     to user-specified file paths. Checked before default resolution.
		/// </param>
		/// <returns>Raw pixel data bytes, or null if the data cannot be read.</returns>
		public byte[] ReadPixelData(BitmapInfo info, string mapFilePath, Endian endianness,
			Dictionary<int, string> sourceOverrides)
		{
			if (info == null)
				return null;

			int calcSize = info.CalculatedBaseMipSize;
			int lod1Size = info.Lod1Size;

			// Build list of (offset, size) candidates to try in priority order
			var candidates = new List<(int rawOffset, int size)>();

			// Strategy 1: LOD1 Offset + calculated size (standard stock maps)
			if (calcSize > 0)
				candidates.Add((info.Lod1Offset, calcSize));

			// Strategy 2: Pixels Offset + calculated size (modded maps)
			if (calcSize > 0 && info.PixelsOffset != info.Lod1Offset)
				candidates.Add((info.PixelsOffset, calcSize));

			// Strategy 3: LOD1 Offset + LOD1 Size (tag-stored size fallback)
			if (lod1Size > 0)
				candidates.Add((info.Lod1Offset, lod1Size));

			// Strategy 4: Pixels Offset + LOD1 Size (last resort)
			if (lod1Size > 0 && info.PixelsOffset != info.Lod1Offset)
				candidates.Add((info.PixelsOffset, lod1Size));

			foreach (var (rawOffset, dataSize) in candidates)
			{
				if (dataSize <= 0 || dataSize > 64 * 1024 * 1024)
					continue;

				int fileOffset = rawOffset & 0x3FFFFFFF;
				if (fileOffset == 0 && (rawOffset >> 30 & 0x3) == 0)
					continue; // Offset 0 in local map is the header, not valid pixel data

				byte[] result = TryReadFromOffset(rawOffset, dataSize, mapFilePath, endianness, sourceOverrides);
				if (result != null)
					return result;
			}

			return null;
		}

		/// <summary>
		///     Returns the source file index (0-3) for the active offset used to read pixel data.
		/// </summary>
		public static int GetActiveSourceIndex(BitmapInfo info)
		{
			int rawOffset = info.Lod1Offset;
			if ((rawOffset & 0x3FFFFFFF) == 0 && info.PixelsOffset != 0)
				rawOffset = info.PixelsOffset;
			return (rawOffset >> 30) & 0x3;
		}

		/// <summary>
		///     Attempts to read pixel data from a specific raw offset.
		/// </summary>
		private byte[] TryReadFromOffset(int rawOffset, int dataSize, string mapFilePath,
			Endian endianness, Dictionary<int, string> sourceOverrides)
		{
			int sourceIndex = (rawOffset >> 30) & 0x3;
			int fileOffset = rawOffset & 0x3FFFFFFF;

			// Check user-provided overrides first
			string sourceFilePath = null;
			if (sourceOverrides != null && sourceOverrides.TryGetValue(sourceIndex, out string overridePath))
				sourceFilePath = overridePath;
			sourceFilePath ??= ResolveSourceFile(sourceIndex, mapFilePath);

			if (sourceFilePath == null || !File.Exists(sourceFilePath))
				return null;

			try
			{
				using (var stream = File.OpenRead(sourceFilePath))
				using (var reader = new EndianReader(stream, endianness))
				{
					if (fileOffset + dataSize > stream.Length)
						return null;

					reader.SeekTo(fileOffset);
					return reader.ReadBlock(dataSize);
				}
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		///     Resolves the file offset, data size, and source file path for the active
		///     pixel data location. Uses the same candidate priority as ReadPixelData.
		///     Used to determine where to write replacement pixel data during DDS injection.
		/// </summary>
		/// <returns>Resolved location tuple, or null if no valid candidate is found.</returns>
		public (int fileOffset, int dataSize, string sourceFilePath)? ResolvePixelDataLocation(
			BitmapInfo info, string mapFilePath, Dictionary<int, string> sourceOverrides)
		{
			if (info == null)
				return null;

			int calcSize = info.CalculatedBaseMipSize;
			int lod1Size = info.Lod1Size;

			var candidates = new List<(int rawOffset, int size)>();

			if (calcSize > 0)
				candidates.Add((info.Lod1Offset, calcSize));
			if (calcSize > 0 && info.PixelsOffset != info.Lod1Offset)
				candidates.Add((info.PixelsOffset, calcSize));
			if (lod1Size > 0)
				candidates.Add((info.Lod1Offset, lod1Size));
			if (lod1Size > 0 && info.PixelsOffset != info.Lod1Offset)
				candidates.Add((info.PixelsOffset, lod1Size));

			foreach (var (rawOffset, dataSize) in candidates)
			{
				if (dataSize <= 0 || dataSize > 64 * 1024 * 1024)
					continue;

				int fileOffset = rawOffset & 0x3FFFFFFF;
				if (fileOffset == 0 && (rawOffset >> 30 & 0x3) == 0)
					continue;

				int sourceIndex = (rawOffset >> 30) & 0x3;
				string sourceFilePath = null;
				if (sourceOverrides != null && sourceOverrides.TryGetValue(sourceIndex, out string overridePath))
					sourceFilePath = overridePath;
				sourceFilePath ??= ResolveSourceFile(sourceIndex, mapFilePath);

				if (sourceFilePath == null || !File.Exists(sourceFilePath))
					continue;

				try
				{
					long fileLength = new FileInfo(sourceFilePath).Length;
					if (fileOffset + dataSize <= fileLength)
						return (fileOffset, dataSize, sourceFilePath);
				}
				catch
				{
					continue;
				}
			}

			return null;
		}

		/// <summary>
		///     Gets a human-readable description of which source file the bitmap data comes from.
		/// </summary>
		public static string GetSourceDescription(int pixelsOffset)
		{
			int sourceIndex = (pixelsOffset >> 30) & 0x3;
			return sourceIndex switch
			{
				0 => "local map",
				1 => "mainmenu.map",
				2 => "shared.map",
				3 => "single_player_shared.map",
				_ => "unknown",
			};
		}

		private static string ResolveSourceFile(int sourceIndex, string mapFilePath)
		{
			if (sourceIndex == 0)
				return mapFilePath;

			string mapDir = Path.GetDirectoryName(mapFilePath);
			if (mapDir == null)
				return null;

			return sourceIndex switch
			{
				1 => Path.Combine(mapDir, "mainmenu.map"),
				2 => Path.Combine(mapDir, "shared.map"),
				3 => Path.Combine(mapDir, "single_player_shared.map"),
				_ => null,
			};
		}
	}
}
