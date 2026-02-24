using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AssemblyAvalonia.Helpers;

namespace AssemblyAvalonia.Views;

public partial class WelcomeView : UserControl
{
	private MainWindow _parentWindow;

	public WelcomeView()
	{
		InitializeComponent();
	}

	public void Initialize(MainWindow parent)
	{
		_parentWindow = parent;
		RefreshRecentFiles();
	}

	public void RefreshRecentFiles()
	{
		var recents = AppState.Settings.RecentFiles;
		RecentList.ItemsSource = recents;
		NoRecentsText.IsVisible = recents.Count == 0;
	}

	private async void OpenFile_Click(object? sender, RoutedEventArgs e)
	{
		var files = await _parentWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
				_parentWindow.OpenFile(path);
		}
	}

	private void RecentFile_Click(object? sender, RoutedEventArgs e)
	{
		if (sender is Button btn && btn.Tag is string path)
			_parentWindow.OpenFile(path);
	}
}
