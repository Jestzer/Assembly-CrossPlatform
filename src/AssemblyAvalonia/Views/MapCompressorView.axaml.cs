using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AssemblyAvalonia.Helpers;
using Blamite.Compression;
using Blamite.IO;
using Blamite.Serialization;
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
		BtnCompress.IsEnabled = !working && false;
		BtnDecompress.IsEnabled = !working && false;
		BtnBatchCompress.IsEnabled = !working;
		BtnBatchDecompress.IsEnabled = !working;
	}

	private void SetSingleButtons(CompressionState state)
	{
		BtnCompress.IsEnabled = !_working && state == CompressionState.Decompressed;
		BtnDecompress.IsEnabled = !_working && state == CompressionState.Compressed;
	}

	private void DetectState(string filePath)
	{
		Task.Run(() =>
		{
			try
			{
				EnsureEngineDb();
				CompressionState state;
				using (var fs = File.OpenRead(filePath))
				{
					var reader = new EndianReader(fs, Endian.LittleEndian);
					state = CompressionManager.DetermineState(reader, AppState.EngineDb,
						out EngineDescription _, out StructureValueCollection _);
				}

				string label = state switch
				{
					CompressionState.Compressed => "Status: Compressed",
					CompressionState.Decompressed => "Status: Decompressed",
					CompressionState.Null => "Status: No compression support",
					CompressionState.NotSupported => "Status: Not supported",
					_ => "Status: Unknown"
				};

				Dispatcher.UIThread.Post(() =>
				{
					StateLabel.Text = label;
					SetSingleButtons(state);
				});
			}
			catch (Exception ex)
			{
				Dispatcher.UIThread.Post(() =>
				{
					StateLabel.Text = "Status: Detection failed";
					BtnCompress.IsEnabled = false;
					BtnDecompress.IsEnabled = false;
				});
				AppendLog($"State detection error: {ex.Message}");
			}
		});
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
			{
				SingleFilePath.Text = path;
				DetectState(path);
			}
		}
	}

	private void RunSingleCompression(CompressionState desiredState)
	{
		string filePath = SingleFilePath.Text?.Trim() ?? "";
		if (!File.Exists(filePath))
		{
			AppendLog("Error: Please select a valid .map file.");
			return;
		}

		if (_working) return;
		SetWorking(true);
		StateLabel.Text = "Status: Processing...";

		string fileName = Path.GetFileName(filePath);
		string action = desiredState == CompressionState.Compressed ? "Compressing" : "Decompressing";
		AppendLog($"{action} {fileName}...");

		Task.Run(() =>
		{
			try
			{
				EnsureEngineDb();
				var result = CompressionManager.HandleCompression(filePath, AppState.EngineDb, desiredState);
				AppendLog(DescribeResult(result, fileName));
			}
			catch (Exception ex)
			{
				AppendLog($"{fileName}: Error - {ex.Message}");
			}
			finally
			{
				Dispatcher.UIThread.Post(() =>
				{
					_working = false;
					BtnBatchCompress.IsEnabled = true;
					BtnBatchDecompress.IsEnabled = true;
					DetectState(filePath);
				});
			}
		});
	}

	private void Compress_Click(object? sender, RoutedEventArgs e)
	{
		RunSingleCompression(CompressionState.Compressed);
	}

	private void Decompress_Click(object? sender, RoutedEventArgs e)
	{
		RunSingleCompression(CompressionState.Decompressed);
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
				Dispatcher.UIThread.Post(() =>
				{
					_working = false;
					BtnBatchCompress.IsEnabled = true;
					BtnBatchDecompress.IsEnabled = true;
					// Re-detect single file state if one is selected
					string singlePath = SingleFilePath.Text?.Trim() ?? "";
					if (File.Exists(singlePath))
						DetectState(singlePath);
				});
			}
		});
	}

	#endregion
}
