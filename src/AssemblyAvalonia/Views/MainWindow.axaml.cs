using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AssemblyAvalonia.Helpers;

namespace AssemblyAvalonia.Views;

public partial class MainWindow : Window
{
	private WelcomeView _welcomeView;

	public MainWindow()
	{
		InitializeComponent();
		XboxIpBox.Text = AppState.Settings.ConsoleXboxIp;
		Xbox360IpBox.Text = AppState.Settings.ConsoleXbox360Ip;
		BuildRecentFilesMenu();

		_welcomeView = new WelcomeView();
		_welcomeView.Initialize(this);
		WelcomeContent.Content = _welcomeView;

		Closing += OnWindowClosing;
	}

	private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
	{
		// Dispose all open map views so they can save sidebar width, etc.
		foreach (TabItem tab in DocumentTabs.Items.Cast<TabItem>())
		{
			if (tab.Content is HaloMapView mapView)
				mapView.Dispose();
		}
	}

	private void UpdateWelcomeVisibility()
	{
		bool hasTabs = DocumentTabs.Items.Count > 0;
		DocumentTabs.IsVisible = hasTabs;
		WelcomeContent.IsVisible = !hasTabs;
		if (!hasTabs)
			_welcomeView.RefreshRecentFiles();
	}

	private async void MenuOpen_Click(object? sender, RoutedEventArgs e)
	{
		var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Open Map File",
			AllowMultiple = false,
			FileTypeFilter = new[]
			{
				new FilePickerFileType("Halo Map Files") { Patterns = new[] { "*.map" } },
				new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
			}
		});

		if (files.Count > 0)
		{
			string path = files[0].TryGetLocalPath();
			if (path != null)
				OpenFile(path);
		}
	}

	public void OpenFile(string filePath)
	{
		if (!File.Exists(filePath))
		{
			StatusText.Text = $"File not found: {filePath}";
			return;
		}

		// Check if already open
		foreach (TabItem tab in DocumentTabs.Items.Cast<TabItem>())
		{
			if (tab.Tag is string existingPath &&
				string.Equals(existingPath, filePath, StringComparison.OrdinalIgnoreCase))
			{
				DocumentTabs.SelectedItem = tab;
				return;
			}
		}

		string fileName = Path.GetFileName(filePath);
		StatusText.Text = $"Opening {fileName}...";

		var mapView = new HaloMapView();
		var tab2 = new TabItem
		{
			Tag = filePath,
			Content = mapView
		};

		// Closable tab header
		var headerPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
		headerPanel.Children.Add(new TextBlock { Text = fileName, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
		var closeBtn = new Button
		{
			Content = "\u00D7",
			FontSize = 14,
			Padding = new Avalonia.Thickness(4, 0),
			MinWidth = 0,
			MinHeight = 0,
			Background = Avalonia.Media.Brushes.Transparent,
			BorderThickness = new Avalonia.Thickness(0)
		};
		closeBtn.Click += (_, _) =>
		{
			mapView.Dispose();
			DocumentTabs.Items.Remove(tab2);
			UpdateWelcomeVisibility();
		};
		headerPanel.Children.Add(closeBtn);
		tab2.Header = headerPanel;

		DocumentTabs.Items.Add(tab2);
		DocumentTabs.SelectedItem = tab2;
		UpdateWelcomeVisibility();

		mapView.OnLoadFailed = async (msg) =>
		{
			mapView.Dispose();
			DocumentTabs.Items.Remove(tab2);
			UpdateWelcomeVisibility();
			await ErrorDialog.Show(this, msg);
		};
		mapView.LoadMap(filePath, this);
	}

	private void MenuExit_Click(object? sender, RoutedEventArgs e)
	{
		Close();
	}

	private void MenuMapCompressor_Click(object? sender, RoutedEventArgs e)
	{
		// Check if already open
		foreach (TabItem tab in DocumentTabs.Items.Cast<TabItem>())
		{
			if (tab.Tag is string tag && tag == "MapCompressor")
			{
				DocumentTabs.SelectedItem = tab;
				return;
			}
		}

		var compressorView = new MapCompressorView(this);
		var newTab = new TabItem
		{
			Tag = "MapCompressor",
			Content = compressorView
		};

		var headerPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
		headerPanel.Children.Add(new TextBlock { Text = "Map Compressor", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
		var closeBtn = new Button
		{
			Content = "\u00D7",
			FontSize = 14,
			Padding = new Avalonia.Thickness(4, 0),
			MinWidth = 0,
			MinHeight = 0,
			Background = Avalonia.Media.Brushes.Transparent,
			BorderThickness = new Avalonia.Thickness(0)
		};
		closeBtn.Click += (_, _) =>
		{
			DocumentTabs.Items.Remove(newTab);
			UpdateWelcomeVisibility();
		};
		headerPanel.Children.Add(closeBtn);
		newTab.Header = headerPanel;

		DocumentTabs.Items.Add(newTab);
		DocumentTabs.SelectedItem = newTab;
		UpdateWelcomeVisibility();
	}

	private void MenuMemoryPoker_Click(object? sender, RoutedEventArgs e)
	{
		// Check if already open
		foreach (TabItem tab in DocumentTabs.Items.Cast<TabItem>())
		{
			if (tab.Tag is string tag && tag == "MemoryPoker")
			{
				DocumentTabs.SelectedItem = tab;
				return;
			}
		}

		var pokerView = new MemoryPokerView(this);
		var newTab = new TabItem
		{
			Tag = "MemoryPoker",
			Content = pokerView
		};

		var headerPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
		headerPanel.Children.Add(new TextBlock { Text = "Memory Poker", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
		var closeBtn = new Button
		{
			Content = "\u00D7",
			FontSize = 14,
			Padding = new Avalonia.Thickness(4, 0),
			MinWidth = 0,
			MinHeight = 0,
			Background = Avalonia.Media.Brushes.Transparent,
			BorderThickness = new Avalonia.Thickness(0)
		};
		closeBtn.Click += (_, _) =>
		{
			DocumentTabs.Items.Remove(newTab);
			UpdateWelcomeVisibility();
		};
		headerPanel.Children.Add(closeBtn);
		newTab.Header = headerPanel;

		DocumentTabs.Items.Add(newTab);
		DocumentTabs.SelectedItem = newTab;
		UpdateWelcomeVisibility();
	}

	private async void MenuAbout_Click(object? sender, RoutedEventArgs e)
	{
		var about = new AboutWindow();
		await about.ShowDialog(this);
	}

	public void SetStatus(string text)
	{
		StatusText.Text = text;
	}

	private Action<string>? _xboxConnectHandler;
	private Action<string>? _xbox360ConnectHandler;

	public void RegisterXboxHandler(Action<string> handler) => _xboxConnectHandler = handler;
	public void RegisterXbox360Handler(Action<string> handler) => _xbox360ConnectHandler = handler;
	public void UnregisterXboxHandler() => _xboxConnectHandler = null;
	public void UnregisterXbox360Handler() => _xbox360ConnectHandler = null;

	public void UpdateXboxStatus(string status, string? buttonLabel = null, bool? buttonEnabled = null)
	{
		XboxStatusText.Text = status;
		XboxStatusText.IsVisible = !string.IsNullOrEmpty(status);
		if (buttonLabel != null) XboxConnectBtn.Content = buttonLabel;
		if (buttonEnabled.HasValue) XboxConnectBtn.IsEnabled = buttonEnabled.Value;
	}

	public void UpdateXbox360Status(string status, string? buttonLabel = null, bool? buttonEnabled = null)
	{
		Xbox360StatusText.Text = status;
		Xbox360StatusText.IsVisible = !string.IsNullOrEmpty(status);
		if (buttonLabel != null) Xbox360ConnectBtn.Content = buttonLabel;
		if (buttonEnabled.HasValue) Xbox360ConnectBtn.IsEnabled = buttonEnabled.Value;
	}

	private void XboxConnect_Click(object? sender, RoutedEventArgs e)
	{
		if (_xboxConnectHandler != null)
			_xboxConnectHandler(XboxIpBox.Text?.Trim() ?? "");
		else
			UpdateXboxStatus("No Xbox map is currently open.");
	}

	private void Xbox360Connect_Click(object? sender, RoutedEventArgs e)
	{
		if (_xbox360ConnectHandler != null)
			_xbox360ConnectHandler(Xbox360IpBox.Text?.Trim() ?? "");
		else
			UpdateXbox360Status("No Xbox 360 map is currently open.");
	}

	public void BuildRecentFilesMenu()
	{
		MenuRecent.Items.Clear();
		var recents = AppState.Settings.RecentFiles;

		if (recents.Count == 0)
		{
			MenuRecent.Items.Add(new MenuItem { Header = "(No recent files)", IsEnabled = false });
			return;
		}

		foreach (var entry in recents)
		{
			var item = new MenuItem
			{
				Header = $"{entry.FileName} ({entry.FileGame})",
				Tag = entry.FilePath
			};
			item.Click += (_, _) =>
			{
				if (item.Tag is string path)
					OpenFile(path);
			};
			MenuRecent.Items.Add(item);
		}
	}
}
