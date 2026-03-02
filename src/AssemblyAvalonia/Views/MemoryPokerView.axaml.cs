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
using Blamite.RTE.Console;

namespace AssemblyAvalonia.Views;

public partial class MemoryPokerView : UserControl
{
	private readonly MainWindow _parentWindow;
	private XeConsole? _console;
	private bool _connected;

	private PokeDatabase? _currentDatabase;
	private string? _currentDatabasePath;
	private PokeEntry? _selectedEntry;

	private readonly Dictionary<string, string> _gameFiles = new();

	public MemoryPokerView(MainWindow parent)
	{
		InitializeComponent();
		_parentWindow = parent;

		ConsoleIpBox.Text = AppState.Settings.ConsoleXbox360Ip;
		LoadGameList();
	}

	public MemoryPokerView()
	{
		InitializeComponent();
		_parentWindow = null!;
	}

	private string PokesDirectory =>
		Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Pokes");

	private void LoadGameList()
	{
		_gameFiles.Clear();
		GameCombo.Items.Clear();

		string pokesDir = PokesDirectory;
		if (!Directory.Exists(pokesDir))
		{
			SetStatus("Pokes directory not found.");
			return;
		}

		foreach (string file in Directory.GetFiles(pokesDir, "*.json").OrderBy(f => f))
		{
			string name = Path.GetFileNameWithoutExtension(file);
			_gameFiles[name] = file;
			GameCombo.Items.Add(name);
		}

		if (GameCombo.Items.Count > 0)
			GameCombo.SelectedIndex = 0;
	}

	private void GameCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (GameCombo.SelectedItem is not string gameName || !_gameFiles.ContainsKey(gameName))
			return;

		try
		{
			_currentDatabasePath = _gameFiles[gameName];
			_currentDatabase = PokeDatabase.LoadFromFile(_currentDatabasePath);
			PopulateCategoryFilter();
			ApplyFilter();
			SetStatus($"Loaded {_currentDatabase.Entries.Count} entries for {gameName}.");
		}
		catch (Exception ex)
		{
			SetStatus($"Failed to load {gameName}: {ex.Message}");
			_currentDatabase = null;
			_currentDatabasePath = null;
		}
	}

	private void PopulateCategoryFilter()
	{
		CategoryCombo.Items.Clear();
		CategoryCombo.Items.Add("All");

		if (_currentDatabase != null)
		{
			foreach (string cat in _currentDatabase.GetCategories())
				CategoryCombo.Items.Add(cat);
		}

		CategoryCombo.SelectedIndex = 0;
	}

	private void CategoryCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		ApplyFilter();
	}

	private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
	{
		ApplyFilter();
	}

	private void ApplyFilter()
	{
		if (_currentDatabase == null)
		{
			EntryList.ItemsSource = null;
			return;
		}

		string? category = CategoryCombo.SelectedItem as string;
		string search = SearchBox.Text?.Trim() ?? "";

		IEnumerable<PokeEntry> filtered = _currentDatabase.Entries;

		if (!string.IsNullOrEmpty(category) && category != "All")
			filtered = filtered.Where(e => e.Category == category);

		if (!string.IsNullOrEmpty(search))
			filtered = filtered.Where(e =>
				e.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
				e.Offset.Contains(search, StringComparison.OrdinalIgnoreCase) ||
				e.Description.Contains(search, StringComparison.OrdinalIgnoreCase));

		EntryList.ItemsSource = filtered.ToList();
	}

	private void EntryList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		_selectedEntry = EntryList.SelectedItem as PokeEntry;
	}

	// --- Console Connection ---

	private void Connect_Click(object? sender, RoutedEventArgs e)
	{
		if (_connected)
		{
			Disconnect();
			return;
		}

		string ip = ConsoleIpBox.Text?.Trim() ?? "";
		if (string.IsNullOrEmpty(ip))
		{
			SetStatus("Enter a console IP address.");
			return;
		}

		AppState.Settings.ConsoleXbox360Ip = ip;
		AppState.Settings.Save();

		ConnectBtn.IsEnabled = false;
		SetStatus("Connecting...");

		Task.Run(() =>
		{
			try
			{
				var console = new XeConsole(ip);
				bool ok = console.Connect();
				console.Disconnect();

				Dispatcher.UIThread.Post(() =>
				{
					if (ok)
					{
						_console = console;
						_connected = true;
						ConnectBtn.Content = "Disconnect";
						ConnectionStatus.Text = $"Connected to {ip}";
						SetStatus($"Connected to Xbox 360 at {ip}.");
					}
					else
					{
						SetStatus("Connection failed. Check IP and console power.");
					}
					ConnectBtn.IsEnabled = true;
				});
			}
			catch (Exception ex)
			{
				Dispatcher.UIThread.Post(() =>
				{
					SetStatus($"Connection error: {ex.Message}");
					ConnectBtn.IsEnabled = true;
				});
			}
		});
	}

	private void Disconnect()
	{
		_console = null;
		_connected = false;
		ConnectBtn.Content = "Connect";
		ConnectionStatus.Text = "Disconnected";
		SetStatus("Disconnected.");
	}

	// --- Poking ---

	private bool PokeEntry(PokeEntry entry)
	{
		if (_console == null || !_connected)
		{
			SetStatus("Not connected to a console.");
			return false;
		}

		try
		{
			uint address = entry.ParseOffset();
			byte[] data = entry.ValueToBytes();
			bool ok = _console.WriteMemory(address, data.Length, data);
			return ok;
		}
		catch (Exception ex)
		{
			SetStatus($"Poke failed for {entry.Name}: {ex.Message}");
			return false;
		}
	}

	private void PokeSelected_Click(object? sender, RoutedEventArgs e)
	{
		if (_selectedEntry == null)
		{
			SetStatus("No entry selected. Click a row first.");
			return;
		}

		if (PokeEntry(_selectedEntry))
			SetStatus($"Poked {_selectedEntry.Name} = {_selectedEntry.CurrentValue} at {_selectedEntry.Offset}.");
	}

	private void PokeAllChanged_Click(object? sender, RoutedEventArgs e)
	{
		if (_currentDatabase == null)
			return;

		var changed = _currentDatabase.Entries.Where(x => x.IsChanged).ToList();
		if (changed.Count == 0)
		{
			SetStatus("No values have been changed.");
			return;
		}

		int success = 0;
		foreach (var entry in changed)
		{
			if (PokeEntry(entry))
				success++;
		}

		SetStatus($"Poked {success}/{changed.Count} changed entries.");
	}

	private void ResetAll_Click(object? sender, RoutedEventArgs e)
	{
		if (_currentDatabase == null)
			return;

		foreach (var entry in _currentDatabase.Entries)
			entry.CurrentValue = entry.DefaultValue;

		ApplyFilter();
		SetStatus("All values reset to defaults.");
	}

	// --- Entry Editing ---

	private void AddEntry_Click(object? sender, RoutedEventArgs e)
	{
		if (_currentDatabase == null)
		{
			SetStatus("No game database loaded.");
			return;
		}

		var entry = new PokeEntry
		{
			Offset = "0x00000000",
			Category = "Custom",
			Name = "New Entry",
			DefaultValue = "0",
			ValueType = PokeValueType.Byte,
			Description = "",
			CurrentValue = "0"
		};

		_currentDatabase.Entries.Add(entry);
		ApplyFilter();
		SetStatus("Added new entry. Edit it in the list, then save.");
	}

	private void RemoveSelected_Click(object? sender, RoutedEventArgs e)
	{
		if (_currentDatabase == null || _selectedEntry == null)
		{
			SetStatus("No entry selected.");
			return;
		}

		string name = _selectedEntry.Name;
		_currentDatabase.Entries.Remove(_selectedEntry);
		_selectedEntry = null;
		ApplyFilter();
		SetStatus($"Removed '{name}'. Save the database to persist.");
	}

	private void SaveDatabase_Click(object? sender, RoutedEventArgs e)
	{
		if (_currentDatabase == null || _currentDatabasePath == null)
		{
			SetStatus("No database loaded.");
			return;
		}

		try
		{
			_currentDatabase.SaveToFile(_currentDatabasePath);
			SetStatus($"Saved {_currentDatabase.GameName} ({_currentDatabase.Entries.Count} entries).");
		}
		catch (Exception ex)
		{
			SetStatus($"Save failed: {ex.Message}");
		}
	}

	private async void ImportVal_Click(object? sender, RoutedEventArgs e)
	{
		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Import Ascension .val File",
			AllowMultiple = false,
			FileTypeFilter = new[]
			{
				new FilePickerFileType("Ascension Value Files") { Patterns = new[] { "*.val" } },
				new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
			}
		});

		if (files.Count == 0) return;

		string? valPath = files[0].TryGetLocalPath();
		if (valPath == null) return;

		try
		{
			var imported = PokeDatabase.LoadFromAscensionFormat(valPath);
			string outputPath = Path.Combine(PokesDirectory, imported.GameName + ".json");

			if (!Directory.Exists(PokesDirectory))
				Directory.CreateDirectory(PokesDirectory);

			imported.SaveToFile(outputPath);
			LoadGameList();

			// Select the newly imported game
			int idx = GameCombo.Items.Cast<string>().ToList().IndexOf(imported.GameName);
			if (idx >= 0)
				GameCombo.SelectedIndex = idx;

			SetStatus($"Imported {imported.Entries.Count} entries from {Path.GetFileName(valPath)}.");
		}
		catch (Exception ex)
		{
			SetStatus($"Import failed: {ex.Message}");
		}
	}

	private void SetStatus(string text)
	{
		StatusText.Text = text;
		_parentWindow?.SetStatus(text);
	}
}
