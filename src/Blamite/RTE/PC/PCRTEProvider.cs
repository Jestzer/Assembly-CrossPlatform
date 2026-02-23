using Blamite.Blam;
using Blamite.IO;
using Blamite.RTE.PC.Native;
using Blamite.Serialization;
using Blamite.Util;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Blamite.RTE.PC
{
	public abstract class PCRTEProvider : RTEProvider
	{
		protected long _baseAddress;
		protected long _mapHeaderAddress;
		protected long _mapMagicAddress;
		protected int _lastGamePid;

		public PCRTEProvider(EngineDescription engine) : base(engine)
		{
		}

		/// <summary>
		///     Gets the address of the cache for the currently-loaded (non-shared) map.
		/// </summary>
		public long CurrentCacheAddress { get; internal set; }

		/// <summary>
		///     Gets the base address of the game module used for the last connection.
		/// </summary>
		public long ModuleBaseAddress => _baseAddress;

		/// <summary>
		///     Gets the PID of the game process used for the last connection.
		/// </summary>
		public int LastGamePid => _lastGamePid;

		/// <summary>
		///     Gets the type of the map that is currently loaded.
		/// </summary>
		public CacheFileType CurrentMapType { get; private set; }

		/// <summary>
		///     Gets the name of the map that is currently loaded.
		/// </summary>
		public string CurrentMapName { get; private set; }

		/// <summary>
		///     Gets the full header of the map that is currently loaded.
		/// </summary>
		public byte[] CurrentMapHeader { get; private set; }

		// The size of the map header in bytes
		protected int MapHeaderSize;

		// Offset (from the start of the map header) of the int32 indicating the map type
		protected int MapTypeOffset;

		// Offset (from the start of the map header) of the ASCII string indicating the map name
		protected int MapNameOffset;

		/// <summary>
		///		Verifies if necessary information is present in Engines.xml for the engine to allow for poking.
		/// </summary>
		/// <returns>Whether required data is present. If not, <seealso cref="ErrorMessage"/> will contain the reason.</returns>
		protected bool CheckBuildInfo()
		{
			if (string.IsNullOrEmpty(_buildInfo.PokingExecutable))
			{
				ErrorMessage = "No gameExecutable value found in Engines.xml for engine " + _buildInfo.Name + ".";
				return false;
			}
			if (_buildInfo.Poking == null)
			{
				ErrorMessage = "No poking definitions found in Engines.xml for engine " + _buildInfo.Name + ".";
				return false;
			}

			return true;
		}

		/// <summary>
		///		Gets the version of the game process and looks for a poking definition for it.
		/// </summary>
		/// <param name="gameProcess">The game process to check.</param>
		/// <param name="gameModule">Optional. The game module (from the given process) to check. Can be null.</param>
		/// <returns>The poking definition for the version if found, null if not, with <seealso cref="ErrorMessage"/> containing the reason.</returns>
		protected PokingInformation RetrieveInformation(Process gameProcess, ProcessModule gameModule = null)
		{
			//verify version, and check for anticheat at the same time
			string version = "";
			_hadToGuessVersion = false;
			PokingInformation info = null;
			try
			{
				//check against module version if applicable in case there is a mismatch by the user for some reason
				if (gameModule != null)
					version = gameModule.FileVersionInfo.FileVersion;
				else
					version = gameProcess.MainModule.FileVersionInfo.FileVersion;

				info = _buildInfo.Poking.RetrieveInformation(version);

				if (info == null)
				{
					//version couldn't be found for whatever reason, so fall back to latest definition and prepare to warn the user that we did so if any errors occur.
					info = _buildInfo.Poking.RetrieveLatestInfo();

					if (info == null)
					{
						ErrorMessage = "Game version " + version + " does not have poking information defined in the Formats folder and no other poking definitions was available to fall back on.";
						return null;
					}
					_hadToGuessVersion = true;
				}
			}
			catch (System.ComponentModel.Win32Exception e)
			{
				ErrorMessage = "Cannot access game process. The following exception occured:\r\n\"" + e.Message + "\"\r\n\r\nThis could be due to Anti-Cheat or lack of admin privileges.";
				return null;
			}
			catch (Exception e)
			{
				// On Linux/Proton, different exceptions may occur when accessing process info.
				// Fall back to latest poking definition.
				info = _buildInfo.Poking.RetrieveLatestInfo();

				if (info == null)
				{
					ErrorMessage = "Could not determine game version and no fallback poking definitions are available. Exception: " + e.Message;
					return null;
				}
				_hadToGuessVersion = true;
			}

			return info;
		}

		/// <summary>
		///		Tries to find a running game process using <seealso cref="EngineDescription.PokingExecutable"/> and/or <seealso cref="EngineDescription.PokingExecutableAlt"/>.
		/// </summary>
		/// <returns>The first instance of the found process, otherwise null, with <seealso cref="ErrorMessage"/> containing the reason.</returns>
		protected Process FindGameProcess()
		{
			Process result = FindGameProcessByName(_buildInfo.PokingExecutable);
			if (result != null)
			{
				_lastGamePid = result.Id;
				return result;
			}

			if (!string.IsNullOrEmpty(_buildInfo.PokingExecutableAlt))
			{
				result = FindGameProcessByName(_buildInfo.PokingExecutableAlt);
				if (result != null)
				{
					_lastGamePid = result.Id;
					return result;
				}
				else
				{
					ErrorMessage = "Game process \"" + _buildInfo.PokingExecutable + "\" (or \"" + _buildInfo.PokingExecutableAlt + "\") does not appear to be running.";
					return null;
				}
			}
			else
			{
				ErrorMessage = "Game process \"" + _buildInfo.PokingExecutable + "\" does not appear to be running.";
				return null;
			}
		}

		private Process FindGameProcessByName(string name)
		{
			// On Linux, use /proc/cmdline scanning to avoid 15-char truncation
			// issues with Wine/Proton process names.
			// Pass PokingModule so that among multiple matching processes (reaper,
			// srt-bwrap, wine helpers), we prefer the one with the game DLL mapped.
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return LinuxProcessHelper.FindProcessByName(name, _buildInfo.PokingModule);

			Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name));

			if (processes.Length > 0)
				return processes[0];

			return null;
		}

		/// <summary>
		///		Tries to find a running game module using <seealso cref="EngineDescription.PokingModule"/>.
		/// </summary>
		/// <param name="process">The process to search.</param>
		/// <param name="errorOccured">Indicates that <seealso cref="EngineDescription.PokingModule"/> was defined but was not found in the process, and that <seealso cref="ErrorMessage"/> was written to.</param>
		/// <returns>The first instance of the found module if applicable. Returns null if <seealso cref="EngineDescription.PokingModule"/> is undefined, or if it was defined but not found, in which case <paramref name="errorOccured"/> will be true and <seealso cref="ErrorMessage"/> contains the reason.</returns>
		protected ProcessModule FindGameModule(Process process, out bool errorOccured)
		{
			errorOccured = false;
			if (!string.IsNullOrEmpty(_buildInfo.PokingModule))
			{
				try
				{
					foreach (ProcessModule m in process.Modules)
						if (string.Equals(Path.GetFileNameWithoutExtension(m.ModuleName), _buildInfo.PokingModule, System.StringComparison.InvariantCultureIgnoreCase))
							return m;

					// On Linux, module enumeration may not find Wine/Proton PE modules.
					// Return null without error; CreateMemoryStream will fall back to /proc/maps.
					if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
						return null;

					ErrorMessage = "Game process \"" + _buildInfo.PokingExecutable + "\" does not appear to be currently running any module named \"" + _buildInfo.PokingModule + "\".";
					errorOccured = true;
				}
				catch (System.ComponentModel.Win32Exception e)
				{
					ErrorMessage = "Cannot access game process. The following exception occured:\r\n\"" + e.Message + "\"\r\n\r\nThis could be due to Anti-Cheat or lack of admin privileges.";
					errorOccured = true;

					return null;
				}
			}

			return null;
		}

		/// <summary>
		///     Creates a ProcessMemoryStream for the given process and module.
		///     On Linux, falls back to /proc/maps if the ProcessModule is unavailable.
		/// </summary>
		protected ProcessMemoryStream CreateMemoryStream(Process gameProcess, ProcessModule gameModule)
		{
			if (gameModule != null || RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return ProcessMemoryStream.Create(gameProcess, gameModule);

			// Linux fallback: try to find the module via /proc/maps
			string moduleName = _buildInfo.PokingModule ?? _buildInfo.PokingExecutable;
			long baseAddr = LinuxProcessHelper.FindModuleBaseAddress(gameProcess.Id, moduleName);
			int moduleSize = LinuxProcessHelper.FindModuleMemorySize(gameProcess.Id, moduleName);

			if (baseAddr != 0)
				return ProcessMemoryStream.Create(gameProcess, baseAddr, moduleSize);

			// Last resort: use main module (may point to wine-preloader on Proton)
			return ProcessMemoryStream.Create(gameProcess, null);
		}

		protected void GetLayoutConstants(EngineDescription engineInfo)
		{
			MapHeaderSize = engineInfo.HeaderSize;

			var layout = engineInfo.Layouts.GetLayout("header");
			MapTypeOffset = layout.GetFieldOffset("type");
			MapNameOffset = layout.GetFieldOffset("internal name");
		}

		protected void ReadInformation(EndianReader reader, EngineDescription engineInfo)
		{
			GetLayoutConstants(engineInfo);

			if (engineInfo.PokingPlatform == RTEConnectionType.LocalProcess32)
				ReadMapPointers32(reader);
			else
				ReadMapPointers64(reader);

			ReadMapHeader(reader);
			ProcessMapHeader();
		}

		protected abstract void ReadMapPointers32(IReader reader);
		protected abstract void ReadMapPointers64(IReader reader);

		protected void ReadMapHeader(IReader reader)
		{
			reader.SeekTo(_mapHeaderAddress);
			CurrentMapHeader = reader.ReadBlock(MapHeaderSize);
		}

		protected void ProcessMapHeader()
		{
			using (IReader reader = new EndianStream(new MemoryStream(CurrentMapHeader), Endian.LittleEndian))
			{
				reader.SeekTo(MapTypeOffset);
				CurrentMapType = (CacheFileType)reader.ReadInt32();

				reader.SeekTo(MapNameOffset);
				CurrentMapName = reader.ReadAscii();

			}
		}

		protected static readonly int MapHeaderMagic = CharConstant.FromString("head");
	}
}
