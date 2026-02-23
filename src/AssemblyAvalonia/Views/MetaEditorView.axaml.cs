using System;
using System.IO;
using System.Xml;
using Avalonia.Controls;
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
	public MetaEditorView()
	{
		InitializeComponent();
	}

	public void LoadTag(TagEntry tag, ICacheFile cacheFile, EngineDescription buildInfo,
		string filePath, TagHierarchy hierarchy, Trie stringIdTrie)
	{
		string groupMagic = CharConstant.ToString(tag.RawTag.Group.Magic);
		TagHeader.Text = $"{tag.TagFileName}.{groupMagic}";

		if (tag.RawTag.MetaLocation == null)
		{
			PluginInfo.Text = "This tag has no metadata (shared cache reference).";
			return;
		}

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
			var srcSegmentGroup = tag.RawTag.MetaLocation.BaseGroup;
			bool alwaysBasicColor = buildInfo.Engine < EngineType.ThirdGeneration;

			// Determine tag data command state
			var tagCommandState = TagDataCommandState.None;

			// Load plugin
			AssemblyPluginVisitor pluginVisitor;
			using (var xml = XmlReader.Create(pluginPath))
			{
				pluginVisitor = new AssemblyPluginVisitor(
					hierarchy, stringIdTrie, srcSegmentGroup,
					AppState.Settings.PluginsShowInvisibles,
					tagCommandState, alwaysBasicColor);
				AssemblyPluginLoader.LoadPlugin(xml, pluginVisitor);
			}

			// Show plugin info
			if (pluginVisitor.PluginRevisions.Count > 0)
			{
				var latest = pluginVisitor.PluginRevisions[pluginVisitor.PluginRevisions.Count - 1];
				PluginInfo.Text = $"Plugin: {groupMagic}.xml | {pluginVisitor.Values.Count} fields | Last revised by {latest.Researcher}";
			}
			else
			{
				PluginInfo.Text = $"Plugin: {groupMagic}.xml | {pluginVisitor.Values.Count} fields";
			}

			// Read field values from file
			long baseOffset = (uint)tag.RawTag.MetaLocation.AsOffset();
			var fileStreamManager = new FileStreamManager(filePath, buildInfo.Endian);
			var changeSet = new FieldChangeSet();
			var metaReader = new MetaReader(fileStreamManager, baseOffset, cacheFile,
				buildInfo, MetaReader.LoadType.File, changeSet, srcSegmentGroup);

			// Flatten tag blocks for proper reading
			var changeTracker = new FieldChangeTracker();
			var flattener = new TagBlockFlattener(metaReader, changeTracker, changeSet);
			flattener.Flatten(pluginVisitor.Values);

			metaReader.ReadFields(pluginVisitor.Values);

			// Display fields
			FieldList.ItemsSource = pluginVisitor.Values;
		}
		catch (Exception ex)
		{
			PluginInfo.Text = $"Error loading metadata: {ex.Message}";
		}
	}
}
