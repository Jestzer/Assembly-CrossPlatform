using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Blamite.Blam;
using Blamite.Blam.Localization;
using Blamite.Serialization;
using Blamite.Serialization.Settings;
using Blamite.IO;

namespace StringDump
{
	internal class Program
	{
		private static void Main(string[] args)
		{
			if (args.Length != 2)
			{
				Console.WriteLine("Usage: StringDump <map file> <output text file>");
				return;
			}

			string mapPath = args[0];
			string dumpPath = args[1];

			if (!File.Exists(mapPath))
			{
				Console.WriteLine("Error: Map file not found: {0}", mapPath);
				return;
			}

			EngineDatabase engineDb = XMLEngineDatabaseLoader.LoadDatabase("Formats/Engines.xml");
			ICacheFile cacheFile;
			LanguagePack locales;
			using (IReader reader = new EndianReader(File.OpenRead(mapPath), Endian.BigEndian))
			{
				Console.WriteLine("Loading cache file...");
				cacheFile = CacheFileLoader.LoadCacheFile(reader, mapPath, engineDb);

				Console.WriteLine("Loading locales...");
				locales = cacheFile.Languages.LoadLanguage(GameLanguage.English, reader);
			}

			var output = new StreamWriter(dumpPath);
			output.WriteLine("Input file: {0}.map", cacheFile.InternalName);
			output.WriteLine();

			// Sort locales by stringID
			var localesById = new List<LocalizedString>();
			localesById.AddRange(locales.StringLists.SelectMany(l => l.Strings).Where(s => s != null && s.Value != null));
			localesById.Sort((x, y) => x.Key.Value.CompareTo(y.Key.Value));

			// Dump locales
			Console.WriteLine("Dumping locales...");
			output.WriteLine("---------------");
			output.WriteLine("English Locales");
			output.WriteLine("---------------");
			foreach (LocalizedString str in localesById)
			{
				if (str != null)
					output.WriteLine("{0} = \"{1}\"", str.Key, str.Value);
			}
			output.WriteLine();

			// Dump stringIDs
			Console.WriteLine("Dumping stringIDs...");
			output.WriteLine("---------");
			output.WriteLine("StringIDs");
			output.WriteLine("---------");
			int index = 0;
			foreach (string str in cacheFile.StringIDs)
			{
				if (str != null)
					output.WriteLine("0x{0:X} = \"{1}\"", index, str);
				else
					output.WriteLine("0x{0:X} = (null)", index);

				index++;
			}
			output.Close();

			Console.WriteLine("Done!");
		}
	}
}
