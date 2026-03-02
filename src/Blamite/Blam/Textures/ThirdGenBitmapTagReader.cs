using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blamite.Blam.Resources;
using Blamite.Blam.ThirdGen;
using Blamite.IO;
using Blamite.Serialization;

namespace Blamite.Blam.Textures
{
	/// <summary>
	///     Reads bitmap tag metadata and pixel data from ThirdGeneration (Halo 3, ODST, Reach, Halo 4)
	///     cache files. Pixel data is stored in resource pages and accessed via the resource system.
	/// </summary>
	public class ThirdGenBitmapTagReader
	{
		// --- Halo 3 / ODST layout constants ---
		private const int H3_BitmapsTagBlockOffset = 0x60;
		private const int H3_BitmapElementSize = 0x30;
		private const int H3_WidthOffset = 0x04;
		private const int H3_HeightOffset = 0x06;
		private const int H3_DepthOffset = 0x08;
		private const int H3_MoreFlagsOffset = 0x09;
		private const int H3_TypeOffset = 0x0A;
		private const int H3_FormatOffset = 0x0C;
		private const int H3_FlagsOffset = 0x0E;
		private const int H3_MipmapCountOffset = 0x14;
		private const int H3_PixelsOffsetField = 0x18;
		private const int H3_PixelsSizeField = 0x1C;
		private const int H3_ResourcesTagBlockOffset = 0x8C;

		// --- Reach / Halo 4 layout constants ---
		private const int Reach_BitmapsTagBlockOffset = 0x7C;
		private const int Reach_BitmapElementSize = 0x2C;
		private const int Reach_WidthOffset = 0x00;
		private const int Reach_HeightOffset = 0x02;
		private const int Reach_DepthOffset = 0x04;
		private const int Reach_FormatFlagsOffset = 0x05;
		private const int Reach_TypeOffset = 0x06; // enum8 (not enum16)
		private const int Reach_FormatOffset = 0x08;
		private const int Reach_FlagsOffset = 0x0A;
		private const int Reach_MipmapCountOffset = 0x10;
		private const int Reach_PixelsOffsetField = 0x14;
		private const int Reach_PixelsSizeField = 0x18;
		private const int Reach_ResourcesTagBlockOffset = 0xA8;

		// Resources/Hardware Textures element size (same for all ThirdGen)
		private const int ResourceElementSize = 0x08;

		private ResourceTable _resourceTable;
		private ResourcePageExtractor _pageExtractor;
		private EngineDescription _buildInfo;
		private readonly Dictionary<string, ResourcePageExtractor> _externalExtractors =
			new Dictionary<string, ResourcePageExtractor>();

		/// <summary>
		///     Diagnostic message from the last failed ReadPixelData call.
		/// </summary>
		public string LastError { get; private set; }

		/// <summary>
		///     Diagnostic message from the last successful ReadPixelData call.
		/// </summary>
		public string LastExtractInfo { get; private set; }

		/// <summary>
		///     Reads all bitmap entries and their resource datums from a bitm tag.
		/// </summary>
		public List<BitmapInfo> ReadBitmapTag(ITag tag, IReader reader, ICacheFile cacheFile,
			EngineDescription buildInfo)
		{
			var results = new List<BitmapInfo>();

			if (tag?.MetaLocation == null || cacheFile?.MetaArea == null)
				return results;

			bool isReachOrLater = buildInfo.Name.Contains("Reach") ||
			                      buildInfo.Name.Contains("Halo 4");

			// Select layout constants
			int bitmapsOffset = isReachOrLater ? Reach_BitmapsTagBlockOffset : H3_BitmapsTagBlockOffset;
			int elementSize = isReachOrLater ? Reach_BitmapElementSize : H3_BitmapElementSize;
			int resourcesOffset = isReachOrLater ? Reach_ResourcesTagBlockOffset : H3_ResourcesTagBlockOffset;

			uint tagOffset = (uint)tag.MetaLocation.AsOffset();

			// Read the Bitmaps/Bitmap Data tagblock reflexive
			reader.SeekTo(tagOffset + bitmapsOffset);
			int bitmapCount = reader.ReadInt32();
			uint bitmapAddress = reader.ReadUInt32();

			if (bitmapCount <= 0 || bitmapCount > 4096)
				return results;

			if (!cacheFile.MetaArea.ContainsPointer(bitmapAddress))
				return results;

			uint bitmapElementsOffset = cacheFile.MetaArea.PointerToOffset(bitmapAddress);

			// Read the Resources/Hardware Textures tagblock reflexive
			reader.SeekTo(tagOffset + resourcesOffset);
			int resourceCount = reader.ReadInt32();
			uint resourceAddress = reader.ReadUInt32();

			uint[] resourceDatums = null;
			if (resourceCount > 0 && resourceCount <= 4096 &&
			    cacheFile.MetaArea.ContainsPointer(resourceAddress))
			{
				uint resourceElementsOffset = cacheFile.MetaArea.PointerToOffset(resourceAddress);
				resourceDatums = new uint[resourceCount];
				for (int i = 0; i < resourceCount; i++)
				{
					reader.SeekTo(resourceElementsOffset + (uint)(i * ResourceElementSize));
					resourceDatums[i] = reader.ReadUInt32();
				}
			}

			// Read each bitmap element
			for (int i = 0; i < bitmapCount; i++)
			{
				uint elemStart = bitmapElementsOffset + (uint)(i * elementSize);
				BitmapInfo info;

				if (isReachOrLater)
					info = ReadReachBitmapElement(reader, elemStart);
				else
					info = ReadH3BitmapElement(reader, elemStart);

				if (info.Width <= 0 || info.Height <= 0)
					continue;

				// Assign resource datum if available
				if (resourceDatums != null && i < resourceDatums.Length)
					info.ResourceDatumValue = resourceDatums[i];

				results.Add(info);
			}

			// Store build info for opening external cache files later
			_buildInfo = buildInfo;

			// Pre-load the resource table for later pixel data extraction
			if (cacheFile.Resources != null)
			{
				try
				{
					_resourceTable = cacheFile.Resources.LoadResourceTable(reader);
					if (_resourceTable != null)
						_pageExtractor = new ResourcePageExtractor(cacheFile);
					else
						LastError = "Resource table loaded but was null.";
				}
				catch (Exception ex)
				{
					_resourceTable = null;
					_pageExtractor = null;
					LastError = $"Failed to load resource table: {ex.Message}";
				}
			}
			else
			{
				LastError = "Cache file has no resource manager.";
			}

			return results;
		}

		private BitmapInfo ReadH3BitmapElement(IReader reader, uint elemStart)
		{
			reader.SeekTo(elemStart + H3_WidthOffset);
			short width = reader.ReadInt16();

			reader.SeekTo(elemStart + H3_HeightOffset);
			short height = reader.ReadInt16();

			reader.SeekTo(elemStart + H3_DepthOffset);
			byte depth = reader.ReadByte();

			reader.SeekTo(elemStart + H3_MoreFlagsOffset);
			byte moreFlags = reader.ReadByte();
			bool isTiled = (moreFlags & (1 << 3)) != 0;

			reader.SeekTo(elemStart + H3_TypeOffset);
			ushort type = reader.ReadUInt16();

			reader.SeekTo(elemStart + H3_FormatOffset);
			ushort format = reader.ReadUInt16();

			reader.SeekTo(elemStart + H3_FlagsOffset);
			ushort flags = reader.ReadUInt16();

			reader.SeekTo(elemStart + H3_MipmapCountOffset);
			byte mipmapCount = reader.ReadByte();

			reader.SeekTo(elemStart + H3_PixelsOffsetField);
			int pixelsOffset = reader.ReadInt32();

			reader.SeekTo(elemStart + H3_PixelsSizeField);
			int pixelsSize = reader.ReadInt32();

			return new BitmapInfo
			{
				Width = width,
				Height = height,
				Depth = depth > 0 ? depth : 1,
				Type = (BitmapType)type,
				Format = (BitmapFormat)format,
				Flags = (BitmapFlags)flags,
				MipmapCount = mipmapCount,
				PixelsOffset = pixelsOffset,
				PixelsSize = pixelsSize,
				IsTiled = isTiled,
			};
		}

		private BitmapInfo ReadReachBitmapElement(IReader reader, uint elemStart)
		{
			reader.SeekTo(elemStart + Reach_WidthOffset);
			short width = reader.ReadInt16();

			reader.SeekTo(elemStart + Reach_HeightOffset);
			short height = reader.ReadInt16();

			reader.SeekTo(elemStart + Reach_DepthOffset);
			byte depth = reader.ReadByte();

			reader.SeekTo(elemStart + Reach_FormatFlagsOffset);
			byte formatFlags = reader.ReadByte();
			bool isTiled = (formatFlags & (1 << 3)) != 0;

			reader.SeekTo(elemStart + Reach_TypeOffset);
			byte type = reader.ReadByte(); // enum8 in Reach/H4

			reader.SeekTo(elemStart + Reach_FormatOffset);
			ushort format = reader.ReadUInt16();

			reader.SeekTo(elemStart + Reach_FlagsOffset);
			ushort flags = reader.ReadUInt16();

			reader.SeekTo(elemStart + Reach_MipmapCountOffset);
			byte mipmapCount = reader.ReadByte();

			reader.SeekTo(elemStart + Reach_PixelsOffsetField);
			int pixelsOffset = reader.ReadInt32();

			reader.SeekTo(elemStart + Reach_PixelsSizeField);
			int pixelsSize = reader.ReadInt32();

			return new BitmapInfo
			{
				Width = width,
				Height = height,
				Depth = depth > 0 ? depth : 1,
				Type = (BitmapType)type,
				Format = (BitmapFormat)format,
				Flags = (BitmapFlags)flags,
				MipmapCount = mipmapCount,
				PixelsOffset = pixelsOffset,
				PixelsSize = pixelsSize,
				IsTiled = isTiled,
			};
		}

		/// <summary>
		///     Reads raw pixel data for a bitmap entry via the resource system.
		/// </summary>
		/// <param name="info">The bitmap info entry (must have ResourceDatumValue set).</param>
		/// <param name="cacheFile">The cache file.</param>
		/// <param name="reader">A reader on the cache file stream.</param>
		/// <param name="mapFilePath">Path to the map file (for resolving external resource files).</param>
		/// <returns>Raw pixel data bytes, or null if extraction fails.</returns>
		public byte[] ReadPixelData(BitmapInfo info, ICacheFile cacheFile, IReader reader,
			string mapFilePath)
		{
			LastError = null;
			LastExtractInfo = null;

			if (_resourceTable == null || _pageExtractor == null)
			{
				LastError ??= "Resource table or page extractor not available.";
				return null;
			}

			var datumIndex = new DatumIndex(info.ResourceDatumValue);
			if (!datumIndex.IsValid)
			{
				LastError = $"Invalid datum index: 0x{info.ResourceDatumValue:X8}";
				return null;
			}

			// Look up resource: try direct index first, then search by datum value
			Resource resource = null;
			if (datumIndex.Index < _resourceTable.Resources.Count)
			{
				resource = _resourceTable.Resources[datumIndex.Index];
				// Verify the datum matches (salt check)
				if (resource != null && resource.Index.Value != 0 &&
				    resource.Index != datumIndex)
				{
					// Direct index didn't match — search by datum value
					resource = _resourceTable.Resources
						.FirstOrDefault(r => r.Index == datumIndex);
				}
			}
			else
			{
				// Index out of range — search by datum value
				resource = _resourceTable.Resources
					.FirstOrDefault(r => r.Index == datumIndex);
			}

			if (resource == null)
			{
				LastError = $"Resource not found. Datum: 0x{info.ResourceDatumValue:X8}, " +
				            $"Index: {datumIndex.Index}, Table size: {_resourceTable.Resources.Count}";
				return null;
			}

			if (resource.Location == null)
			{
				LastError = $"Resource has no location data. Datum: 0x{info.ResourceDatumValue:X8}";
				return null;
			}

			// Try each page in order: primary (main data), secondary, tertiary.
			string[] pageNames = { "Primary", "Secondary", "Tertiary" };
			ResourcePage[] pages = {
				resource.Location.PrimaryPage,
				resource.Location.SecondaryPage,
				resource.Location.TertiaryPage
			};
			int[] offsets = {
				resource.Location.PrimaryOffset,
				resource.Location.SecondaryOffset,
				resource.Location.TertiaryOffset
			};
			ResourceSize[] sizes = {
				resource.Location.PrimarySize,
				resource.Location.SecondarySize,
				resource.Location.TertiarySize
			};

			for (int i = 0; i < 3; i++)
			{
				byte[] data = TryExtractFromPage(
					pages[i], offsets[i], sizes[i],
					info, cacheFile, reader, mapFilePath);

				if (data != null)
				{
					LastExtractInfo = $"Page={pageNames[i]}, " +
					                  $"pixSize={info.PixelsSize}, " +
					                  $"extracted={data.Length}, tiled={info.IsTiled}";
					return data;
				}
			}

			// Build detailed diagnostic for failure
			string pageInfo = BuildPageDiagnostic(resource.Location);
			LastError = $"All resource pages failed extraction. Datum: 0x{info.ResourceDatumValue:X8}. {pageInfo}";
			return null;
		}

		private string BuildPageDiagnostic(ResourcePointer loc)
		{
			var parts = new List<string>();
			if (loc.SecondaryPage != null)
				parts.Add($"Secondary: offset={loc.SecondaryOffset}, size={loc.SecondarySize?.Size ?? -1}, file={loc.SecondaryPage.FilePath ?? "internal"}, uncomp={loc.SecondaryPage.UncompressedSize}");
			else
				parts.Add("Secondary: null");

			if (loc.PrimaryPage != null)
				parts.Add($"Primary: offset={loc.PrimaryOffset}, size={loc.PrimarySize?.Size ?? -1}, file={loc.PrimaryPage.FilePath ?? "internal"}, uncomp={loc.PrimaryPage.UncompressedSize}");
			else
				parts.Add("Primary: null");

			if (loc.TertiaryPage != null)
				parts.Add($"Tertiary: offset={loc.TertiaryOffset}, size={loc.TertiarySize?.Size ?? -1}");
			else
				parts.Add("Tertiary: null");

			return string.Join("; ", parts);
		}

		private byte[] TryExtractFromPage(ResourcePage page, int offset, ResourceSize size,
			BitmapInfo info, ICacheFile cacheFile, IReader reader, string mapFilePath)
		{
			if (page == null)
				return null;

			// If the resource system explicitly declares zero data for this page, skip it.
			if (size != null && size.Size <= 0)
				return null;

			int dataSize = (size != null && size.Size > 0) ? size.Size : 0;
			if (dataSize <= 0)
				dataSize = info.PixelsSize;
			if (dataSize <= 0)
				dataSize = info.CalculatedBaseMipSize;
			if (dataSize <= 0)
				return null;

			try
			{
				byte[] decompressedPage = ExtractPage(page, cacheFile, reader, mapFilePath);

				if (decompressedPage == null || decompressedPage.Length == 0)
					return null;

				// Use the resource offset as the position within the decompressed page
				int inPageOffset = 0;
				if (offset > 0 && offset < decompressedPage.Length)
					inPageOffset = offset;

				int availableData = decompressedPage.Length - inPageOffset;
				if (availableData <= 0)
					return null;

				// For tiled textures, we need at least the aligned tiled size
				// (the untiler pads to 32-block alignment). Use CalculatedBaseMipSize
				// as the baseline — don't extract excessive amounts.
				if (info.IsTiled)
				{
					int baseMipSize = info.CalculatedBaseMipSize;
					if (baseMipSize > 0 && baseMipSize > dataSize)
						dataSize = baseMipSize;
				}

				if (dataSize > availableData)
					dataSize = availableData;

				var result = new byte[dataSize];
				Array.Copy(decompressedPage, inPageOffset, result, 0, dataSize);
				return result;
			}
			catch (Exception ex)
			{
				LastError = $"Page extraction failed: {ex.Message}";
				return null;
			}
		}

		private byte[] ExtractPage(ResourcePage page, ICacheFile cacheFile,
			IReader reader, string mapFilePath)
		{
			if (page.FilePath == null)
			{
				return ExtractPageWithExtractor(_pageExtractor, reader.BaseStream, page);
			}

			string normalizedPath = page.FilePath.Replace('\\', '/');
			string externalPath = ResolveExternalFilePath(normalizedPath, mapFilePath);
			if (externalPath == null || !File.Exists(externalPath))
			{
				string mapDir = Path.GetDirectoryName(mapFilePath) ?? "";
				string expectedFile = Path.GetFileName(normalizedPath);
				LastError = $"External resource file not found: \"{expectedFile}\" " +
				            $"(looked in {mapDir})";
				return null;
			}

			var extractor = GetExternalPageExtractor(externalPath);
			if (extractor == null)
				return null;

			using (var externalStream = File.OpenRead(externalPath))
			{
				return ExtractPageWithExtractor(extractor, externalStream, page);
			}
		}

		private byte[] ExtractPageWithExtractor(ResourcePageExtractor extractor,
			Stream stream, ResourcePage page)
		{
			using (var outStream = new MemoryStream())
			{
				extractor.ExtractDecompressPage(page, stream, outStream);
				return outStream.ToArray();
			}
		}

		/// <summary>
		///     Gets or creates a ResourcePageExtractor for an external cache file.
		///     Opens the external file as a ThirdGenCacheFile to obtain its RawTable offset,
		///     which is needed for correct page extraction. Results are cached per file path.
		/// </summary>
		private ResourcePageExtractor GetExternalPageExtractor(string externalPath)
		{
			if (_externalExtractors.TryGetValue(externalPath, out var cached))
				return cached;

			try
			{
				using (var stream = File.OpenRead(externalPath))
				using (var reader = new EndianReader(stream, _buildInfo.Endian))
				{
					var externalCache = new ThirdGenCacheFile(reader, _buildInfo, externalPath);
					var extractor = new ResourcePageExtractor(externalCache);
					_externalExtractors[externalPath] = extractor;
					return extractor;
				}
			}
			catch (Exception ex)
			{
				LastError = $"Failed to open external cache file " +
				            $"'{Path.GetFileName(externalPath)}': {ex.Message}";
				_externalExtractors[externalPath] = null;
				return null;
			}
		}


		private string ResolveExternalFilePath(string resourceFilePath, string mapFilePath)
		{
			// ResourcePage.FilePath may be a relative path or just a filename.
			// Normalize Windows-style backslashes for cross-platform compatibility.
			resourceFilePath = resourceFilePath.Replace('\\', '/');

			string mapDir = Path.GetDirectoryName(mapFilePath);
			if (mapDir == null)
				return null;

			// Try just the filename relative to the map directory
			string fileName = Path.GetFileName(resourceFilePath);
			string resolved = Path.Combine(mapDir, fileName);
			if (File.Exists(resolved))
				return resolved;

			// Try the relative path as-is (e.g., "maps/shared.map")
			resolved = Path.Combine(mapDir, resourceFilePath);
			if (File.Exists(resolved))
				return resolved;

			// Try walking up one directory level (map might be in a subdirectory)
			string parentDir = Path.GetDirectoryName(mapDir);
			if (parentDir != null)
			{
				resolved = Path.Combine(parentDir, resourceFilePath);
				if (File.Exists(resolved))
					return resolved;

				resolved = Path.Combine(parentDir, fileName);
				if (File.Exists(resolved))
					return resolved;
			}

			return null;
		}
	}
}
