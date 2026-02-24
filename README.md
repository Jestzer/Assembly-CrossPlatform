# Assembly Crossplatform

### Cross-platform Halo Map Editor

Assembly Crossplatform is a free, open-source Halo map file (.map) editor built with [Avalonia UI](https://avaloniaui.net/) for Windows, Linux, and macOS. It is a cross-platform port of the original [Assembly](https://github.com/XboxChaos/Assembly) by Xbox Chaos, rewritten to run natively on all major desktop platforms.

## Features

* **Cross-platform** - Runs on Windows, Linux, and macOS using .NET 8 and Avalonia UI
* **Multi-generation support** - Opens cache files for Halo CE, Halo 2, Halo 3, Halo: Reach, Halo 4, and Halo MCC
* **Tag editing** - Browse and edit tag metadata with a searchable field editor, hex viewer, and real-time memory poking
* **Tag swapping** - Swap tags between datum indices directly from the editor
* **Map compression** - Compress and decompress map files (supports multiple Halo engine formats)
* **Real-time editing (RTE)** - Poke changes directly to game memory on supported engines

## Downloading

Precompiled builds for Windows and Linux are available on the [Releases](https://github.com/Jestzer/Assembly-CrossPlatform/releases) page.

## Building from Source

### Requirements
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build
```bash
dotnet build src/AssemblyAvalonia/AssemblyAvalonia.csproj
```

### Run
```bash
dotnet run --project src/AssemblyAvalonia/AssemblyAvalonia.csproj
```

### Publish (self-contained)
```bash
# Linux
dotnet publish src/AssemblyAvalonia/AssemblyAvalonia.csproj -c Release -r linux-x64 --self-contained

# Windows
dotnet publish src/AssemblyAvalonia/AssemblyAvalonia.csproj -c Release -r win-x64 --self-contained
```

## Bug Reports

If you encounter any issues, please submit bug reports through the [issue tracker](https://github.com/Jestzer/Assembly-CrossPlatform/issues/new). Include any error messages, what map the error occurred on, and steps to reproduce the issue.

## License

Licensed under GPL-3.0. See [LICENSE](LICENSE) for details.

## Credits

This cross-platform port builds on the original Assembly by the Xbox Chaos community. See the About window in the application for full credits.
