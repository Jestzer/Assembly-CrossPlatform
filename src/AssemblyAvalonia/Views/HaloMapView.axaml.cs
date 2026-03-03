using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AssemblyAvalonia.Helpers;
using AssemblyAvalonia.Models;
using Blamite.Blam;
using Blamite.IO;
using Blamite.RTE;
using Blamite.RTE.Console;
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
	private RTEConnectionType? _consolePlatform;
	private Dictionary<int, string> _sharedMapOverrides = new();
	private List<HeaderValue> _headerValues;
	private volatile bool _disposed;

	// Cached lowercase names for fast search
	private Dictionary<TagGroup, string> _lowerGroupMagic;
	private Dictionary<TagGroup, string> _lowerGroupDesc;
	private Dictionary<TagEntry, string> _lowerTagNames;

	public HaloMapView()
	{
		InitializeComponent();

		// Restore sidebar width from settings
		double savedWidth = AppState.Settings.SidebarWidth;
		if (savedWidth >= 200)
			MainGrid.ColumnDefinitions[0].Width = new GridLength(savedWidth);
	}

	/// <summary>
	///     Called when map loading fails. Set by the caller to handle cleanup.
	/// </summary>
	public Action<string> OnLoadFailed { get; set; }

	public void LoadMap(string filePath, MainWindow parent)
	{
		_filePath = filePath;
		_parentWindow = parent;

		// Show loading message in the content area
		string fileName = Path.GetFileName(filePath);
		this.TryFindResource("TextSecondaryBrush", out var secondaryBrush);
		var textBrush = secondaryBrush as Avalonia.Media.IBrush ?? Avalonia.Media.Brushes.Gray;
		MetaContent.Content = new TextBlock
		{
			Text = $"Loading {fileName}...",
			Foreground = textBrush,
			HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
			VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
			FontSize = 16,
		};

		Task.Run(() =>
		{
			try
			{
				LoadMapInternal();
			}
			catch (Exception ex)
			{
				Dispatcher.UIThread.Post(() =>
				{
					string msg = $"Error loading map: {ex.Message}";
					_parentWindow.SetStatus(msg);
					OnLoadFailed?.Invoke(msg);
				});
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
		var fileStream = File.OpenRead(_filePath);
		var reader = new EndianReader(fileStream, Endian.BigEndian);

		if (_disposed)
		{
			reader.Dispose();
			fileStream.Dispose();
			return;
		}

		_fileStream = fileStream;
		_reader = reader;

		// Load cache file (endianness is corrected inside based on engine)
		_cacheFile = CacheFileLoader.LoadCacheFile(_reader, _filePath, AppState.EngineDb, out _buildInfo);

		if (_disposed)
			return;

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
		else if (_buildInfo.PokingPlatform == RTEConnectionType.ConsoleXbox ||
		         _buildInfo.PokingPlatform == RTEConnectionType.ConsoleXbox360)
		{
			_consolePlatform = _buildInfo.PokingPlatform;
			bool isXbox = _buildInfo.PokingPlatform == RTEConnectionType.ConsoleXbox;
			Dispatcher.UIThread.Post(() =>
			{
				if (isXbox)
					_parentWindow.RegisterXboxHandler(HandleConsoleConnect);
				else
					_parentWindow.RegisterXbox360Handler(HandleConsoleConnect);
			});
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

		// Load saved shared map overrides for this map
		var sharedOverrides = AppState.Settings.GetSharedMapOverrides(_cacheFile.InternalName);
		bool isSecondGen = _cacheFile.Engine == EngineType.SecondGeneration;
		foreach (var kvp in sharedOverrides)
		{
			string srcName = Blamite.Blam.Textures.SecondGenBitmapTagReader.GetSourceDescription(kvp.Key << 30);
			headerValues.Add(new HeaderValue("Shared Map", $"{srcName} \u2192 {Path.GetFileName(kvp.Value)}"));
		}

		if (_disposed)
			return;

		// Update UI on dispatcher thread
		Dispatcher.UIThread.Post(() =>
		{
			if (_disposed)
				return;

			_sharedMapOverrides = sharedOverrides;
			_headerValues = headerValues;
			HeaderList.ItemsSource = headerValues;

			// Show shared maps panel for 2nd gen engines
			if (isSecondGen)
			{
				SharedMapsPanel.IsVisible = true;
				RefreshSharedMapLabel();
			}
			TagTree.ItemsSource = _allGroups;

			// Replace loading message with the default prompt
			this.TryFindResource("TextSecondaryBrush", out var brush);
			var promptBrush = brush as Avalonia.Media.IBrush ?? Avalonia.Media.Brushes.Gray;
			MetaContent.Content = new TextBlock
			{
				Text = "Select a tag to view its metadata.",
				Foreground = promptBrush,
				HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
				VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
				FontSize = 16,
			};

			string fileName = Path.GetFileName(_filePath);
			string game = _buildInfo.Name;
			_parentWindow.SetStatus($"Loaded {fileName} ({game}) - {_cacheFile.Tags.Count} tags");
			_parentWindow.Title = $"Assembly Crossplatform - {fileName}";

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

			// Show bitmap preview for bitm tags, meta editor for everything else
			string groupMagic = CharConstant.ToString(entry.RawTag.Group.Magic);
			if (groupMagic == "bitm" && entry.RawTag.MetaLocation != null)
			{
				// ThirdGen bitmap preview is experimental — default to meta view
				if (_cacheFile.Engine == EngineType.ThirdGeneration)
					ShowMetaEditorForTag(entry, true);
				else
					ShowBitmapPreviewForTag(entry);
			}
			else
				ShowMetaEditorForTag(entry, false);
		}
	}

	private void ShowBitmapPreviewForTag(TagEntry entry)
	{
		var preview = new BitmapPreviewView();
		preview.ShowMetaEditor = () => ShowMetaEditorForTag(entry, true);
		preview.InitialSharedMapOverrides = _sharedMapOverrides;
		preview.OnSharedMapSelected = (sourceIndex, chosenPath) =>
		{
			AppState.Settings.SetSharedMapPath(_cacheFile.InternalName, sourceIndex, chosenPath);
			_sharedMapOverrides[sourceIndex] = chosenPath;
			RefreshSharedMapLabel();
			RefreshSharedMapHeaders();
		};
		preview.LoadBitmap(entry, _cacheFile, _buildInfo, _filePath, _hierarchy, _stringIdTrie, _parentWindow, _rteProvider);
		MetaContent.Content = preview;
		_parentWindow.SetStatus($"Loaded bitmap: [{entry.GroupName}] {entry.TagFileName}");
	}

	private void ShowMetaEditorForTag(TagEntry entry, bool isBitm)
	{
		var editor = new MetaEditorView();
		editor.OnLoadComplete = (success, error) =>
		{
			if (success)
				_parentWindow.SetStatus($"Loaded tag: [{entry.GroupName}] {entry.TagFileName}");
		};
		if (isBitm)
		{
			editor.ShowBitmapPreview = () => ShowBitmapPreviewForTag(entry);
			if (_cacheFile.Engine == EngineType.ThirdGeneration)
				editor.ShowPreviewLabel = "Show Preview (Experimental)";
		}
		editor.LoadTag(entry, _cacheFile, _buildInfo, _filePath, _hierarchy, _stringIdTrie, _parentWindow, _rteProvider);
		MetaContent.Content = editor;
	}

	private void RefreshSharedMapLabel()
	{
		if (_sharedMapOverrides.Count == 0)
		{
			SharedMapLabel.Text = "No shared map configured.";
			SharedMapClearBtn.IsVisible = false;
		}
		else
		{
			var parts = new List<string>();
			foreach (var kvp in _sharedMapOverrides)
			{
				string srcName = Blamite.Blam.Textures.SecondGenBitmapTagReader.GetSourceDescription(kvp.Key << 30);
				parts.Add($"{srcName}: {Path.GetFileName(kvp.Value)}");
			}
			SharedMapLabel.Text = string.Join("\n", parts);
			SharedMapClearBtn.IsVisible = true;
		}
	}

	private void RefreshSharedMapHeaders()
	{
		if (_headerValues == null || _cacheFile == null)
			return;

		// Remove existing shared map entries
		_headerValues.RemoveAll(h => h.Title == "Shared Map");

		// Add current overrides
		foreach (var kvp in _sharedMapOverrides)
		{
			string srcName = Blamite.Blam.Textures.SecondGenBitmapTagReader.GetSourceDescription(kvp.Key << 30);
			_headerValues.Add(new HeaderValue("Shared Map", $"{srcName} \u2192 {Path.GetFileName(kvp.Value)}"));
		}

		// Avalonia ItemsControl needs a new list reference to trigger a refresh
		HeaderList.ItemsSource = new List<HeaderValue>(_headerValues);
	}

	private async void SharedMapBrowse_Click(object? sender, RoutedEventArgs e)
	{
		if (_cacheFile == null)
			return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null)
			return;

		try
		{
			var dialog = await topLevel.StorageProvider.OpenFilePickerAsync(
				new Avalonia.Platform.Storage.FilePickerOpenOptions
				{
					Title = "Locate shared map file",
					AllowMultiple = false,
					FileTypeFilter = new[]
					{
						new Avalonia.Platform.Storage.FilePickerFileType("Halo Map Files")
						{
							Patterns = new[] { "*.map" }
						}
					}
				});

			if (dialog == null || dialog.Count == 0)
				return;

			string chosenPath = dialog[0].TryGetLocalPath();
			if (string.IsNullOrEmpty(chosenPath))
				return;

			// Default to source 2 (shared.map) — the most common shared resource
			int sourceIndex = 2;
			AppState.Settings.SetSharedMapPath(_cacheFile.InternalName, sourceIndex, chosenPath);
			_sharedMapOverrides[sourceIndex] = chosenPath;

			RefreshSharedMapLabel();
			RefreshSharedMapHeaders();
			_parentWindow?.SetStatus($"Shared map set: {Path.GetFileName(chosenPath)}");
		}
		catch (Exception ex)
		{
			_parentWindow?.SetStatus($"Browse failed: {ex.Message}");
		}
	}

	private void SharedMapClear_Click(object? sender, RoutedEventArgs e)
	{
		if (_cacheFile == null)
			return;

		AppState.Settings.ClearAllSharedMapPaths(_cacheFile.InternalName);
		_sharedMapOverrides.Clear();

		RefreshSharedMapLabel();
		RefreshSharedMapHeaders();
		_parentWindow?.SetStatus("Shared map paths cleared.");
	}

	private void HandleConsoleConnect(string ip)
	{
		bool isXbox = _consolePlatform == RTEConnectionType.ConsoleXbox;
		Action<string, string?, bool?> updateStatus = isXbox
			? _parentWindow.UpdateXboxStatus
			: _parentWindow.UpdateXbox360Status;

		if (string.IsNullOrEmpty(ip))
		{
			updateStatus("Please enter the console's IP address.", null, null);
			return;
		}

		// If already connected, disconnect
		if (_rteProvider is ConsoleRTEProvider)
		{
			_rteProvider = null;
			updateStatus("Disconnected.", "Connect", true);
			_parentWindow?.SetStatus("Console disconnected.");
			return;
		}

		// Create the appropriate console object
		XConsole console;
		if (isXbox)
		{
			console = new XbConsole(ip);
			AppState.Settings.ConsoleXboxIp = ip;
		}
		else
		{
			console = new XeConsole(ip, AppState.Settings.ConsoleXbox360Fusion);
			AppState.Settings.ConsoleXbox360Ip = ip;
		}
		AppState.Settings.Save();

		updateStatus("Connecting...", null, false);

		Task.Run(() =>
		{
			bool success = console.Connect();
			string runningTitle = null;
			if (success)
			{
				runningTitle = console.GetRunningTitle();
				console.Disconnect();
			}

			Dispatcher.UIThread.Post(() =>
			{
				if (success)
				{
					_rteProvider = new ConsoleRTEProvider(console);
					string titleInfo = !string.IsNullOrEmpty(runningTitle)
						? $" Running: {runningTitle}"
						: "";
					updateStatus($"Connected to {ip}.{titleInfo}", "Disconnect", true);
					_parentWindow?.SetStatus($"Console connected: {ip}");
				}
				else
				{
					updateStatus(
						$"Could not connect to {ip}. Verify the console is on and XBDM is running.",
						null, true);
					_parentWindow?.SetStatus($"Console connection failed: {ip}");
				}
			});
		});
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
		_disposed = true;

		// Save sidebar width before disposing.
		// When the user drags the GridSplitter, Avalonia converts the column
		// width to an absolute pixel value — read it back from the GridLength.
		var colWidth = MainGrid.ColumnDefinitions[0].Width;
		double sidebarWidth = colWidth.IsAbsolute ? colWidth.Value : 0;
		if (sidebarWidth <= 0)
			sidebarWidth = AppState.Settings.SidebarWidth; // preserve previous value
		if (sidebarWidth >= 200)
		{
			AppState.Settings.SidebarWidth = sidebarWidth;
			AppState.Settings.Save();
		}

		_reader?.Dispose();
		_fileStream?.Dispose();
		if (_consolePlatform == RTEConnectionType.ConsoleXbox)
			_parentWindow?.UnregisterXboxHandler();
		else if (_consolePlatform == RTEConnectionType.ConsoleXbox360)
			_parentWindow?.UnregisterXbox360Handler();
	}
}
