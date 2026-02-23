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
		private static void Main(string[] args)
		{
			if (args.Length < 1 || args.Length > 2)
			{
				Console.WriteLine("Usage: TagDatabaseDump <map file> [output xml file]");
				Console.WriteLine();
				Console.WriteLine("Exports all tags from a .map file to ForgeX-compatible XML.");
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
			ICacheFile cacheFile;
			using (IReader reader = new EndianReader(File.OpenRead(mapPath), Endian.BigEndian))
			{
				Console.WriteLine("Loading cache file...");
				cacheFile = CacheFileLoader.LoadCacheFile(reader, mapPath, engineDb);
			}

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

				xml.WriteEndElement();
			}

			Console.WriteLine("Done! Wrote {0} tags to {1}", tagCount, outputPath);
		}
	}
}
