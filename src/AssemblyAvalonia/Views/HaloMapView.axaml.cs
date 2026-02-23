using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using AssemblyAvalonia.Helpers;
using AssemblyAvalonia.Models;
using Blamite.Blam;
using Blamite.IO;
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

		// Build header info
		var headerValues = new List<HeaderValue>
		{
			new HeaderValue("Engine", _buildInfo.Name),
			new HeaderValue("Internal Name", _cacheFile.InternalName),
			new HeaderValue("Scenario", _cacheFile.ScenarioName),
			new HeaderValue("Tags", _cacheFile.Tags.Count.ToString()),
		};

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
			bool groupMatches = group.TagGroupMagic.ToLowerInvariant().Contains(lowerFilter) ||
								(group.Description?.ToLowerInvariant().Contains(lowerFilter) ?? false);

			if (groupMatches)
			{
				filtered.Add(group);
			}
			else
			{
				var matchingChildren = group.Children
					.Where(c => c.TagFileName.ToLowerInvariant().Contains(lowerFilter))
					.ToList();

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

	private void TagTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (TagTree.SelectedItem is TagEntry entry && _cacheFile != null && _buildInfo != null)
		{
			_parentWindow.SetStatus($"Loading tag: [{entry.GroupName}] {entry.TagFileName}...");

			var metaEditor = new MetaEditorView();
			metaEditor.LoadTag(entry, _cacheFile, _buildInfo, _filePath, _hierarchy, _stringIdTrie, _parentWindow);
			MetaContent.Content = metaEditor;

			_parentWindow.SetStatus($"Loaded tag: [{entry.GroupName}] {entry.TagFileName}");
		}
	}

	public void Dispose()
	{
		_reader?.Dispose();
		_fileStream?.Dispose();
	}
}
