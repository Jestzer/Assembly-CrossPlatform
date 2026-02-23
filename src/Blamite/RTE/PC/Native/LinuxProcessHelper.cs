using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

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
		/// <returns>The first matching Process, or null if not found.</returns>
		public static Process FindProcessByName(string name)
		{
			string targetName = Path.GetFileNameWithoutExtension(name);

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

					byte[] cmdlineBytes = File.ReadAllBytes(cmdlinePath);
					if (cmdlineBytes.Length == 0)
						continue;

					// cmdline is null-delimited; argv[0] is the executable path
					int firstNull = Array.IndexOf(cmdlineBytes, (byte)0);
					int len = firstNull >= 0 ? firstNull : cmdlineBytes.Length;
					string argv0 = System.Text.Encoding.UTF8.GetString(cmdlineBytes, 0, len);

					string processName = Path.GetFileNameWithoutExtension(argv0);
					if (string.Equals(processName, targetName, StringComparison.OrdinalIgnoreCase))
						return Process.GetProcessById(pid);
				}
				catch (Exception)
				{
					// Process may have exited or we may lack permissions; skip
					continue;
				}
			}

			return null;
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

					// Check if this line contains the module name (case-insensitive)
					if (line.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) < 0)
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

					if (line.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) < 0)
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
	}
}
