using Blamite.Blam;
using Blamite.IO;
using Blamite.RTE.PC.Native;
using Blamite.Serialization;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Blamite.RTE.PC
{
	public class PCThirdGenRTEProvider : PCRTEProvider
	{
		/// <summary>
		///     Constructs a new PCThirdGenRTEProvider.
		/// </summary>
		public PCThirdGenRTEProvider(EngineDescription engine) : base(engine)
		{
		}

		/// <summary>
		///     The type of connection that the provider will establish.
		/// </summary>
		public override RTEConnectionType ConnectionType
		{
			get { return _buildInfo.PokingPlatform; }
		}

		/// <summary>
		///     Obtains a stream which can be used to read and write a cache file's meta in realtime.
		///     The stream will be set up such that offsets in the stream correspond to meta pointers in the cache file.
		/// </summary>
		/// <param name="cacheFile">The cache file to get a stream for.</param>
		/// <param name="tag">The tag to be poked; only needed for Eldorado.</param>
		/// <returns>The stream if it was opened successfully, or null otherwise.</returns>
		public override IStream GetCacheStream(ICacheFile cacheFile = null, ITag tag = null)
		{
			if (!CheckBuildInfo())
				return null; //ErrorMessage was handled by above.

			Process gameProcess = FindGameProcess();
			if (gameProcess == null)
				return null; //ErrorMessage was handled by above.

			ProcessModule gameModule = FindGameModule(gameProcess, out bool moduleError);
			if (moduleError)
				return null; //ErrorMessage was handled by above.

			PokingInformation info = RetrieveInformation(gameProcess, gameModule);
			if (info == null)
				return null; //ErrorMessage was handled by above.

			if ((!info.HeaderPointer.HasValue || !info.MagicOffset.HasValue) && (!info.HeaderAddress.HasValue || !info.MagicAddress.HasValue))
			{
				ErrorMessage = "Third Generation poking requires either HeaderAddress and MagicAddress values (Halo 3, ODST), or HeaderPointer and MagicOffset values (Reach and later).";
				return null;
			}

			ProcessMemoryStream gameMemory = CreateMemoryStream(gameProcess, gameModule);

			_baseAddress = gameMemory.ModuleBaseAddress;

			var reader = new EndianReader(gameMemory, BitConverter.IsLittleEndian ? Endian.LittleEndian : Endian.BigEndian);

			if (info.HeaderPointer.HasValue)
			{
				reader.SeekTo(_baseAddress + info.HeaderPointer.Value);

				long address;
				if (_buildInfo.PokingPlatform == RTEConnectionType.LocalProcess32)
				{
					address = reader.ReadUInt32();
					_mapHeaderAddress = address + 0x8;
				}
				else
				{
					address = reader.ReadInt64();
					_mapHeaderAddress = address + 0x10;
				}

				_mapMagicAddress = address + _buildInfo.HeaderSize + info.MagicOffset.Value;
			}
			else
			{
				_mapHeaderAddress = _baseAddress + info.HeaderAddress.Value;
				_mapMagicAddress = _baseAddress + info.MagicAddress.Value;
			}

			ReadInformation(reader, _buildInfo);

			long memoryAddress = CurrentCacheAddress;

			// On Linux/Proton, the poking XML offsets may be wrong for the installed
			// game version (the version can't be detected under Wine). If the cache
			// address from the magicAddress offset isn't accessible, scan the module
			// data near the headerAddress for the correct pointer.
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
				memoryAddress != 0 && !IsAddressReadable(gameProcess.Id, memoryAddress))
			{
				long recovered = ScanForCacheAddress(gameProcess.Id, reader);
				if (recovered != 0)
					memoryAddress = CurrentCacheAddress = recovered;
			}

			if (cacheFile != null && CurrentMapName != cacheFile.InternalName)
			{
				gameMemory.Close();
				if (string.IsNullOrEmpty(CurrentMapName))
					ErrorMessage = "Tried to poke map \"" + cacheFile.InternalName + "\" but the game is not currently running any map." + GuessError;
				else
					ErrorMessage = "Tried to poke map \"" + cacheFile.InternalName + "\" but the game is currently in map \"" + CurrentMapName + "\"." + GuessError;
				return null;
			}

			if (memoryAddress == 0)
			{
				ErrorMessage = "Map file base memory address is reading as 0. Check your poking definition." + GuessError;
				return null;
			}

			// Adjust offset so virtual pointers from the cache file map to correct
			// process memory addresses. memoryAddress is where the meta data lives
			// in the game process, and MetaArea.BasePointer is the virtual base the
			// cache file uses for its pointers. The delta translates between them.
			long streamOffset = memoryAddress;
			if (cacheFile != null && cacheFile.MetaArea != null)
				streamOffset = memoryAddress - cacheFile.MetaArea.BasePointer;

			OffsetStream gameStream = new OffsetStream(gameMemory, streamOffset);
			return new EndianStream(gameStream, BitConverter.IsLittleEndian ? Endian.LittleEndian : Endian.BigEndian);
		}

		/// <summary>
		///     Scans module data near the header address for an 8-byte pointer that
		///     points to readable process memory. When the poking XML's magicAddress
		///     is wrong (e.g., game version mismatch on Linux/Proton), the correct
		///     pointer is typically within a few hundred bytes of the header address.
		/// </summary>
		private long ScanForCacheAddress(int pid, IReader reader)
		{
			// Scan ±4KB around the header address. The magic pointer is typically
			// very close to the header (e.g., 0x10 bytes before it).
			long scanStart = _mapHeaderAddress - 0x1000;
			long scanEnd = _mapHeaderAddress + 0x1000;
			long bestCandidate = 0;
			long bestDistance = long.MaxValue;

			for (long addr = scanStart; addr < scanEnd; addr += 8)
			{
				try
				{
					reader.SeekTo(addr);
					long val = reader.ReadInt64();

					// Must look like a valid 64-bit user-space address (high range)
					if (val <= 0x10000 || val > 0x7FFFFFFFFFFF)
						continue;

					// Must be page-aligned (cache allocations always are)
					if ((val & 0xFFF) != 0)
						continue;

					// Must be readable in the target process
					if (!IsAddressReadable(pid, val))
						continue;

					// Prefer the candidate closest to the header
					long distance = Math.Abs(addr - _mapHeaderAddress);
					if (distance < bestDistance)
					{
						bestDistance = distance;
						bestCandidate = val;
					}
				}
				catch
				{
					// Skip unreadable addresses in the module
				}
			}

			return bestCandidate;
		}

		/// <summary>
		///     Tests if an address is readable in a process via process_vm_readv.
		/// </summary>
		private static bool IsAddressReadable(int pid, long address)
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				try
				{
					return LinuxProcessHelper.ProbeAddress(pid, address);
				}
				catch
				{
					return false;
				}
			}
			return true; // On Windows, assume readable (errors caught by caller)
		}

		protected override void ReadMapPointers32(IReader reader)
		{
			reader.SeekTo(_mapMagicAddress);
			CurrentCacheAddress = reader.ReadUInt32();
		}

		protected override void ReadMapPointers64(IReader reader)
		{
			reader.SeekTo(_mapMagicAddress);
			CurrentCacheAddress = reader.ReadInt64();
		}
	}
}
