using System;
using System.IO;
using System.Xml;
using Blamite.Blam;
using Blamite.IO;
using Blamite.Serialization;
using Blamite.Serialization.Settings;
using Blamite.Util;

namespace TagDatabaseDump
{
	internal class Program
	{
		// Halo 3 / Reach: 7 separate palette reflexives at fixed offsets in scenario tag
		private static readonly (string Name, int Offset)[] H3SandboxPalettes =
		{
			("Vehicle",    0x1E0),
			("Weapon",     0x1EC),
			("Equipment",  0x1F8),
			("Scenery",    0x204),
			("Teleporter", 0x210),
			("Goal",       0x21C),
			("Spawner",    0x228),
		};

		// Halo 4 MCC: single "Map Variant Palettes" reflexive at 0x2C4 containing nested entries
		private const int H4PaletteOffset = 0x2C4;
		private const int H4PaletteCategorySize = 0x14;  // Each category entry
		private const int H4PaletteEntrySize = 0x1C;     // Each item entry within a category

		private const int H3PaletteEntrySize = 0x1C;

		private static void Main(string[] args)
		{
			if (args.Length < 1 || args.Length > 2)
			{
				Console.WriteLine("Usage: TagDatabaseDump <map file> [output xml file]");
				Console.WriteLine();
				Console.WriteLine("Exports all tags from a .map file to ForgeX-compatible XML,");
				Console.WriteLine("including sandbox (forge) palette data from the scenario tag.");
				Console.WriteLine("If no output path is specified, writes <mapname>.xml in the current directory.");
				return;
			}

			string mapPath = args[0];
			if (!File.Exists(mapPath))
			{
				Console.WriteLine("Error: Map file not found: {0}", mapPath);
				return;
			}

			EngineDatabase engineDb = XMLEngineDatabaseLoader.LoadDatabase("Formats/Engines.xml");
			EngineDescription buildInfo;
			ICacheFile cacheFile;

			// Keep reader open — we need it for reading palette reflexives
			IReader reader = new EndianReader(File.OpenRead(mapPath), Endian.BigEndian);
			try
			{
				Console.WriteLine("Loading cache file...");
				cacheFile = CacheFileLoader.LoadCacheFile(reader, mapPath, engineDb, out buildInfo);

				string outputPath = args.Length >= 2
					? args[1]
					: cacheFile.InternalName + ".xml";

				// Count tags with valid groups
				int tagCount = 0;
				foreach (ITag tag in cacheFile.Tags)
				{
					if (tag != null && tag.Group != null && tag.Index.IsValid)
						tagCount++;
				}

				Console.WriteLine("Map: {0} ({1} tags)", cacheFile.InternalName, tagCount);
				Console.WriteLine("Writing {0}...", outputPath);

				var settings = new XmlWriterSettings
				{
					Indent = true,
					IndentChars = "  ",
					OmitXmlDeclaration = true
				};

				using (XmlWriter xml = XmlWriter.Create(outputPath, settings))
				{
					xml.WriteStartElement("Map");
					xml.WriteAttributeString("Map", cacheFile.InternalName);
					xml.WriteAttributeString("TagCount", tagCount.ToString());

					foreach (ITag tag in cacheFile.Tags)
					{
						if (tag == null || tag.Group == null || !tag.Index.IsValid)
							continue;

						string tagClass = CharConstant.ToString(tag.Group.Magic);
						string tagName = cacheFile.FileNames.GetTagName(tag);
						string tagPath = string.IsNullOrEmpty(tagName) ? "UNK" : tagName;

						xml.WriteStartElement("Tag");
						xml.WriteAttributeString("Class", tagClass);
						xml.WriteAttributeString("Path", tagPath);
						xml.WriteAttributeString("Ident", ((int)tag.Index.Value).ToString());
						xml.WriteEndElement();
					}

					// Write sandbox palette data from the scenario tag
					WritePalettes(xml, reader, cacheFile, buildInfo);

					xml.WriteEndElement(); // </Map>
				}

				Console.WriteLine("Done! Wrote {0} tags to {1}", tagCount, outputPath);
			}
			finally
			{
				reader.Dispose();
			}
		}

		private static void WritePalettes(XmlWriter xml, IReader reader, ICacheFile cacheFile,
			EngineDescription buildInfo)
		{
			// Find the scenario (scnr) tag
			int scnrMagic = CharConstant.FromString("scnr");
			ITag scnrTag = null;
			foreach (ITag tag in cacheFile.Tags)
			{
				if (tag != null && tag.Group != null && tag.Group.Magic == scnrMagic &&
					tag.MetaLocation != null)
				{
					scnrTag = tag;
					break;
				}
			}

			if (scnrTag == null)
			{
				Console.WriteLine("No scenario tag found; skipping palette export.");
				return;
			}

			// We need the tag block layout to read reflexive headers
			StructureLayout tagBlockLayout = buildInfo.Layouts.GetLayout("tag block");
			if (tagBlockLayout == null)
			{
				Console.WriteLine("No tag block layout found; skipping palette export.");
				return;
			}

			long scnrOffset = scnrTag.MetaLocation.AsOffset();
			int totalEntries = 0;

			xml.WriteStartElement("Palettes");

			// Try H4 MCC format first (nested "Map Variant Palettes" at 0x2C4)
			int h4Count = TryWriteH4Palettes(xml, reader, cacheFile, tagBlockLayout, scnrOffset);

			if (h4Count > 0)
			{
				totalEntries = h4Count;
				Console.WriteLine("Palettes (H4 format): {0} entries", totalEntries);
			}
			else
			{
				// Fall back to H3/Reach format: 7 separate reflexives
				foreach (var (name, paletteOffset) in H3SandboxPalettes)
				{
					int count = WriteH3SinglePalette(xml, reader, cacheFile, tagBlockLayout,
						scnrOffset, name, paletteOffset);
					totalEntries += count;
				}
				Console.WriteLine("Palettes (H3 format): {0} entries across {1} categories",
					totalEntries, H3SandboxPalettes.Length);
			}

			xml.WriteEndElement(); // </Palettes>
		}

		/// <summary>
		/// H4 MCC: reads the "Map Variant Palettes" two-level reflexive at offset 0x2C4.
		/// Level 1: palette categories (0x14 bytes each) with StringID name + nested Entries block.
		/// Level 2: entries (0x1C bytes each) with StringID name + MaxAllowed + Price.
		/// </summary>
		private static int TryWriteH4Palettes(XmlWriter xml, IReader reader, ICacheFile cacheFile,
			StructureLayout tagBlockLayout, long scnrOffset)
		{
			// Read the top-level "Map Variant Palettes" tag block at 0x2C4
			reader.SeekTo(scnrOffset + H4PaletteOffset);
			StructureValueCollection outerBlock = StructureReader.ReadStructure(reader, tagBlockLayout);

			int categoryCount = (int)outerBlock.GetInteger("entry count");
			uint categoryPointer = (uint)outerBlock.GetInteger("pointer");

			if (categoryCount <= 0 || categoryCount > 20)
				return 0;

			long expandedCategoryPtr = cacheFile.PointerExpander.Expand(categoryPointer);
			if (!cacheFile.MetaArea.ContainsPointer(expandedCategoryPtr))
				return 0;

			uint categoryDataOffset = cacheFile.MetaArea.PointerToOffset(expandedCategoryPtr);
			int totalEntries = 0;

			for (int cat = 0; cat < categoryCount; cat++)
			{
				long catOffset = categoryDataOffset + (cat * H4PaletteCategorySize);

				// Read category StringID at +0x0
				reader.SeekTo(catOffset);
				uint categoryStringId = reader.ReadUInt32();
				string categoryName = "";
				if (categoryStringId != 0 && cacheFile.StringIDs != null)
					categoryName = cacheFile.StringIDs.GetString(new StringID(categoryStringId)) ?? "";

				if (string.IsNullOrEmpty(categoryName))
					categoryName = $"Category_{cat}";

				// Read nested "Entries" tag block at +0x8 within the category
				reader.SeekTo(catOffset + 0x8);
				StructureValueCollection entriesBlock = StructureReader.ReadStructure(reader, tagBlockLayout);

				int entryCount = (int)entriesBlock.GetInteger("entry count");
				uint entryPointer = (uint)entriesBlock.GetInteger("pointer");

				xml.WriteStartElement("Palette");
				xml.WriteAttributeString("Category", categoryName);

				if (entryCount > 0 && entryCount < 10000)
				{
					long expandedEntryPtr = cacheFile.PointerExpander.Expand(entryPointer);
					if (cacheFile.MetaArea.ContainsPointer(expandedEntryPtr))
					{
						uint entryDataOffset = cacheFile.MetaArea.PointerToOffset(expandedEntryPtr);

						for (int i = 0; i < entryCount; i++)
						{
							long entryOffset = entryDataOffset + (i * H4PaletteEntrySize);

							// StringID Name at +0x0
							reader.SeekTo(entryOffset);
							uint nameStringId = reader.ReadUInt32();

							// Variants tag block at +0x4
							reader.SeekTo(entryOffset + 0x4);
							StructureValueCollection variantsBlock = StructureReader.ReadStructure(reader, tagBlockLayout);
							int variantCount = (int)variantsBlock.GetInteger("entry count");
							uint variantPointer = (uint)variantsBlock.GetInteger("pointer");

							// MaxAllowed at +0x10
							reader.SeekTo(entryOffset + 0x10);
							int maxAllowed = reader.ReadInt32();

							// Price at +0x14
							int price = reader.ReadInt32();

							string displayName = "";
							if (nameStringId != 0 && cacheFile.StringIDs != null)
								displayName = cacheFile.StringIDs.GetString(new StringID(nameStringId)) ?? "";

							xml.WriteStartElement("Entry");
							xml.WriteAttributeString("Name", displayName);
							xml.WriteAttributeString("MaxAllowed", maxAllowed.ToString());
							xml.WriteAttributeString("Cost", price.ToString());

							// Read variants if present
							if (variantCount > 0 && variantCount < 1000)
							{
								long expandedVarPtr = cacheFile.PointerExpander.Expand(variantPointer);
								if (cacheFile.MetaArea.ContainsPointer(expandedVarPtr))
								{
									uint varDataOffset = cacheFile.MetaArea.PointerToOffset(expandedVarPtr);
									for (int v = 0; v < variantCount; v++)
									{
										// Variant entry is 0x48 bytes
										// Display Name StringID at +0x0
										reader.SeekTo(varDataOffset + (v * 0x48));
										uint varNameId = reader.ReadUInt32();

										string varName = "";
										if (varNameId != 0 && cacheFile.StringIDs != null)
											varName = cacheFile.StringIDs.GetString(new StringID(varNameId)) ?? "";

										xml.WriteStartElement("Variant");
										xml.WriteAttributeString("Name", varName);
										xml.WriteEndElement();
									}
								}
							}

							xml.WriteEndElement(); // </Entry>
							totalEntries++;
						}
					}
				}

				xml.WriteEndElement(); // </Palette>
			}

			return totalEntries;
		}

		/// <summary>
		/// H3/Reach format: reads a single palette reflexive at a fixed offset in the scenario tag.
		/// Each entry is 0x1C bytes with tag reference + StringID + MaxAllowed + Cost.
		/// </summary>
		private static int WriteH3SinglePalette(XmlWriter xml, IReader reader, ICacheFile cacheFile,
			StructureLayout tagBlockLayout, long scnrOffset, string categoryName, int paletteOffset)
		{
			// Read the tag block header at the palette's offset in the scenario tag
			reader.SeekTo(scnrOffset + paletteOffset);
			StructureValueCollection blockHeader = StructureReader.ReadStructure(reader, tagBlockLayout);

			int entryCount = (int)blockHeader.GetInteger("entry count");
			uint pointer = (uint)blockHeader.GetInteger("pointer");

			if (entryCount <= 0)
			{
				xml.WriteStartElement("Palette");
				xml.WriteAttributeString("Category", categoryName);
				xml.WriteEndElement();
				return 0;
			}

			// Expand the pointer (ThirdGen uses pointer compression)
			long expandedPtr = cacheFile.PointerExpander.Expand(pointer);

			if (!cacheFile.MetaArea.ContainsPointer(expandedPtr))
			{
				xml.WriteStartElement("Palette");
				xml.WriteAttributeString("Category", categoryName);
				xml.WriteEndElement();
				return 0;
			}

			uint dataOffset = cacheFile.MetaArea.PointerToOffset(expandedPtr);

			xml.WriteStartElement("Palette");
			xml.WriteAttributeString("Category", categoryName);

			int written = 0;
			for (int i = 0; i < entryCount; i++)
			{
				reader.SeekTo(dataOffset + (i * H3PaletteEntrySize));

				// Tag reference: group magic (int32) at +0x0, datum index (uint32) at +0xC
				reader.ReadInt32();  // group magic (skip)
				reader.ReadInt32();  // padding
				reader.ReadInt32();  // padding
				uint datumIndex = reader.ReadUInt32(); // +0xC

				// StringID display name at +0x10
				uint stringIdVal = reader.ReadUInt32();

				// Maximum allowed at +0x14
				int maxAllowed = reader.ReadInt32();

				// Price per instance at +0x18
				float cost = reader.ReadFloat();

				// Skip null tag references
				if (datumIndex == 0xFFFFFFFF)
					continue;

				string displayName = "";
				if (stringIdVal != 0 && cacheFile.StringIDs != null)
				{
					displayName = cacheFile.StringIDs.GetString(new StringID(stringIdVal)) ?? "";
				}

				xml.WriteStartElement("Entry");
				xml.WriteAttributeString("Ident", ((int)datumIndex).ToString());
				xml.WriteAttributeString("Name", displayName);
				xml.WriteAttributeString("MaxAllowed", maxAllowed.ToString());
				xml.WriteAttributeString("Cost", cost.ToString("G"));
				xml.WriteEndElement();
				written++;
			}

			xml.WriteEndElement(); // </Palette>
			return written;
		}
	}
}
