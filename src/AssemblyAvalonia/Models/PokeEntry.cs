using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace AssemblyAvalonia.Models
{
	public enum PokeValueType
	{
		Byte,
		Float,
		Double
	}

	public class PokeEntry
	{
		public string Offset { get; set; } = "";
		public string Category { get; set; } = "";
		public string Name { get; set; } = "";
		public string DefaultValue { get; set; } = "0";

		[JsonConverter(typeof(StringEnumConverter))]
		public PokeValueType ValueType { get; set; } = PokeValueType.Byte;

		public string Description { get; set; } = "";

		/// <summary>
		///     The current value to poke. Not persisted — used only at runtime.
		/// </summary>
		[JsonIgnore]
		public string CurrentValue { get; set; } = "";

		/// <summary>
		///     Whether the current value differs from the default.
		/// </summary>
		[JsonIgnore]
		public bool IsChanged => CurrentValue != DefaultValue;

		/// <summary>
		///     Parses the hex offset string to a uint address.
		/// </summary>
		public uint ParseOffset()
		{
			string hex = Offset.Trim();
			if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
				hex = hex.Substring(2);
			return uint.Parse(hex, System.Globalization.NumberStyles.HexNumber);
		}

		/// <summary>
		///     Converts the current value to bytes appropriate for Xbox 360 (big-endian).
		/// </summary>
		public byte[] ValueToBytes()
		{
			switch (ValueType)
			{
				case PokeValueType.Byte:
					return new[] { byte.Parse(CurrentValue) };

				case PokeValueType.Float:
					byte[] floatBytes = BitConverter.GetBytes(float.Parse(CurrentValue));
					if (BitConverter.IsLittleEndian)
						Array.Reverse(floatBytes);
					return floatBytes;

				case PokeValueType.Double:
					byte[] doubleBytes = BitConverter.GetBytes(double.Parse(CurrentValue));
					if (BitConverter.IsLittleEndian)
						Array.Reverse(doubleBytes);
					return doubleBytes;

				default:
					throw new InvalidOperationException($"Unknown value type: {ValueType}");
			}
		}
	}

	public class PokeDatabase
	{
		public string GameName { get; set; } = "";
		public List<PokeEntry> Entries { get; set; } = new();

		public static PokeDatabase LoadFromFile(string path)
		{
			string json = File.ReadAllText(path);
			var db = JsonConvert.DeserializeObject<PokeDatabase>(json)
				?? new PokeDatabase();

			// Initialize CurrentValue from DefaultValue for all entries
			foreach (var entry in db.Entries)
				entry.CurrentValue = entry.DefaultValue;

			return db;
		}

		public void SaveToFile(string path)
		{
			string json = JsonConvert.SerializeObject(this, Formatting.Indented);
			File.WriteAllText(path, json);
		}

		/// <summary>
		///     Imports an Ascension .val file (JSON dictionary keyed by hex offset).
		/// </summary>
		public static PokeDatabase LoadFromAscensionFormat(string path)
		{
			string json = File.ReadAllText(path);
			var raw = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(json)
				?? new Dictionary<string, JObject>();

			string gameName = Path.GetFileNameWithoutExtension(path);
			var db = new PokeDatabase { GameName = gameName };

			foreach (var kvp in raw)
			{
				var obj = kvp.Value;
				var entry = new PokeEntry
				{
					Offset = obj.Value<string>("Offset") ?? kvp.Key,
					Category = obj.Value<string>("Class") ?? "",
					Name = obj.Value<string>("Name") ?? "",
					DefaultValue = obj.Value<string>("DefaultV") ?? "0",
					Description = obj.Value<string>("Description") ?? ""
				};

				string valType = obj.Value<string>("Value_Type") ?? "Byte";
				entry.ValueType = valType switch
				{
					"Float" => PokeValueType.Float,
					"Double" => PokeValueType.Double,
					_ => PokeValueType.Byte
				};

				entry.CurrentValue = entry.DefaultValue;
				db.Entries.Add(entry);
			}

			return db;
		}

		/// <summary>
		///     Returns distinct category names from all entries.
		/// </summary>
		public List<string> GetCategories()
		{
			var categories = new HashSet<string>();
			foreach (var entry in Entries)
			{
				if (!string.IsNullOrEmpty(entry.Category))
					categories.Add(entry.Category);
			}

			var sorted = new List<string>(categories);
			sorted.Sort(StringComparer.OrdinalIgnoreCase);
			return sorted;
		}
	}
}
