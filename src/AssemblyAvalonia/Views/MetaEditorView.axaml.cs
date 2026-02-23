using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using AssemblyAvalonia.Helpers;
using AssemblyAvalonia.Models;
using AssemblyAvalonia.Models.MetaData;
using AssemblyAvalonia.Plugins;
using Blamite.Blam;
using Blamite.IO;
using Blamite.Plugins;
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
	private int _baseSize;

	public MetaEditorView()
	{
		InitializeComponent();
	}

	public void LoadTag(TagEntry tag, ICacheFile cacheFile, EngineDescription buildInfo,
		string filePath, TagHierarchy hierarchy, Trie stringIdTrie, MainWindow parentWindow)
	{
		_tag = tag;
		_cacheFile = cacheFile;
		_buildInfo = buildInfo;
		_filePath = filePath;
		_stringIdTrie = stringIdTrie;
		_parentWindow = parentWindow;

		string groupMagic = CharConstant.ToString(tag.RawTag.Group.Magic);
		TagHeader.Text = $"{tag.TagFileName}.{groupMagic}";

		if (tag.RawTag.MetaLocation == null)
		{
			PluginInfo.Text = "This tag has no metadata (shared cache reference).";
			return;
		}

		_srcSegmentGroup = tag.RawTag.MetaLocation.BaseGroup;

		// Find plugin XML
		string pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
		string enginePluginDir = buildInfo.Settings.GetSetting<string>("plugins");
		string pluginPath = Path.Combine(pluginsDir, enginePluginDir,
			VariousFunctions.SterilizeTagGroupName(groupMagic).Trim() + ".xml");

		// Try fallback plugin directory
		if (!File.Exists(pluginPath) && buildInfo.Settings.PathExists("fallbackPlugins"))
		{
			string fallback = buildInfo.Settings.GetSetting<string>("fallbackPlugins");
			pluginPath = Path.Combine(pluginsDir, fallback,
				VariousFunctions.SterilizeTagGroupName(groupMagic).Trim() + ".xml");
		}

		if (!File.Exists(pluginPath))
		{
			PluginInfo.Text = $"No plugin found for '{groupMagic}'.";
			return;
		}

		try
		{
			bool alwaysBasicColor = buildInfo.Engine < EngineType.ThirdGeneration;
			var tagCommandState = TagDataCommandState.None;

			// Load plugin
			using (var xml = XmlReader.Create(pluginPath))
			{
				_pluginVisitor = new AssemblyPluginVisitor(
					hierarchy, stringIdTrie, _srcSegmentGroup,
					AppState.Settings.PluginsShowInvisibles,
					tagCommandState, alwaysBasicColor);
				AssemblyPluginLoader.LoadPlugin(xml, _pluginVisitor);
			}

			// Show plugin info
			if (_pluginVisitor.PluginRevisions.Count > 0)
			{
				var latest = _pluginVisitor.PluginRevisions[_pluginVisitor.PluginRevisions.Count - 1];
				PluginInfo.Text = $"Plugin: {groupMagic}.xml | {_pluginVisitor.Values.Count} fields | Last revised by {latest.Researcher}";
			}
			else
			{
				PluginInfo.Text = $"Plugin: {groupMagic}.xml | {_pluginVisitor.Values.Count} fields";
			}

			// Read field values from file
			long baseOffset = (uint)tag.RawTag.MetaLocation.AsOffset();
			var fileStreamManager = new FileStreamManager(filePath, buildInfo.Endian);
			_fileChanges = new FieldChangeSet();
			var metaReader = new MetaReader(fileStreamManager, baseOffset, cacheFile,
				buildInfo, MetaReader.LoadType.File, _fileChanges, _srcSegmentGroup);

			// Flatten tag blocks and read values
			_changeTracker = new FieldChangeTracker();
			_flattener = new TagBlockFlattener(metaReader, _changeTracker, _fileChanges);
			_flattener.Flatten(_pluginVisitor.Values);

			metaReader.ReadFields(_pluginVisitor.Values);

			// Wire up change tracking
			_changeTracker.RegisterChangeSet(_fileChanges);
			_changeTracker.Attach(_pluginVisitor.Values);

			// Display fields
			_fields = _pluginVisitor.Values;
			_baseSize = _pluginVisitor.BaseSize;
			FieldList.ItemsSource = _fields;

			SaveButton.IsVisible = true;
			HexToggle.IsVisible = true;
		}
		catch (Exception ex)
		{
			PluginInfo.Text = $"Error loading metadata: {ex.Message}";
		}
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
