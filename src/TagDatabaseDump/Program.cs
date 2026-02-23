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
		// Map Variant palette reflexive offsets within the scenario (scnr) tag
		private static readonly (string Name, int Offset)[] SandboxPalettes =
		{
			("Vehicle",    0x1E0),
			("Weapon",     0x1EC),
			("Equipment",  0x1F8),
			("Scenery",    0x204),
			("Teleporter", 0x210),
			("Goal",       0x21C),
			("Spawner",    0x228),
		};

		private const int PaletteEntrySize = 0x1C;

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

			foreach (var (name, paletteOffset) in SandboxPalettes)
			{
				int count = WriteSinglePalette(xml, reader, cacheFile, tagBlockLayout,
					scnrOffset, name, paletteOffset);
				totalEntries += count;
			}

			xml.WriteEndElement(); // </Palettes>

			Console.WriteLine("Palettes: {0} entries across {1} categories",
				totalEntries, SandboxPalettes.Length);
		}

		private static int WriteSinglePalette(XmlWriter xml, IReader reader, ICacheFile cacheFile,
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
				reader.SeekTo(dataOffset + (i * PaletteEntrySize));

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
