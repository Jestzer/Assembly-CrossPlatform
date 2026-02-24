using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AssemblyAvalonia.Helpers;
using AssemblyAvalonia.Models;
using Blamite.Blam;
using Blamite.IO;
using Blamite.RTE;
using Blamite.RTE.PC;
using Blamite.Serialization;
using Blamite.Serialization.Settings;
using Blamite.Util;

namespace AssemblyAvalonia.Views;

public partial class HaloMapView : UserControl, IDisposable
{
	private ICacheFile _cacheFile;
	private EngineDescription _buildInfo;
	private string _filePath;
	private Trie _stringIdTrie;
	private TagHierarchy _hierarchy;
	private MainWindow _parentWindow;
	private List<TagGroup> _allGroups;
	private EndianReader _reader;
	private Stream _fileStream;
	private RTEProvider _rteProvider;

	// Cached lowercase names for fast search
	private Dictionary<TagGroup, string> _lowerGroupMagic;
	private Dictionary<TagGroup, string> _lowerGroupDesc;
	private Dictionary<TagEntry, string> _lowerTagNames;

	public HaloMapView()
	{
		InitializeComponent();
	}

	public void LoadMap(string filePath, MainWindow parent)
	{
		_filePath = filePath;
		_parentWindow = parent;

		Task.Run(() =>
		{
			try
			{
				LoadMapInternal();
			}
			catch (Exception ex)
			{
				Dispatcher.UIThread.Post(() =>
					_parentWindow.SetStatus($"Error loading map: {ex.Message}"));
			}
		});
	}

	private void LoadMapInternal()
	{
		// Load engine database if not loaded
		if (AppState.EngineDb == null)
		{
			string formatsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Formats");
			string dbPath = Path.Combine(formatsDir, "Engines.xml");
			if (!File.Exists(dbPath))
			{
				Dispatcher.UIThread.Post(() =>
					_parentWindow.SetStatus("Error: Engines.xml not found in Formats directory."));
				return;
			}

			// Blamite resolves layout/database paths relative to CWD
			// (paths in Engines.xml already include "Formats/" prefix).
			string oldCwd = Environment.CurrentDirectory;
			Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;
			try
			{
				AppState.EngineDb = XMLEngineDatabaseLoader.LoadDatabase(dbPath);
			}
			finally
			{
				Environment.CurrentDirectory = oldCwd;
			}
		}

		// Open the file
		_fileStream = File.OpenRead(_filePath);
		_reader = new EndianReader(_fileStream, Endian.BigEndian);

		// Load cache file (endianness is corrected inside based on engine)
		_cacheFile = CacheFileLoader.LoadCacheFile(_reader, _filePath, AppState.EngineDb, out _buildInfo);

		// Create RTE provider for poking if supported
		if (_buildInfo.PokingPlatform == RTEConnectionType.LocalProcess32 ||
			_buildInfo.PokingPlatform == RTEConnectionType.LocalProcess64)
		{
			switch (_cacheFile.Engine)
			{
				case EngineType.FirstGeneration:
					_rteProvider = new PCFirstGenRTEProvider(_buildInfo);
					break;
				case EngineType.SecondGeneration:
					_rteProvider = new PCSecondGenRTEProvider(_buildInfo);
					break;
				case EngineType.ThirdGeneration:
					_rteProvider = new PCThirdGenRTEProvider(_buildInfo);
					break;
			}
		}

		// Build string ID trie
		_stringIdTrie = new Trie();
		if (_cacheFile.StringIDs != null)
		{
			foreach (var str in _cacheFile.StringIDs)
				_stringIdTrie.Add(str);
		}

		// Build tag hierarchy
		_hierarchy = new TagHierarchy();
		_hierarchy.Entries = new List<TagEntry>();

		var groups = new Dictionary<ITagGroup, TagGroup>();
		foreach (var tag in _cacheFile.Tags)
		{
			if (tag == null || tag.Group == null)
				continue;

			if (!groups.TryGetValue(tag.Group, out var group))
			{
				string magic = CharConstant.ToString(tag.Group.Magic);

				// Resolve description: StringID first, then GroupNames, then magic
				string desc = null;
				if (tag.Group.Description.Value != 0 && _cacheFile.StringIDs != null)
					desc = _cacheFile.StringIDs.GetString(tag.Group.Description);
				if (desc == null && _buildInfo.GroupNames != null)
					desc = _buildInfo.GroupNames.RetrieveName(magic);
				desc ??= magic;

				group = new TagGroup(tag.Group, magic, desc);
				groups[tag.Group] = group;
			}

			string tagName = _cacheFile.FileNames.GetTagName(tag) ?? $"0x{tag.Index.Value:X}";
			var entry = new TagEntry(tag, group.TagGroupMagic, tagName);
			group.Children.Add(entry);
			_hierarchy.Entries.Add(entry);
		}

		var sortedGroups = groups.Values.OrderBy(g => g.TagGroupMagic).ToList();
		foreach (var g in sortedGroups)
			g.Children = g.Children.OrderBy(e => e.TagFileName).ToList();

		_allGroups = sortedGroups;
		foreach (var g in sortedGroups)
			_hierarchy.Groups.Add(g);

		// Build lowercase name caches for fast search
		_lowerGroupMagic = new Dictionary<TagGroup, string>(sortedGroups.Count);
		_lowerGroupDesc = new Dictionary<TagGroup, string>(sortedGroups.Count);
		_lowerTagNames = new Dictionary<TagEntry, string>();
		foreach (var g in sortedGroups)
		{
			_lowerGroupMagic[g] = g.TagGroupMagic.ToLowerInvariant();
			_lowerGroupDesc[g] = g.Description?.ToLowerInvariant() ?? "";
			foreach (var child in g.Children)
				_lowerTagNames[child] = child.TagFileName.ToLowerInvariant();
		}

		// Build header info
		string mapName = MapNameLookup.GetMapName(_cacheFile.InternalName, _buildInfo.Name);
		var headerValues = new List<HeaderValue>
		{
			new HeaderValue("Engine", _buildInfo.Name),
			new HeaderValue("Internal Name", _cacheFile.InternalName),
		};
		if (mapName != null)
			headerValues.Add(new HeaderValue("Map Name", mapName));
		headerValues.Add(new HeaderValue("Scenario", _cacheFile.ScenarioName));
		headerValues.Add(new HeaderValue("Tags", _cacheFile.Tags.Count.ToString()));

		if (_cacheFile.StringIDs != null)
			headerValues.Add(new HeaderValue("String IDs", _cacheFile.StringIDs.Count.ToString()));

		// Update UI on dispatcher thread
		Dispatcher.UIThread.Post(() =>
		{
			HeaderList.ItemsSource = headerValues;
			TagTree.ItemsSource = _allGroups;

			string fileName = Path.GetFileName(_filePath);
			string game = _buildInfo.Name;
			_parentWindow.SetStatus($"Loaded {fileName} ({game}) - {_cacheFile.Tags.Count} tags");
			_parentWindow.Title = $"Assembly - {fileName}";

			// Add to recent files
			AppState.Settings.AddRecentFile(fileName, _filePath, game);
			_parentWindow.BuildRecentFilesMenu();
		});
	}

	private void TagSearchBox_TextChanged(object? sender, TextChangedEventArgs e)
	{
		string filter = TagSearchBox.Text?.Trim() ?? "";
		if (_allGroups == null)
			return;

		if (string.IsNullOrEmpty(filter))
		{
			TagTree.ItemsSource = _allGroups;
			return;
		}

		string lowerFilter = filter.ToLowerInvariant();
		var filtered = new List<TagGroup>();

		foreach (var group in _allGroups)
		{
			bool groupMatches = _lowerGroupMagic[group].Contains(lowerFilter) ||
								_lowerGroupDesc[group].Contains(lowerFilter);

			if (groupMatches)
			{
				filtered.Add(group);
			}
			else
			{
				var matchingChildren = new List<TagEntry>();
				foreach (var child in group.Children)
				{
					if (_lowerTagNames[child].Contains(lowerFilter))
						matchingChildren.Add(child);
				}

				if (matchingChildren.Count > 0)
				{
					var filteredGroup = new TagGroup(group.RawGroup, group.TagGroupMagic, group.Description);
					filteredGroup.Children = matchingChildren;
					filtered.Add(filteredGroup);
				}
			}
		}

		TagTree.ItemsSource = filtered;
	}

	private TagEntry _selectedTag;

	private void TagTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (TagTree.SelectedItem is TagEntry entry && _cacheFile != null && _buildInfo != null)
		{
			_selectedTag = entry;
			_parentWindow.SetStatus($"Loading tag: [{entry.GroupName}] {entry.TagFileName}...");

			// Show swap panel with current datum index and populate group dropdown
			SwapPanel.IsVisible = true;
			CurrentDatumText.Text = entry.DatumIndexString;
			SwapGroupCombo.ItemsSource = _allGroups;
			SwapGroupCombo.SelectedIndex = -1;
			SwapTagCombo.ItemsSource = null;

			var metaEditor = new MetaEditorView();
			metaEditor.OnLoadComplete = (success, error) =>
			{
				if (success)
					_parentWindow.SetStatus($"Loaded tag: [{entry.GroupName}] {entry.TagFileName}");
			};
			metaEditor.LoadTag(entry, _cacheFile, _buildInfo, _filePath, _hierarchy, _stringIdTrie, _parentWindow, _rteProvider);
			MetaContent.Content = metaEditor;
		}
	}

	private void SwapGroupCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (SwapGroupCombo.SelectedItem is TagGroup group)
			SwapTagCombo.ItemsSource = group.Children;
		else
			SwapTagCombo.ItemsSource = null;
	}

	private async void SwapTags_Click(object? sender, RoutedEventArgs e)
	{
		if (_selectedTag?.RawTag == null || _cacheFile == null)
			return;

		if (SwapTagCombo.SelectedItem is not TagEntry targetEntry || targetEntry.RawTag == null)
		{
			string msg = "No target tag selected. Choose a group and tag to swap with.";
			_parentWindow.SetStatus(msg);
			await ErrorDialog.Show(_parentWindow, msg);
			return;
		}

		ITag sourceTag = _selectedTag.RawTag;
		ITag targetTag = targetEntry.RawTag;

		if (sourceTag.Index.Value == targetTag.Index.Value)
		{
			string msg = "Cannot swap a tag with itself.";
			_parentWindow.SetStatus(msg);
			await ErrorDialog.Show(_parentWindow, msg);
			return;
		}

		// Swap MetaLocation pointers
		var tempMeta = sourceTag.MetaLocation;
		sourceTag.MetaLocation = targetTag.MetaLocation;
		targetTag.MetaLocation = tempMeta;

		// Swap groups if different
		if (sourceTag.Group != targetTag.Group)
		{
			var tempGroup = sourceTag.Group;
			sourceTag.Group = targetTag.Group;
			targetTag.Group = tempGroup;
		}

		// Save changes to the map file
		try
		{
			// Close the read stream so we can open for writing
			var endianness = _cacheFile.Endianness;
			_reader?.Dispose();
			_fileStream?.Dispose();

			using (var stream = new EndianStream(File.Open(_filePath, FileMode.Open, FileAccess.ReadWrite), endianness))
			{
				_cacheFile.SaveChanges(stream);
			}

			_parentWindow.SetStatus($"Swapped [{_selectedTag.TagFileName}] with [{targetEntry.TagFileName}]. Reload the map to see updated metadata.");
		}
		catch (Exception ex)
		{
			// Undo the swap on failure
			var tempMeta2 = sourceTag.MetaLocation;
			sourceTag.MetaLocation = targetTag.MetaLocation;
			targetTag.MetaLocation = tempMeta2;
			if (sourceTag.Group != targetTag.Group)
			{
				var tempGroup2 = sourceTag.Group;
				sourceTag.Group = targetTag.Group;
				targetTag.Group = tempGroup2;
			}
			string msg = $"Swap failed: {ex.Message}";
			_parentWindow.SetStatus(msg);
			await ErrorDialog.Show(_parentWindow, msg);
		}
		finally
		{
			// Reopen the file for continued reading
			_fileStream = File.OpenRead(_filePath);
			_reader = new EndianReader(_fileStream, _cacheFile.Endianness);
		}
	}

	public void Dispose()
	{
		_reader?.Dispose();
		_fileStream?.Dispose();
	}
}
