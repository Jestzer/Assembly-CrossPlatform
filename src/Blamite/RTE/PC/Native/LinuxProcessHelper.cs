using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Blamite.RTE.PC.Native
{
	/// <summary>
	///     Helper methods for finding processes and modules on Linux,
	///     particularly for games running under Wine/Proton where standard
	///     .NET process APIs may not work correctly.
	/// </summary>
	public static class LinuxProcessHelper
	{
		/// <summary>
		///     Finds a process by searching /proc/[pid]/cmdline for a matching executable name.
		///     This works around the 15-character truncation of /proc/[pid]/comm that causes
		///     Process.GetProcessesByName() to fail for long executable names like "MCC-Win64-Shipping".
		/// </summary>
		/// <param name="name">The executable name to search for (with or without extension).</param>
		/// <param name="moduleName">Optional module name that must be present in /proc/[pid]/maps.
		/// Under Proton, multiple processes (reaper, srt-bwrap, wine helpers) share the game
		/// name in their cmdline; only the actual game process has the game DLL mapped.</param>
		/// <returns>The matching Process, or null if not found.</returns>
		public static Process FindProcessByName(string name, string moduleName = null)
		{
			string targetName = Path.GetFileNameWithoutExtension(name);
			var matchingPids = new System.Collections.Generic.List<int>();

			foreach (string pidDir in Directory.EnumerateDirectories("/proc"))
			{
				string dirName = Path.GetFileName(pidDir);
				if (!int.TryParse(dirName, out int pid))
					continue;

				try
				{
					string cmdlinePath = Path.Combine(pidDir, "cmdline");
					if (!File.Exists(cmdlinePath))
						continue;

					// cmdline is null-delimited; under Proton, argv[0] is wine64,
					// and the game executable appears as a later argument.
					// Search the entire cmdline string for the target name.
					string cmdline = File.ReadAllText(cmdlinePath);
					if (cmdline.Length == 0)
						continue;

					if (cmdline.Contains(targetName, StringComparison.OrdinalIgnoreCase))
						matchingPids.Add(pid);
				}
				catch (Exception)
				{
					// Process may have exited or we may lack permissions; skip
					continue;
				}
			}

			if (matchingPids.Count == 0)
				return null;

			// If a module name is provided, prefer the process that has it mapped.
			// Under Proton, only the actual game process will have the game DLL loaded.
			if (!string.IsNullOrEmpty(moduleName))
			{
				foreach (int pid in matchingPids)
				{
					if (FindModuleBaseAddress(pid, moduleName) != 0)
						return Process.GetProcessById(pid);
				}
			}

			// Fallback: return the last (highest PID) match
			return Process.GetProcessById(matchingPids[matchingPids.Count - 1]);
		}

		/// <summary>
		///     Finds the base address of a module loaded in a process by parsing /proc/[pid]/maps.
		///     This is useful for Wine/Proton processes where Process.Modules may not correctly
		///     enumerate PE modules loaded by Wine.
		/// </summary>
		/// <param name="pid">The process ID.</param>
		/// <param name="moduleName">The module name to search for (e.g., "MCC-Win64-Shipping.exe").</param>
		/// <returns>The base address of the module, or 0 if not found.</returns>
		public static long FindModuleBaseAddress(int pid, string moduleName)
		{
			string targetName = Path.GetFileNameWithoutExtension(moduleName);
			string mapsPath = $"/proc/{pid}/maps";

			if (!File.Exists(mapsPath))
				return 0;

			try
			{
				foreach (string line in File.ReadLines(mapsPath))
				{
					// Format: "address-address perms offset dev inode pathname"
					// Example: "7f1234000000-7f1234001000 r--p 00000000 08:01 12345 /path/to/module.so"
					if (string.IsNullOrWhiteSpace(line))
						continue;

					// Match module name as a filename (e.g., "halo3.dll" or "halo3.so"),
					// not just as a substring (which could match directory names or
					// unrelated files like "halo3fonts.dat").
					if (!IsModuleMatch(line, targetName))
						continue;

					// Parse the start address (everything before the first dash)
					int dashIndex = line.IndexOf('-');
					if (dashIndex <= 0)
						continue;

					string addressStr = line.Substring(0, dashIndex);
					if (long.TryParse(addressStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long address))
						return address;
				}
			}
			catch (Exception)
			{
				// Process may have exited or maps may be unreadable
			}

			return 0;
		}

		/// <summary>
		///     Checks if a /proc/maps line contains a module matching the target name.
		///     Matches "targetName.dll", "targetName.so", or "targetName.so.X" as a
		///     filename component, preventing false matches on directory names or
		///     unrelated files that happen to contain the target string.
		/// </summary>
		private static bool IsModuleMatch(string line, string targetName)
		{
			// First check if the line contains the target name at all (fast path)
			int idx = line.IndexOf(targetName, StringComparison.OrdinalIgnoreCase);
			if (idx < 0)
				return false;

			// Check each occurrence to see if it's a proper filename match
			while (idx >= 0)
			{
				int afterName = idx + targetName.Length;
				if (afterName < line.Length)
				{
					char nextChar = line[afterName];
					// Must be followed by .dll, .so, or end of meaningful content
					if (nextChar == '.')
					{
						string rest = line.Substring(afterName).TrimEnd();
						if (rest.StartsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
							rest.StartsWith(".so", StringComparison.OrdinalIgnoreCase))
						{
							// Also verify it's preceded by a path separator or start of path
							if (idx == 0 || line[idx - 1] == '/' || line[idx - 1] == '\\')
								return true;
						}
					}
				}

				// Search for next occurrence
				idx = line.IndexOf(targetName, idx + 1, StringComparison.OrdinalIgnoreCase);
			}

			return false;
		}

		/// <summary>
		///     Finds the memory size of a module in a process by parsing /proc/[pid]/maps.
		///     Calculates the size from the first to last mapping containing the module name.
		/// </summary>
		/// <param name="pid">The process ID.</param>
		/// <param name="moduleName">The module name to search for.</param>
		/// <returns>The total memory size of the module's mappings, or 0 if not found.</returns>
		public static int FindModuleMemorySize(int pid, string moduleName)
		{
			string targetName = Path.GetFileNameWithoutExtension(moduleName);
			string mapsPath = $"/proc/{pid}/maps";

			if (!File.Exists(mapsPath))
				return 0;

			long firstStart = 0;
			long lastEnd = 0;

			try
			{
				foreach (string line in File.ReadLines(mapsPath))
				{
					if (string.IsNullOrWhiteSpace(line))
						continue;

					if (!IsModuleMatch(line, targetName))
						continue;

					// Parse address range "start-end"
					int dashIndex = line.IndexOf('-');
					if (dashIndex <= 0)
						continue;

					int spaceIndex = line.IndexOf(' ', dashIndex);
					if (spaceIndex <= 0)
						continue;

					string startStr = line.Substring(0, dashIndex);
					string endStr = line.Substring(dashIndex + 1, spaceIndex - dashIndex - 1);

					if (long.TryParse(startStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long startAddr) &&
						long.TryParse(endStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long endAddr))
					{
						if (firstStart == 0)
							firstStart = startAddr;
						lastEnd = endAddr;
					}
				}
			}
			catch (Exception)
			{
				// Process may have exited or maps may be unreadable
			}

			if (firstStart > 0 && lastEnd > firstStart)
				return (int)(lastEnd - firstStart);

			return 0;
		}

		/// <summary>
		///     Tests if an address is readable in another process using process_vm_readv.
		/// </summary>
		/// <param name="pid">The process ID.</param>
		/// <param name="address">The address to probe.</param>
		/// <returns>True if 4 bytes can be read from the address.</returns>
		public static unsafe bool ProbeAddress(int pid, long address)
		{
			byte[] buf = new byte[4];
			fixed (byte* pBuf = buf)
			{
				var localIov = new Iovec { iov_base = (IntPtr)pBuf, iov_len = (IntPtr)4 };
				var remoteIov = new Iovec { iov_base = (IntPtr)address, iov_len = (IntPtr)4 };
				long result = process_vm_readv(pid, ref localIov, 1, ref remoteIov, 1, 0);
				return result > 0;
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct Iovec
		{
			public IntPtr iov_base;
			public IntPtr iov_len;
		}

		[DllImport("libc", SetLastError = true)]
		private static extern long process_vm_readv(
			int pid, ref Iovec local_iov, ulong liovcnt,
			ref Iovec remote_iov, ulong riovcnt, ulong flags);
	}
}
