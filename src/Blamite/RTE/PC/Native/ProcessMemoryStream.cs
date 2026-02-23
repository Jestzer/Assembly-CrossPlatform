using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Blamite.RTE.PC.Native
{
	/// <summary>
	///     A Stream which reads/writes another process's memory.
	///     Platform-specific implementations handle the actual memory access.
	/// </summary>
	public abstract class ProcessMemoryStream : Stream
	{
		protected Process _process;
		protected ProcessModule _processModule;
		protected long _moduleBaseAddress;
		protected int _moduleMemorySize;

		/// <summary>
		///     Gets the process that the stream operates on.
		/// </summary>
		public Process BaseProcess
		{
			get { return _process; }
		}

		/// <summary>
		///     Gets the module in the process that the stream operates on. May be null on Linux.
		/// </summary>
		public ProcessModule BaseModule
		{
			get { return _processModule; }
		}

		/// <summary>
		///     Gets the base address of the module in the process's memory space.
		/// </summary>
		public long ModuleBaseAddress
		{
			get { return _moduleBaseAddress; }
		}

		/// <summary>
		///     Gets the memory size of the module.
		/// </summary>
		public int ModuleMemorySize
		{
			get { return _moduleMemorySize; }
		}

		public override bool CanRead
		{
			get { return true; }
		}

		public override bool CanSeek
		{
			get { return true; }
		}

		public override bool CanWrite
		{
			get { return true; }
		}

		public override long Length
		{
			get { return _moduleMemorySize; }
		}

		public override long Position { get; set; }

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			switch (origin)
			{
				case SeekOrigin.Begin:
					Position = offset;
					break;

				case SeekOrigin.Current:
					Position += offset;
					break;

				case SeekOrigin.End:
					Position = _moduleBaseAddress + _moduleMemorySize - offset;
					break;
			}
			return Position;
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		/// <summary>
		///     Creates a ProcessMemoryStream appropriate for the current platform.
		/// </summary>
		/// <param name="process">The process to access the memory of.</param>
		/// <param name="module">The process module to access. Can be null, where the main module will be used.</param>
		/// <returns>A platform-specific ProcessMemoryStream.</returns>
		public static ProcessMemoryStream Create(Process process, ProcessModule module = null)
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return new WindowsProcessMemoryStream(process, module);
			else
				return new LinuxProcessMemoryStream(process, module);
		}

		/// <summary>
		///     Creates a ProcessMemoryStream with an explicit base address (Linux only).
		///     Used when ProcessModule is unavailable for Wine/Proton processes.
		/// </summary>
		/// <param name="process">The process to access the memory of.</param>
		/// <param name="baseAddress">The base address of the module in the process's memory space.</param>
		/// <param name="moduleSize">The size of the module in memory.</param>
		/// <returns>A LinuxProcessMemoryStream.</returns>
		public static ProcessMemoryStream Create(Process process, long baseAddress, int moduleSize)
		{
			return new LinuxProcessMemoryStream(process, baseAddress, moduleSize);
		}
	}
}
