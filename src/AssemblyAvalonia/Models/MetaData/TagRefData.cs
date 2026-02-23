using System;
using System.Collections.Generic;

namespace AssemblyAvalonia.Models.MetaData
{
	public class TagRefData : ValueField
	{
		private readonly TagHierarchy _allTags;
		private readonly bool _withGroup;
		private TagGroup _group;
		private bool _showButtons;
		private TagEntry _value;
		private List<TagGroup> _groupsWithNull;
		private IReadOnlyList<TagEntry> _groupEntries = Array.Empty<TagEntry>();

		public TagRefData(string name, uint offset, long address, TagHierarchy allTags, bool showButtons, bool withGroup,
			uint pluginLine, string tooltip)
			: base(name, offset, address, pluginLine, tooltip)
		{
			_allTags = allTags;
			_withGroup = withGroup;
			_showButtons = showButtons;
		}

		public TagEntry Value
		{
			get { return _value; }
			set
			{
				_value = value;
				NotifyPropertyChanged("Value");
				NotifyPropertyChanged("ValueIndex");
			}
		}

		/// <summary>
		///     Index into GroupEntries for ComboBox binding.
		///     Index 0 = NullTag, index N+1 = group.Children[N].
		/// </summary>
		public int ValueIndex
		{
			get
			{
				if (_value == null || _value.IsNull)
					return 0;
				if (_groupEntries.Count == 0)
					return -1;
				for (int i = 0; i < _groupEntries.Count; i++)
				{
					if (_groupEntries[i] == _value)
						return i;
				}
				return 0;
			}
			set
			{
				if (value >= 0 && value < _groupEntries.Count)
					Value = _groupEntries[value];
				else if (_groupEntries.Count > 0)
					Value = _groupEntries[0]; // null tag
			}
		}

		public bool ShowButtons
		{
			get { return _showButtons; }
			set
			{
				_showButtons = value;
				NotifyPropertyChanged("ShowButtons");
			}
		}

		public TagGroup Group
		{
			get { return _group; }
			set
			{
				_group = value;

				// Rebuild cached GroupEntries for the new group
				if (_group == null || _group.RawGroup == null)
					_groupEntries = Array.Empty<TagEntry>();
				else
				{
					var list = new List<TagEntry>(_group.Children.Count + 1);
					list.Add(_group.NullTag);
					list.AddRange(_group.Children);
					_groupEntries = list;
				}

				NotifyPropertyChanged("Group");
				NotifyPropertyChanged("GroupIndex");
				NotifyPropertyChanged("GroupEntries");
				NotifyPropertyChanged("ValueIndex");
			}
		}

		/// <summary>
		///     Index into GroupsWithNull for ComboBox binding (avoids SelectedItem reference issues).
		///     Index 0 = NullGroup, index N+1 = _allTags.Groups[N].
		/// </summary>
		public int GroupIndex
		{
			get
			{
				if (_group == null || _group.RawGroup == null)
					return 0;
				int idx = _allTags.Groups.IndexOf(_group);
				return idx >= 0 ? idx + 1 : 0;
			}
			set
			{
				if (value <= 0)
					Group = TagHierarchy.NullGroup;
				else if (value - 1 < _allTags.Groups.Count)
					Group = _allTags.Groups[value - 1];
			}
		}

		public List<TagGroup> GroupsWithNull
		{
			get
			{
				if (_groupsWithNull == null)
				{
					_groupsWithNull = new List<TagGroup>(_allTags.Groups.Count + 1);
					_groupsWithNull.Add(TagHierarchy.NullGroup);
					_groupsWithNull.AddRange(_allTags.Groups);
				}
				return _groupsWithNull;
			}
		}

		public IReadOnlyList<TagEntry> GroupEntries
		{
			get { return _groupEntries; }
		}

		public bool WithGroup
		{
			get { return _withGroup; }
		}

		public TagHierarchy Tags
		{
			get { return _allTags; }
		}

		public bool CanJump
		{
			get { return _value != null && !_value.IsNull; }
		}

		public override void Accept(IMetaFieldVisitor visitor)
		{
			visitor.VisitTagRef(this);
		}

		public override MetaField CloneValue()
		{
			var result = new TagRefData(Name, Offset, FieldAddress, _allTags, _showButtons, _withGroup, PluginLine, ToolTip);
			result.Group = _group;
			result.Value = _value;
			return result;
		}

		public override string AsString()
		{
			return string.Format("tagref | {0} | {1} {2}", Name, Value?.GroupName ?? "null", Value?.TagFileName ?? "null");
		}

		public override object GetAsJson()
		{
			Dictionary<string, object> dict = new Dictionary<string, object>();
			dict["GroupName"] = Value?.GroupName ?? "NONE";
			dict["Path"] = Value?.TagFileName ?? "";

			return dict;
		}
	}
}