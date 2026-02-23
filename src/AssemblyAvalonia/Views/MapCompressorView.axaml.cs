using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AssemblyAvalonia.Helpers;
using Blamite.Compression;
using Blamite.Serialization.Settings;

namespace AssemblyAvalonia.Views;

public partial class MapCompressorView : UserControl
{
	private readonly MainWindow _parentWindow;
	private bool _working;

	public MapCompressorView(MainWindow parent)
	{
		InitializeComponent();
		_parentWindow = parent;
	}

	public MapCompressorView()
	{
		InitializeComponent();
		_parentWindow = null!;
	}

	private void EnsureEngineDb()
	{
		if (AppState.EngineDb != null)
			return;

		string formatsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Formats");
		string dbPath = Path.Combine(formatsDir, "Engines.xml");
		if (!File.Exists(dbPath))
			throw new FileNotFoundException("Engines.xml not found in Formats directory.");

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

	private void AppendLog(string message)
	{
		Dispatcher.UIThread.Post(() =>
		{
			StatusLog.Text = string.IsNullOrEmpty(StatusLog.Text)
				? message
				: StatusLog.Text + "\n" + message;
		});
	}

	private void SetWorking(bool working)
	{
		_working = working;
		BtnDoSingle.IsEnabled = !working;
		BtnBatchCompress.IsEnabled = !working;
		BtnBatchDecompress.IsEnabled = !working;
	}

	private static string DescribeResult(CompressionState result, string fileName)
	{
		return result switch
		{
			CompressionState.Compressed => $"{fileName}: Compressed successfully.",
			CompressionState.Decompressed => $"{fileName}: Decompressed successfully.",
			CompressionState.Null => $"{fileName}: No compression required (format doesn't use compression).",
			CompressionState.ReadOnly => $"{fileName}: File is read-only. Check file properties.",
			CompressionState.NotSupported => $"{fileName}: Operation not supported for this format.",
			_ => $"{fileName}: Unknown result ({result})."
		};
	}

	#region Single File

	private async void BrowseFile_Click(object? sender, RoutedEventArgs e)
	{
		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Select a map file",
			AllowMultiple = false,
			FileTypeFilter = new[]
			{
				new FilePickerFileType("Halo Map Files") { Patterns = new[] { "*.map" } },
				new FilePickerFileType("All Files") { Patterns = new[] { "*" } }
			}
		});

		if (files.Count > 0)
		{
			string? path = files[0].TryGetLocalPath();
			if (path != null)
				SingleFilePath.Text = path;
		}
	}

	private void DoCompression_Click(object? sender, RoutedEventArgs e)
	{
		string filePath = SingleFilePath.Text?.Trim() ?? "";
		if (!File.Exists(filePath))
		{
			AppendLog("Error: Please select a valid .map file.");
			return;
		}

		if (_working) return;
		SetWorking(true);

		string fileName = Path.GetFileName(filePath);
		AppendLog($"Processing {fileName}...");

		Task.Run(() =>
		{
			try
			{
				EnsureEngineDb();
				var result = CompressionManager.HandleCompression(filePath, AppState.EngineDb);
				AppendLog(DescribeResult(result, fileName));
			}
			catch (Exception ex)
			{
				AppendLog($"{fileName}: Error - {ex.Message}");
			}
			finally
			{
				Dispatcher.UIThread.Post(() => SetWorking(false));
			}
		});
	}

	private void OpenMap_Click(object? sender, RoutedEventArgs e)
	{
		string filePath = SingleFilePath.Text?.Trim() ?? "";
		if (File.Exists(filePath))
			_parentWindow?.OpenFile(filePath);
		else
			AppendLog("Error: Please select a valid .map file first.");
	}

	#endregion

	#region Batch

	private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
	{
		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
		{
			Title = "Select folder with .map files",
			AllowMultiple = false
		});

		if (folders.Count > 0)
		{
			string? path = folders[0].TryGetLocalPath();
			if (path != null)
				BatchFolderPath.Text = path;
		}
	}

	private void BatchCompress_Click(object? sender, RoutedEventArgs e)
	{
		RunBatch(CompressionState.Compressed);
	}

	private void BatchDecompress_Click(object? sender, RoutedEventArgs e)
	{
		RunBatch(CompressionState.Decompressed);
	}

	private void RunBatch(CompressionState desiredState)
	{
		string folder = BatchFolderPath.Text?.Trim() ?? "";
		if (!Directory.Exists(folder))
		{
			AppendLog("Error: Please select a valid folder.");
			return;
		}

		string[] files = Directory.GetFiles(folder, "*.map");
		if (files.Length == 0)
		{
			AppendLog("No .map files found in the selected folder.");
			return;
		}

		if (_working) return;
		SetWorking(true);

		string action = desiredState == CompressionState.Compressed ? "Compressing" : "Decompressing";
		AppendLog($"{action} {files.Length} file(s)...");

		Task.Run(() =>
		{
			try
			{
				EnsureEngineDb();
				int processed = 0;
				foreach (string file in files)
				{
					string name = Path.GetFileName(file);
					try
					{
						var result = CompressionManager.HandleCompression(file, AppState.EngineDb, desiredState);
						AppendLog(DescribeResult(result, name));
					}
					catch (Exception ex)
					{
						AppendLog($"{name}: Error - {ex.Message}");
					}
					processed++;
					Dispatcher.UIThread.Post(() =>
						_parentWindow?.SetStatus($"Batch: {processed}/{files.Length} complete"));
				}
				AppendLog($"Batch complete. {processed} file(s) processed.");
			}
			catch (Exception ex)
			{
				AppendLog($"Batch error: {ex.Message}");
			}
			finally
			{
				Dispatcher.UIThread.Post(() => SetWorking(false));
			}
		});
	}

	#endregion
}
