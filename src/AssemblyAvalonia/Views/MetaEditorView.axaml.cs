using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AssemblyAvalonia.Helpers;
using AssemblyAvalonia.Models;
using AssemblyAvalonia.Models.MetaData;
using AssemblyAvalonia.Plugins;
using Blamite.Blam;
using Blamite.IO;
using Blamite.Plugins;
using Blamite.RTE;
using Blamite.Serialization;
using Blamite.Util;

namespace AssemblyAvalonia.Views;

public partial class MetaEditorView : UserControl
{
	// Keep references alive so flattener PropertyChanged events work
	private TagBlockFlattener _flattener;
	private FieldChangeTracker _changeTracker;
	private FieldChangeSet _fileChanges;
	private AssemblyPluginVisitor _pluginVisitor;
	private ObservableCollection<MetaField> _fields;

	// Stored for save functionality
	private ICacheFile _cacheFile;
	private EngineDescription _buildInfo;
	private TagEntry _tag;
	private FileSegmentGroup _srcSegmentGroup;
	private Trie _stringIdTrie;
	private string _filePath;
	private MainWindow _parentWindow;
	private RTEProvider _rteProvider;
	private int _baseSize;

	// Cache resolved plugin paths to avoid repeated File.Exists checks
	private static readonly ConcurrentDictionary<string, string> _pluginPathCache = new();

	public MetaEditorView()
	{
		InitializeComponent();
	}

	/// <summary>
	///     Called when the tag load completes (success or failure).
	///     Set by the caller so it can update status text, etc.
	/// </summary>
	public Action<bool, string> OnLoadComplete { get; set; }

	/// <summary>
	///     Callback to switch from meta editor back to bitmap preview.
	///     Set by the caller for bitm tags. When set, the "Show Preview" button is shown.
	/// </summary>
	public Action ShowBitmapPreview { get; set; }

	/// <summary>
	///     Optional override for the "Show Preview" button text.
	/// </summary>
	public string ShowPreviewLabel { get; set; }

	private static string ResolvePluginPath(string groupMagic, EngineDescription buildInfo)
	{
		string cacheKey = $"{buildInfo.Name}:{groupMagic}";
		if (_pluginPathCache.TryGetValue(cacheKey, out string cached))
			return cached;

		string pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
		string enginePluginDir = buildInfo.Settings.GetSetting<string>("plugins");
		string pluginPath = Path.Combine(pluginsDir, enginePluginDir,
			VariousFunctions.SterilizeTagGroupName(groupMagic).Trim() + ".xml");

		if (!File.Exists(pluginPath) && buildInfo.Settings.PathExists("fallbackPlugins"))
		{
			string fallback = buildInfo.Settings.GetSetting<string>("fallbackPlugins");
			pluginPath = Path.Combine(pluginsDir, fallback,
				VariousFunctions.SterilizeTagGroupName(groupMagic).Trim() + ".xml");
		}

		if (!File.Exists(pluginPath))
			pluginPath = null;

		_pluginPathCache[cacheKey] = pluginPath;
		return pluginPath;
	}

	public void LoadTag(TagEntry tag, ICacheFile cacheFile, EngineDescription buildInfo,
		string filePath, TagHierarchy hierarchy, Trie stringIdTrie, MainWindow parentWindow,
		RTEProvider rteProvider = null)
	{
		_tag = tag;
		_cacheFile = cacheFile;
		_buildInfo = buildInfo;
		_filePath = filePath;
		_stringIdTrie = stringIdTrie;
		_parentWindow = parentWindow;
		_rteProvider = rteProvider;

		string groupMagic = CharConstant.ToString(tag.RawTag.Group.Magic);
		TagHeader.Text = $"{tag.TagFileName}.{groupMagic}";

		if (tag.RawTag.MetaLocation == null)
		{
			PluginInfo.Text = "This tag has no metadata (shared cache reference).";
			OnLoadComplete?.Invoke(true, null);
			return;
		}

		_srcSegmentGroup = tag.RawTag.MetaLocation.BaseGroup;

		// Immediate UI feedback
		PluginInfo.Text = "Loading...";
		SaveButton.IsVisible = false;
		HexToggle.IsVisible = false;
		PokeButton.IsVisible = false;
		RefreshMemButton.IsVisible = false;
		ShowPreviewButton.IsVisible = ShowBitmapPreview != null;
		if (ShowPreviewLabel != null)
			ShowPreviewButton.Content = ShowPreviewLabel;

		// Capture values for background thread
		var segmentGroup = _srcSegmentGroup;
		long baseOffset = (uint)tag.RawTag.MetaLocation.AsOffset();
		bool alwaysBasicColor = buildInfo.Engine < EngineType.ThirdGeneration;

		Task.Run(() =>
		{
			try
			{
				// Resolve plugin path (cached)
				string pluginPath = ResolvePluginPath(groupMagic, buildInfo);
				if (pluginPath == null)
				{
					Dispatcher.UIThread.Post(() =>
					{
						PluginInfo.Text = $"No plugin found for '{groupMagic}'.";
						OnLoadComplete?.Invoke(true, null);
					});
					return;
				}

				// Parse plugin XML
				var tagCommandState = TagDataCommandState.None;
				AssemblyPluginVisitor pluginVisitor;
				using (var xml = XmlReader.Create(pluginPath))
				{
					pluginVisitor = new AssemblyPluginVisitor(
						hierarchy, stringIdTrie, segmentGroup,
						AppState.Settings.PluginsShowInvisibles,
						tagCommandState, alwaysBasicColor);
					AssemblyPluginLoader.LoadPlugin(xml, pluginVisitor);
				}

				// Build plugin info text
				string pluginInfoText;
				if (pluginVisitor.PluginRevisions.Count > 0)
				{
					var latest = pluginVisitor.PluginRevisions[pluginVisitor.PluginRevisions.Count - 1];
					pluginInfoText = $"Plugin: {groupMagic}.xml | {pluginVisitor.Values.Count} fields | Last revised by {latest.Researcher}";
				}
				else
				{
					pluginInfoText = $"Plugin: {groupMagic}.xml | {pluginVisitor.Values.Count} fields";
				}

				// Read field values from file
				var fileStreamManager = new FileStreamManager(filePath, buildInfo.Endian);
				var fileChanges = new FieldChangeSet();
				var metaReader = new MetaReader(fileStreamManager, baseOffset, cacheFile,
					buildInfo, MetaReader.LoadType.File, fileChanges, segmentGroup);

				// Flatten tag blocks and read values
				var changeTracker = new FieldChangeTracker();
				var flattener = new TagBlockFlattener(metaReader, changeTracker, fileChanges);
				flattener.Flatten(pluginVisitor.Values);
				metaReader.ReadFields(pluginVisitor.Values);

				// Wire up change tracking
				changeTracker.RegisterChangeSet(fileChanges);
				changeTracker.Attach(pluginVisitor.Values);

				int baseSize = pluginVisitor.BaseSize;
				var fields = pluginVisitor.Values;

				// Update UI on dispatcher thread
				Dispatcher.UIThread.Post(() =>
				{
					_pluginVisitor = pluginVisitor;
					_fileChanges = fileChanges;
					_changeTracker = changeTracker;
					_flattener = flattener;
					_fields = fields;
					_baseSize = baseSize;

					PluginInfo.Text = pluginInfoText;
					FieldList.ItemsSource = _fields;

					SaveButton.IsVisible = true;
					HexToggle.IsVisible = true;
					if (_rteProvider != null)
					{
						PokeButton.IsVisible = true;
						RefreshMemButton.IsVisible = true;
					}

					OnLoadComplete?.Invoke(true, null);
				});
			}
			catch (Exception ex)
			{
				Dispatcher.UIThread.Post(() =>
				{
					PluginInfo.Text = $"Error loading metadata: {ex.Message}";
					OnLoadComplete?.Invoke(false, ex.Message);
				});
			}
		});
	}

	private void Save_Click(object? sender, RoutedEventArgs e)
	{
		if (_pluginVisitor == null || _cacheFile == null)
			return;

		try
		{
			var fileStreamManager = new FileStreamManager(_filePath, _buildInfo.Endian);
			using (var stream = fileStreamManager.OpenReadWrite())
			{
				long baseOffset = (uint)_tag.RawTag.MetaLocation.AsOffset();
				var metaWriter = new MetaWriter(
					stream, baseOffset, _cacheFile, _buildInfo,
					MetaWriter.SaveType.File, null, _stringIdTrie, _srcSegmentGroup);
				metaWriter.WriteFields(_pluginVisitor.Values);
				_cacheFile.SaveChanges(stream);
			}

			_fileChanges.MarkAllUnchanged();
			_parentWindow?.SetStatus("Tag saved successfully.");
		}
		catch (Exception ex)
		{
			_parentWindow?.SetStatus($"Save failed: {ex.Message}");
		}
	}

	private void Poke_Click(object? sender, RoutedEventArgs e)
	{
		if (_rteProvider == null || _pluginVisitor == null || _cacheFile == null)
			return;

		try
		{
			using (IStream metaStream = _rteProvider.GetCacheStream(_cacheFile, _tag.RawTag))
			{
				if (metaStream != null)
				{
					// Verify we can read from the tag's location before writing
					try
					{
						long tagPtr = _tag.RawTag.MetaLocation.AsPointer();
						metaStream.SeekTo(tagPtr);
						((IReader)metaStream).ReadUInt32();
					}
					catch (Exception readEx)
					{
						string diag = "";
						if (_rteProvider is Blamite.RTE.PC.PCRTEProvider pcProvider)
						{
							diag = $" | PID={pcProvider.LastGamePid}" +
								$" mod=0x{pcProvider.ModuleBaseAddress:X}" +
								$" cache=0x{pcProvider.CurrentCacheAddress:X}" +
								$" vBase=0x{(_cacheFile.MetaArea?.BasePointer ?? 0):X}";
						}
						_parentWindow?.SetStatus($"Poke failed: cannot read tag memory ({readEx.Message}){diag}");
						return;
					}

					// Write changed fields to game memory
					var metaWriter = new MetaWriter(
						metaStream,
						_tag.RawTag.MetaLocation.AsPointer(),
						_cacheFile, _buildInfo,
						MetaWriter.SaveType.Memory,
						_fileChanges,
						_stringIdTrie, _srcSegmentGroup);
					metaWriter.WriteFields(_pluginVisitor.Values);
					_parentWindow?.SetStatus("Changes poked to game memory.");
				}
				else
				{
					_parentWindow?.SetStatus($"Poke failed: {_rteProvider.ErrorMessage}");
				}
			}
		}
		catch (Exception ex)
		{
			_parentWindow?.SetStatus($"Poke failed: {ex.Message}");
		}
	}

	private void RefreshFromMemory_Click(object? sender, RoutedEventArgs e)
	{
		if (_rteProvider == null || _pluginVisitor == null || _cacheFile == null)
			return;

		try
		{
			var rteStreamManager = new RTEStreamManager(_rteProvider, _cacheFile, _tag.RawTag);
			long basePointer = _tag.RawTag.MetaLocation.AsPointer();
			var metaReader = new MetaReader(rteStreamManager, basePointer, _cacheFile,
				_buildInfo, MetaReader.LoadType.Memory, _fileChanges, _srcSegmentGroup);
			metaReader.ReadFields(_pluginVisitor.Values);
			_parentWindow?.SetStatus("Refreshed from game memory.");
		}
		catch (Exception ex)
		{
			_parentWindow?.SetStatus($"Refresh failed: {ex.Message}");
		}
	}

	private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
	{
		if (_pluginVisitor == null)
			return;

		string filter = SearchBox.Text?.Trim() ?? "";
		if (string.IsNullOrEmpty(filter))
		{
			FieldList.ItemsSource = _pluginVisitor.Values;
			return;
		}

		string lowerFilter = filter.ToLowerInvariant();
		var filtered = new List<MetaField>();
		foreach (var field in _pluginVisitor.Values)
		{
			string name = GetFieldName(field);
			if (name != null && name.ToLowerInvariant().Contains(lowerFilter))
				filtered.Add(field);
		}
		FieldList.ItemsSource = filtered;
	}

	private static string GetFieldName(MetaField field)
	{
		if (field is ValueField vf)
			return vf.DisplayName;
		if (field is CommentData cd)
			return cd.DisplayName;
		if (field is WrappedTagBlockEntry wt)
			return GetFieldName(wt.WrappedField);
		return null;
	}

	private void ShowPreview_Click(object? sender, RoutedEventArgs e)
	{
		ShowBitmapPreview?.Invoke();
	}

	private void HexToggle_Click(object? sender, RoutedEventArgs e)
	{
		bool showHex = HexToggle is ToggleButton tb && tb.IsChecked == true;
		if (showHex)
		{
			LoadHexView();
			HexScroller.IsVisible = true;
			FieldScroller.IsVisible = false;
		}
		else
		{
			HexScroller.IsVisible = false;
			FieldScroller.IsVisible = true;
		}
	}

	private void LoadHexView()
	{
		try
		{
			var fsm = new FileStreamManager(_filePath, _buildInfo.Endian);
			using (var reader = fsm.OpenRead())
			{
				long offset = (uint)_tag.RawTag.MetaLocation.AsOffset();
				reader.SeekTo(offset);
				int size = _baseSize > 0 ? _baseSize : 4096;
				byte[] data = reader.ReadBlock(size);
				HexDisplay.Text = FormatHexDump(data, offset);
			}
		}
		catch (Exception ex)
		{
			HexDisplay.Text = $"Error reading hex data: {ex.Message}";
		}
	}

	private static string FormatHexDump(byte[] data, long baseOffset)
	{
		var sb = new StringBuilder();
		for (int i = 0; i < data.Length; i += 16)
		{
			sb.Append($"{baseOffset + i:X8}  ");

			// Hex bytes
			for (int j = 0; j < 16; j++)
			{
				if (i + j < data.Length)
					sb.Append($"{data[i + j]:X2} ");
				else
					sb.Append("   ");
				if (j == 7)
					sb.Append(' ');
			}

			sb.Append(" |");

			// ASCII
			for (int j = 0; j < 16 && i + j < data.Length; j++)
			{
				byte b = data[i + j];
				sb.Append(b >= 0x20 && b <= 0x7E ? (char)b : '.');
			}

			sb.AppendLine("|");
		}
		return sb.ToString();
	}
}
