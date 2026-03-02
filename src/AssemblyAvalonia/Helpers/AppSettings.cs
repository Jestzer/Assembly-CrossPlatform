using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json;

namespace AssemblyAvalonia.Helpers
{
	public class RecentFileEntry
	{
		public string FileName { get; set; }
		public string FilePath { get; set; }
		public string FileGame { get; set; }
	}

	public class AppSettings
	{
		private static readonly string SettingsDirectory =
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Assembly");
		private static readonly string SettingsPath =
			Path.Combine(SettingsDirectory, "Settings.json");

		// Window state
		public double WindowWidth { get; set; } = 1100;
		public double WindowHeight { get; set; } = 600;
		public bool WindowMaximized { get; set; }

		// Layout
		public double SidebarWidth { get; set; } = 300;

		// Plugin display
		public bool PluginsShowComments { get; set; } = true;
		public bool PluginsShowInvisibles { get; set; }
		public bool PluginsShowDataRefNotice { get; set; } = true;

		// Shared map paths — key: "{internalName}:{sourceIndex}", value: absolute file path
		public Dictionary<string, string> SharedMapPaths { get; set; } = new();

		// Recent files
		public ObservableCollection<RecentFileEntry> RecentFiles { get; set; } = new ObservableCollection<RecentFileEntry>();

		public void SetSharedMapPath(string internalName, int sourceIndex, string path)
		{
			SharedMapPaths ??= new();
			SharedMapPaths[$"{internalName}:{sourceIndex}"] = path;
			Save();
		}

		public void ClearSharedMapPath(string internalName, int sourceIndex)
		{
			SharedMapPaths?.Remove($"{internalName}:{sourceIndex}");
			Save();
		}

		public void ClearAllSharedMapPaths(string internalName)
		{
			if (SharedMapPaths == null || SharedMapPaths.Count == 0)
				return;

			var keysToRemove = new List<string>();
			string prefix = $"{internalName}:";
			foreach (var key in SharedMapPaths.Keys)
			{
				if (key.StartsWith(prefix, StringComparison.Ordinal))
					keysToRemove.Add(key);
			}
			foreach (var key in keysToRemove)
				SharedMapPaths.Remove(key);
			Save();
		}

		public Dictionary<int, string> GetSharedMapOverrides(string internalName)
		{
			var result = new Dictionary<int, string>();
			if (SharedMapPaths == null)
				return result;

			string prefix = $"{internalName}:";
			foreach (var kvp in SharedMapPaths)
			{
				if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal) &&
					int.TryParse(kvp.Key.Substring(prefix.Length), out int sourceIndex))
				{
					result[sourceIndex] = kvp.Value;
				}
			}
			return result;
		}

		public void AddRecentFile(string fileName, string filePath, string fileGame)
		{
			// Remove existing entry with same path
			for (int i = RecentFiles.Count - 1; i >= 0; i--)
			{
				if (string.Equals(RecentFiles[i].FilePath, filePath, StringComparison.OrdinalIgnoreCase))
					RecentFiles.RemoveAt(i);
			}

			// Insert at top
			RecentFiles.Insert(0, new RecentFileEntry
			{
				FileName = fileName,
				FilePath = filePath,
				FileGame = fileGame
			});

			// Keep max 10
			while (RecentFiles.Count > 10)
				RecentFiles.RemoveAt(RecentFiles.Count - 1);

			Save();
		}

		public void Save()
		{
			try
			{
				Directory.CreateDirectory(SettingsDirectory);
				string json = JsonConvert.SerializeObject(this, Formatting.Indented);
				File.WriteAllText(SettingsPath, json);
			}
			catch (Exception)
			{
				// Silently fail — settings are non-critical
			}
		}

		public static AppSettings Load()
		{
			try
			{
				if (File.Exists(SettingsPath))
				{
					string json = File.ReadAllText(SettingsPath);
					return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
				}
			}
			catch (Exception)
			{
				// Corrupted settings — return defaults
			}
			return new AppSettings();
		}
	}
}
