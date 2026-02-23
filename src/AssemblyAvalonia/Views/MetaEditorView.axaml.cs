using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Xml;
using Avalonia.Controls;
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
			FieldList.ItemsSource = _fields;

			SaveButton.IsVisible = true;
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
}
