using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AssemblyAvalonia.Models;
using Blamite.Blam;
using Blamite.Blam.Textures;
using Blamite.IO;
using Blamite.Serialization;
using Blamite.Util;

namespace AssemblyAvalonia.Views;

public partial class BitmapPreviewView : UserControl
{
	private ICacheFile _cacheFile;
	private EngineDescription _buildInfo;
	private TagEntry _tag;
	private string _filePath;
	private MainWindow _parentWindow;
	private TagHierarchy _hierarchy;
	private Trie _stringIdTrie;
	private Blamite.RTE.RTEProvider _rteProvider;

	private List<BitmapInfo> _bitmapEntries;
	private Dictionary<int, string> _sharedMapOverrides = new();
	private BitmapInfo _pendingBrowseInfo;
	private WriteableBitmap _currentBitmap;
	private byte[] _currentRawData;
	private BitmapInfo _currentBitmapInfo;
	private ThirdGenBitmapTagReader _thirdGenReader;

	/// <summary>
	///     Callback to switch from bitmap preview to the meta editor view.
	///     Set by the caller (HaloMapView).
	/// </summary>
	public Action ShowMetaEditor { get; set; }

	/// <summary>
	///     Pre-populated shared map overrides from persisted settings.
	///     Set by the caller before calling LoadBitmap.
	/// </summary>
	public Dictionary<int, string> InitialSharedMapOverrides { get; set; }

	/// <summary>
	///     Called when the user browses for a shared map file from the bitmap error prompt.
	///     Parameters: sourceIndex, chosenPath.
	/// </summary>
	public Action<int, string> OnSharedMapSelected { get; set; }

	public BitmapPreviewView()
	{
		InitializeComponent();
	}

	public void LoadBitmap(TagEntry tag, ICacheFile cacheFile, EngineDescription buildInfo,
		string filePath, TagHierarchy hierarchy, Trie stringIdTrie, MainWindow parentWindow,
		Blamite.RTE.RTEProvider rteProvider = null)
	{
		_tag = tag;
		_cacheFile = cacheFile;
		_buildInfo = buildInfo;
		_filePath = filePath;
		_hierarchy = hierarchy;
		_stringIdTrie = stringIdTrie;
		_parentWindow = parentWindow;
		_rteProvider = rteProvider;

		// Pre-populate shared map overrides from persisted settings
		if (InitialSharedMapOverrides != null && InitialSharedMapOverrides.Count > 0)
			_sharedMapOverrides = new Dictionary<int, string>(InitialSharedMapOverrides);

		string groupMagic = CharConstant.ToString(tag.RawTag.Group.Magic);
		TagHeader.Text = $"{tag.TagFileName}.{groupMagic}";

		if (tag.RawTag.MetaLocation == null)
		{
			ShowStatus("This tag has no metadata (shared cache reference).");
			return;
		}

		BitmapInfoText.Text = "Loading...";

		Task.Run(() =>
		{
			try
			{
				ReadBitmapEntries();
			}
			catch (Exception ex)
			{
				Dispatcher.UIThread.Post(() =>
				{
					ShowStatus($"Error reading bitmap tag: {ex.Message}");
					BitmapInfoText.Text = "Error";
				});
			}
		});
	}

	private void ReadBitmapEntries()
	{
		if (_cacheFile.Engine == EngineType.SecondGeneration)
		{
			var reader_tag = new SecondGenBitmapTagReader();
			using (var stream = System.IO.File.OpenRead(_filePath))
			using (var reader = new EndianReader(stream, _buildInfo.Endian))
			{
				_bitmapEntries = reader_tag.ReadBitmapTag(_tag.RawTag, reader, _cacheFile);
			}
		}
		else if (_cacheFile.Engine == EngineType.ThirdGeneration)
		{
			_thirdGenReader = new ThirdGenBitmapTagReader();
			using (var stream = System.IO.File.OpenRead(_filePath))
			using (var reader = new EndianReader(stream, _buildInfo.Endian))
			{
				_bitmapEntries = _thirdGenReader.ReadBitmapTag(
					_tag.RawTag, reader, _cacheFile, _buildInfo);
			}
		}
		else
		{
			Dispatcher.UIThread.Post(() =>
				ShowStatus($"Bitmap preview is not yet supported for {_cacheFile.Engine} engine maps."));
			return;
		}

		if (_bitmapEntries == null || _bitmapEntries.Count == 0)
		{
			Dispatcher.UIThread.Post(() => ShowStatus("No bitmap entries found in this tag."));
			return;
		}

		// Build display strings for the ComboBox
		var displayItems = new List<string>();
		for (int i = 0; i < _bitmapEntries.Count; i++)
		{
			var info = _bitmapEntries[i];
			displayItems.Add($"{i}: {info.Width}x{info.Height} {info.Format}");
		}

		Dispatcher.UIThread.Post(() =>
		{
			BitmapSelector.ItemsSource = displayItems;
			if (displayItems.Count > 0)
				BitmapSelector.SelectedIndex = 0;
		});
	}

	private void BitmapSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		int index = BitmapSelector.SelectedIndex;
		if (index < 0 || _bitmapEntries == null || index >= _bitmapEntries.Count)
			return;

		var info = _bitmapEntries[index];
		string flagsStr = "";
		if (info.IsCompressed) flagsStr += "Compressed ";
		if (info.IsSwizzled) flagsStr += "Swizzled ";
		if ((info.Flags & BitmapFlags.Linear) != 0) flagsStr += "Linear ";
		if (info.IsTiled) flagsStr += "Tiled ";

		if (_cacheFile.Engine == EngineType.SecondGeneration)
		{
			int sourceIndex = SecondGenBitmapTagReader.GetActiveSourceIndex(info);
			string source = SecondGenBitmapTagReader.GetSourceDescription(
				sourceIndex << 30);
			BitmapInfoText.Text = $"{info.Width}x{info.Height} | {info.Format} | {flagsStr}| {source} | Mipmaps: {info.MipmapCount}";
		}
		else
		{
			BitmapInfoText.Text = $"{info.Width}x{info.Height} | {info.Format} | {flagsStr}| Mipmaps: {info.MipmapCount}";
		}

		// Hide any previous status / browse button
		StatusPanel.IsVisible = false;
		BrowseSharedMapButton.IsVisible = false;

		// Decode on background thread
		Task.Run(() =>
		{
			try
			{
				DecodeAndDisplay(info);
			}
			catch (Exception ex)
			{
				Dispatcher.UIThread.Post(() => ShowStatus($"Error decoding bitmap: {ex.Message}"));
			}
		});
	}

	private void DecodeAndDisplay(BitmapInfo info)
	{
		byte[] rawData;

		if (_cacheFile.Engine == EngineType.ThirdGeneration && _thirdGenReader != null)
		{
			// ThirdGen: extract pixel data via resource system
			using (var stream = System.IO.File.OpenRead(_filePath))
			using (var reader = new EndianReader(stream, _buildInfo.Endian))
			{
				rawData = _thirdGenReader.ReadPixelData(info, _cacheFile, reader, _filePath);
			}

			// Untile Xbox 360 tiled textures
			if (rawData != null && info.IsTiled)
				rawData = Xbox360Untiler.Untile(rawData, info);
		}
		else
		{
			// SecondGen: read from raw offsets with shared map overrides
			var tagReader = new SecondGenBitmapTagReader();
			rawData = tagReader.ReadPixelData(info, _filePath, _buildInfo.Endian, _sharedMapOverrides);
		}

		// Store raw data for DDS extraction (before decode)
		var rawDataForExtract = rawData;

		if (rawData == null)
		{
			if (_cacheFile.Engine == EngineType.SecondGeneration)
			{
				int sourceIndex = SecondGenBitmapTagReader.GetActiveSourceIndex(info);
				string srcName = SecondGenBitmapTagReader.GetSourceDescription(sourceIndex << 30);
				string srcPixels = SecondGenBitmapTagReader.GetSourceDescription(info.PixelsOffset);
				string srcLod1 = SecondGenBitmapTagReader.GetSourceDescription(info.Lod1Offset);
				int pxOff = info.PixelsOffset & 0x3FFFFFFF;
				int lodOff = info.Lod1Offset & 0x3FFFFFFF;
				bool isShared = sourceIndex > 0;

				Dispatcher.UIThread.Post(() =>
				{
					ShowStatus($"Could not read pixel data. " +
							   $"PixelsOffset: 0x{pxOff:X} ({srcPixels}), " +
							   $"LOD1Offset: 0x{lodOff:X} ({srcLod1}), " +
							   $"LOD1Size: {info.Lod1Size}, CalcSize: {info.CalculatedBaseMipSize}. " +
							   (isShared
								   ? $"The bitmap data is in {srcName}. Use the button below to locate it."
								   : "The pixel data could not be read from the map file."));

					if (isShared)
					{
						_pendingBrowseInfo = info;
						BrowseSharedMapButton.Content = $"Browse for {srcName}...";
						BrowseSharedMapButton.IsVisible = true;
					}
				});
			}
			else
			{
				string detail = _thirdGenReader?.LastError ?? "Unknown error";
				Dispatcher.UIThread.Post(() =>
					ShowStatus($"Could not extract pixel data from resource. " +
					           $"Datum: 0x{info.ResourceDatumValue:X8}, " +
					           $"Size: {info.PixelsSize}, CalcSize: {info.CalculatedBaseMipSize}. " +
					           detail));
			}
			return;
		}

		bool bigEndian = _buildInfo.Endian == Endian.BigEndian;
		byte[] decoded = BitmapDecoder.Decode(info, rawData, bigEndian);

		if (decoded == null)
		{
			int formatValue = (int)info.Format;
			Dispatcher.UIThread.Post(() =>
				ShowStatus($"Unsupported bitmap format: {info.Format} (0x{formatValue:X2})"));
			return;
		}

		// Create WriteableBitmap on the UI thread
		Dispatcher.UIThread.Post(() =>
		{
			try
			{
				var wb = new WriteableBitmap(
					new PixelSize(info.Width, info.Height),
					new Vector(96, 96),
					Avalonia.Platform.PixelFormats.Bgra8888,
					AlphaFormat.Unpremul);

				using (var fb = wb.Lock())
				{
					int stride = info.Width * 4;
					if (fb.RowBytes == stride)
					{
						// Row bytes match, copy all at once
						Marshal.Copy(decoded, 0, fb.Address, Math.Min(decoded.Length, fb.Size.Height * fb.RowBytes));
					}
					else
					{
						// Copy row by row to handle stride differences
						for (int y = 0; y < info.Height; y++)
						{
							int srcOffset = y * stride;
							IntPtr dstRow = fb.Address + y * fb.RowBytes;
							int bytesToCopy = Math.Min(stride, decoded.Length - srcOffset);
							if (bytesToCopy <= 0) break;
							Marshal.Copy(decoded, srcOffset, dstRow, bytesToCopy);
						}
					}
				}

				PreviewImage.Source = wb;
				_currentBitmap = wb;
				_currentRawData = rawDataForExtract;
				_currentBitmapInfo = info;
				ExtractPngButton.IsVisible = true;
				ExtractDdsButton.IsVisible = true;
				InjectDdsButton.IsVisible = (_cacheFile.Engine == EngineType.SecondGeneration);
				BrowseSharedMapButton.IsVisible = false;

				// Show extraction diagnostic for ThirdGen
				if (_thirdGenReader?.LastExtractInfo != null)
					BitmapInfoText.Text += $" | {_thirdGenReader.LastExtractInfo}";
			}
			catch (Exception ex)
			{
				ShowStatus($"Error creating preview image: {ex.Message}");
			}
		});
	}

	private async void BrowseSharedMap_Click(object? sender, RoutedEventArgs e)
	{
		if (_pendingBrowseInfo == null)
			return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null)
			return;

		try
		{
			var dialog = await topLevel.StorageProvider.OpenFilePickerAsync(
				new Avalonia.Platform.Storage.FilePickerOpenOptions
				{
					Title = "Locate shared map file",
					AllowMultiple = false,
					FileTypeFilter = new[]
					{
						new Avalonia.Platform.Storage.FilePickerFileType("Halo Map Files")
						{
							Patterns = new[] { "*.map" }
						}
					}
				});

			if (dialog == null || dialog.Count == 0)
				return;

			string chosenPath = dialog[0].TryGetLocalPath();
			if (string.IsNullOrEmpty(chosenPath))
				return;

			int sourceIndex = SecondGenBitmapTagReader.GetActiveSourceIndex(_pendingBrowseInfo);
			_sharedMapOverrides[sourceIndex] = chosenPath;

			// Notify caller so the path can be persisted and shown in the sidebar
			OnSharedMapSelected?.Invoke(sourceIndex, chosenPath);

			// Retry decode with the new path
			var info = _pendingBrowseInfo;
			_pendingBrowseInfo = null;
			StatusPanel.IsVisible = false;
			BrowseSharedMapButton.IsVisible = false;

			Task.Run(() =>
			{
				try
				{
					DecodeAndDisplay(info);
				}
				catch (Exception ex)
				{
					Dispatcher.UIThread.Post(() => ShowStatus($"Error decoding bitmap: {ex.Message}"));
				}
			});
		}
		catch (Exception ex)
		{
			ShowStatus($"Browse failed: {ex.Message}");
		}
	}

	private async void ExtractPng_Click(object? sender, RoutedEventArgs e)
	{
		if (_currentBitmap == null)
			return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null)
			return;

		int index = BitmapSelector.SelectedIndex;
		string defaultName = $"{System.IO.Path.GetFileNameWithoutExtension(_tag.TagFileName)}_{index}.png";

		var file = await topLevel.StorageProvider.SaveFilePickerAsync(
			new FilePickerSaveOptions
			{
				Title = "Extract as PNG",
				SuggestedFileName = defaultName,
				FileTypeChoices = new[]
				{
					new FilePickerFileType("PNG Images") { Patterns = new[] { "*.png" } }
				}
			});

		if (file == null)
			return;

		try
		{
			using (var stream = await file.OpenWriteAsync())
			{
				_currentBitmap.Save(stream);
			}
			_parentWindow?.SetStatus($"PNG extracted to {file.Name}");
		}
		catch (Exception ex)
		{
			_parentWindow?.SetStatus($"PNG extract failed: {ex.Message}");
		}
	}

	private async void ExtractDds_Click(object? sender, RoutedEventArgs e)
	{
		if (_currentRawData == null || _currentBitmapInfo == null)
			return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null)
			return;

		int index = BitmapSelector.SelectedIndex;
		string defaultName = $"{System.IO.Path.GetFileNameWithoutExtension(_tag.TagFileName)}_{index}.dds";

		var file = await topLevel.StorageProvider.SaveFilePickerAsync(
			new FilePickerSaveOptions
			{
				Title = "Extract as DDS",
				SuggestedFileName = defaultName,
				FileTypeChoices = new[]
				{
					new FilePickerFileType("DDS Textures") { Patterns = new[] { "*.dds" } }
				}
			});

		if (file == null)
			return;

		try
		{
			using (var stream = await file.OpenWriteAsync())
			{
				WriteDds(stream, _currentBitmapInfo, _currentRawData);
			}
			_parentWindow?.SetStatus($"DDS extracted to {file.Name}");
		}
		catch (Exception ex)
		{
			_parentWindow?.SetStatus($"DDS extract failed: {ex.Message}");
		}
	}

	private static void WriteDds(System.IO.Stream stream, BitmapInfo info, byte[] pixelData)
	{
		using var writer = new System.IO.BinaryWriter(stream);

		// DDS magic
		writer.Write(0x20534444); // "DDS "

		// DDS_HEADER (124 bytes)
		writer.Write(124);         // dwSize
		uint flags = 0x1 | 0x2 | 0x4 | 0x1000; // CAPS | HEIGHT | WIDTH | PIXELFORMAT
		if (info.IsCompressed)
			flags |= 0x80000; // LINEARSIZE
		else
			flags |= 0x8; // PITCH
		writer.Write(flags);       // dwFlags
		writer.Write(info.Height); // dwHeight
		writer.Write(info.Width);  // dwWidth

		if (info.IsCompressed)
			writer.Write(pixelData.Length); // dwPitchOrLinearSize
		else
			writer.Write(info.Width * info.BytesPerPixel); // dwPitchOrLinearSize

		writer.Write(0);           // dwDepth
		writer.Write(0);           // dwMipMapCount
		for (int i = 0; i < 11; i++)
			writer.Write(0);       // dwReserved1[11]

		// DDS_PIXELFORMAT (32 bytes)
		writer.Write(32);          // dwSize
		uint pfFlags;
		uint fourCC = 0;
		uint rgbBitCount = 0;
		uint rMask = 0, gMask = 0, bMask = 0, aMask = 0;

		switch (info.Format)
		{
			case BitmapFormat.DXT1:
				pfFlags = 0x4; // FOURCC
				fourCC = 0x31545844; // "DXT1"
				break;
			case BitmapFormat.DXT3:
				pfFlags = 0x4;
				fourCC = 0x33545844; // "DXT3"
				break;
			case BitmapFormat.DXT5:
				pfFlags = 0x4;
				fourCC = 0x35545844; // "DXT5"
				break;
			case BitmapFormat.A8R8G8B8:
				pfFlags = 0x1 | 0x40; // ALPHAPIXELS | RGB
				rgbBitCount = 32;
				aMask = 0xFF000000; rMask = 0x00FF0000; gMask = 0x0000FF00; bMask = 0x000000FF;
				break;
			case BitmapFormat.X8R8G8B8:
				pfFlags = 0x40; // RGB
				rgbBitCount = 32;
				rMask = 0x00FF0000; gMask = 0x0000FF00; bMask = 0x000000FF;
				break;
			case BitmapFormat.R5G6B5:
				pfFlags = 0x40;
				rgbBitCount = 16;
				rMask = 0xF800; gMask = 0x07E0; bMask = 0x001F;
				break;
			case BitmapFormat.A1R5G5B5:
				pfFlags = 0x1 | 0x40;
				rgbBitCount = 16;
				aMask = 0x8000; rMask = 0x7C00; gMask = 0x03E0; bMask = 0x001F;
				break;
			case BitmapFormat.A4R4G4B4:
				pfFlags = 0x1 | 0x40;
				rgbBitCount = 16;
				aMask = 0xF000; rMask = 0x0F00; gMask = 0x00F0; bMask = 0x000F;
				break;
			case BitmapFormat.A8:
				pfFlags = 0x2; // ALPHA
				rgbBitCount = 8;
				aMask = 0xFF;
				break;
			default:
				// For unsupported formats, write raw data with generic 8-bit luminance header
				pfFlags = 0x20000; // LUMINANCE
				rgbBitCount = 8;
				rMask = 0xFF;
				break;
		}

		writer.Write(pfFlags);     // dwFlags
		writer.Write(fourCC);      // dwFourCC
		writer.Write(rgbBitCount); // dwRGBBitCount
		writer.Write(rMask);       // dwRBitMask
		writer.Write(gMask);       // dwGBitMask
		writer.Write(bMask);       // dwBBitMask
		writer.Write(aMask);       // dwABitMask

		// Caps
		writer.Write(0x1000);      // dwCaps (TEXTURE)
		writer.Write(0);           // dwCaps2
		writer.Write(0);           // dwCaps3
		writer.Write(0);           // dwCaps4
		writer.Write(0);           // dwReserved2

		// Pixel data
		writer.Write(pixelData);
	}

	private async void InjectDds_Click(object? sender, RoutedEventArgs e)
	{
		if (_currentRawData == null || _currentBitmapInfo == null)
			return;
		if (_cacheFile.Engine != EngineType.SecondGeneration)
			return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null)
			return;

		var dialog = await topLevel.StorageProvider.OpenFilePickerAsync(
			new FilePickerOpenOptions
			{
				Title = "Select DDS file to inject",
				AllowMultiple = false,
				FileTypeFilter = new[]
				{
					new FilePickerFileType("DDS Textures") { Patterns = new[] { "*.dds" } }
				}
			});

		if (dialog == null || dialog.Count == 0)
			return;

		string ddsPath = dialog[0].TryGetLocalPath();
		if (string.IsNullOrEmpty(ddsPath))
			return;

		try
		{
			byte[] ddsBytes = System.IO.File.ReadAllBytes(ddsPath);
			var parsed = ParseDds(ddsBytes);

			// Resolve where the pixel data lives in the map file
			var tagReader = new SecondGenBitmapTagReader();
			var location = tagReader.ResolvePixelDataLocation(
				_currentBitmapInfo, _filePath, _sharedMapOverrides);

			if (location == null)
			{
				ShowStatus("Cannot inject DDS: unable to resolve pixel data location in the map file.");
				return;
			}

			var (fileOffset, dataSize, sourceFilePath) = location.Value;

			string validationError = ValidateDdsForInjection(parsed, _currentBitmapInfo, dataSize);
			if (validationError != null)
			{
				ShowStatus($"Cannot inject DDS: {validationError}");
				return;
			}

			// Write pixel data to the map file
			using (var stream = System.IO.File.Open(sourceFilePath, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite))
			{
				stream.Seek(fileOffset, System.IO.SeekOrigin.Begin);
				stream.Write(parsed.PixelData, 0, parsed.PixelData.Length);
			}

			int sourceIndex = SecondGenBitmapTagReader.GetActiveSourceIndex(_currentBitmapInfo);
			string sourceName = SecondGenBitmapTagReader.GetSourceDescription(sourceIndex << 30);
			_parentWindow?.SetStatus(
				$"DDS injected to {sourceName} at offset 0x{fileOffset:X} ({parsed.PixelData.Length} bytes).");

			// Refresh the preview
			var info = _currentBitmapInfo;
			StatusPanel.IsVisible = false;
			await Task.Run(() =>
			{
				try { DecodeAndDisplay(info); }
				catch (Exception ex)
				{
					Dispatcher.UIThread.Post(() =>
						ShowStatus($"Injected successfully but preview refresh failed: {ex.Message}"));
				}
			});
		}
		catch (UnauthorizedAccessException)
		{
			ShowStatus("Cannot inject DDS: the target map file is read-only or locked by another process.");
		}
		catch (System.IO.IOException ioEx)
		{
			ShowStatus($"Cannot inject DDS: {ioEx.Message}");
		}
		catch (Exception ex)
		{
			ShowStatus($"Inject DDS failed: {ex.Message}");
		}
	}

	private class DdsParseResult
	{
		public int Width, Height;
		public BitmapFormat Format;
		public byte[]? PixelData;
		public string? Error;
	}

	private static DdsParseResult ParseDds(byte[] fileBytes)
	{
		var result = new DdsParseResult();

		if (fileBytes == null || fileBytes.Length < 128)
		{
			result.Error = "File is too small to be a valid DDS file.";
			return result;
		}

		uint magic = BitConverter.ToUInt32(fileBytes, 0);
		if (magic != 0x20534444) // "DDS "
		{
			result.Error = "Not a valid DDS file (bad magic).";
			return result;
		}

		uint headerSize = BitConverter.ToUInt32(fileBytes, 4);
		if (headerSize != 124)
		{
			result.Error = $"Invalid DDS header size: {headerSize} (expected 124).";
			return result;
		}

		result.Height = BitConverter.ToInt32(fileBytes, 12);
		result.Width = BitConverter.ToInt32(fileBytes, 16);

		// Pixel format at offset 76
		uint pfFlags = BitConverter.ToUInt32(fileBytes, 80);
		uint fourCC = BitConverter.ToUInt32(fileBytes, 84);
		uint rgbBitCount = BitConverter.ToUInt32(fileBytes, 88);
		uint rMask = BitConverter.ToUInt32(fileBytes, 92);
		uint gMask = BitConverter.ToUInt32(fileBytes, 96);
		uint bMask = BitConverter.ToUInt32(fileBytes, 100);
		uint aMask = BitConverter.ToUInt32(fileBytes, 104);

		if ((pfFlags & 0x4) != 0) // FOURCC
		{
			result.Format = fourCC switch
			{
				0x31545844 => BitmapFormat.DXT1,
				0x33545844 => BitmapFormat.DXT3,
				0x35545844 => BitmapFormat.DXT5,
				_ => BitmapFormat.A8 // placeholder, will error below
			};
			if (fourCC != 0x31545844 && fourCC != 0x33545844 && fourCC != 0x35545844)
			{
				byte[] ccBytes = BitConverter.GetBytes(fourCC);
				string ccStr = System.Text.Encoding.ASCII.GetString(ccBytes);
				result.Error = $"Unsupported DDS FourCC: \"{ccStr}\" (0x{fourCC:X8}).";
				return result;
			}
		}
		else if ((pfFlags & 0x40) != 0) // RGB
		{
			bool hasAlpha = (pfFlags & 0x1) != 0;
			if (rgbBitCount == 32 && rMask == 0xFF0000 && gMask == 0xFF00 && bMask == 0xFF)
				result.Format = hasAlpha ? BitmapFormat.A8R8G8B8 : BitmapFormat.X8R8G8B8;
			else if (rgbBitCount == 16 && rMask == 0xF800 && gMask == 0x7E0 && bMask == 0x1F)
				result.Format = BitmapFormat.R5G6B5;
			else if (rgbBitCount == 16 && rMask == 0x7C00 && gMask == 0x3E0 && bMask == 0x1F)
				result.Format = BitmapFormat.A1R5G5B5;
			else if (rgbBitCount == 16 && rMask == 0xF00 && gMask == 0xF0 && bMask == 0xF)
				result.Format = BitmapFormat.A4R4G4B4;
			else
			{
				result.Error = $"Unsupported DDS RGB format: {rgbBitCount}bpp, " +
				               $"R=0x{rMask:X}, G=0x{gMask:X}, B=0x{bMask:X}, A=0x{aMask:X}.";
				return result;
			}
		}
		else if ((pfFlags & 0x2) != 0 && rgbBitCount == 8) // ALPHA
		{
			result.Format = BitmapFormat.A8;
		}
		else if ((pfFlags & 0x20000) != 0 && rgbBitCount == 8) // LUMINANCE
		{
			result.Format = BitmapFormat.Y8;
		}
		else if ((pfFlags & 0x20000) != 0 && rgbBitCount == 16) // LUMINANCE + ALPHA
		{
			result.Format = BitmapFormat.A8Y8;
		}
		else
		{
			result.Error = $"Unsupported DDS pixel format: flags=0x{pfFlags:X}, {rgbBitCount}bpp.";
			return result;
		}

		result.PixelData = new byte[fileBytes.Length - 128];
		Array.Copy(fileBytes, 128, result.PixelData, 0, result.PixelData.Length);
		return result;
	}

	private static string ValidateDdsForInjection(DdsParseResult dds, BitmapInfo info, int expectedSize)
	{
		if (dds.Error != null)
			return dds.Error;
		if (dds.Format != info.Format)
			return $"Format mismatch: DDS is {dds.Format}, bitmap expects {info.Format}.";
		if (dds.Width != info.Width)
			return $"Width mismatch: DDS is {dds.Width}, bitmap expects {info.Width}.";
		if (dds.Height != info.Height)
			return $"Height mismatch: DDS is {dds.Height}, bitmap expects {info.Height}.";
		if (dds.PixelData.Length != expectedSize)
			return $"Data size mismatch: DDS has {dds.PixelData.Length} bytes, bitmap expects {expectedSize} bytes.";
		return null;
	}

	private void ShowMeta_Click(object? sender, RoutedEventArgs e)
	{
		ShowMetaEditor?.Invoke();
	}

	private void ShowStatus(string message)
	{
		StatusText.Text = message;
		StatusPanel.IsVisible = true;
	}
}
