using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Blamite.RTE.PC.Native
{
	/// <summary>
	///     Windows implementation of ProcessMemoryStream using kernel32 ReadProcessMemory/WriteProcessMemory.
	/// </summary>
	public class WindowsProcessMemoryStream : ProcessMemoryStream
	{
		/// <summary>
		///     Constructs a new WindowsProcessMemoryStream.
		/// </summary>
		/// <param name="process">The process to access the memory of.</param>
		/// <param name="module">The process module to access. Can be null, where Process.MainModule will be used.</param>
		public WindowsProcessMemoryStream(Process process, ProcessModule module = null)
		{
			_process = process;
			_processModule = module ?? process.MainModule;
			_moduleBaseAddress = (long)_processModule.BaseAddress;
			_moduleMemorySize = _processModule.ModuleMemorySize;

			Position = _moduleBaseAddress;
		}

		public override unsafe int Read(byte[] buffer, int offset, int count)
		{
			UIntPtr bytesRead;

			count = Math.Min(count, buffer.Length - offset); // Make sure we don't overflow the buffer
			fixed (byte* pBuffer = buffer)
			{
				ReadProcessMemory(_process.Handle, (IntPtr)Position, pBuffer + offset, (UIntPtr)count, out bytesRead);
			}

			Position += (long)bytesRead;
			return (int)bytesRead;
		}

		public override unsafe void Write(byte[] buffer, int offset, int count)
		{
			UIntPtr bytesWritten;

			count = Math.Min(count, buffer.Length - offset); // Make sure we don't read beyond the buffer
			fixed (byte* pBuffer = buffer)
			{
				WriteProcessMemory(_process.Handle, (IntPtr)Position, pBuffer + offset, (UIntPtr)count, out bytesWritten);
			}

			Position += (long)bytesWritten;
		}

		#region Native Functions

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern unsafe bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte* lpBuffer,
			UIntPtr nSize, out UIntPtr lpNumberOfBytesRead);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern unsafe bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte* lpBuffer,
			UIntPtr nSize, out UIntPtr lpNumberOfBytesWritten);

		#endregion Native Functions
	}
}
