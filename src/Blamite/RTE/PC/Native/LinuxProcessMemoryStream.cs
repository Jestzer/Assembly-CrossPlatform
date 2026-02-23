using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Blamite.RTE.PC.Native
{
	/// <summary>
	///     Linux implementation of ProcessMemoryStream using process_vm_readv/process_vm_writev.
	///     These syscalls (available since Linux 3.2) allow reading/writing another process's
	///     memory without ptrace, provided the caller has appropriate permissions.
	/// </summary>
	public class LinuxProcessMemoryStream : ProcessMemoryStream
	{
		private readonly int _pid;

		/// <summary>
		///     Constructs a new LinuxProcessMemoryStream.
		/// </summary>
		/// <param name="process">The process to access the memory of.</param>
		/// <param name="module">The process module to access. Can be null, where Process.MainModule will be used.</param>
		public LinuxProcessMemoryStream(Process process, ProcessModule module = null)
		{
			_process = process;
			_pid = process.Id;
			_processModule = module ?? process.MainModule;

			if (_processModule != null)
			{
				_moduleBaseAddress = (long)_processModule.BaseAddress;
				_moduleMemorySize = _processModule.ModuleMemorySize;
			}

			Position = _moduleBaseAddress;
		}

		/// <summary>
		///     Constructs a new LinuxProcessMemoryStream with an explicit base address.
		///     Used when ProcessModule is unavailable (e.g., Wine/Proton processes where
		///     the module info was obtained from /proc/[pid]/maps).
		/// </summary>
		/// <param name="process">The process to access the memory of.</param>
		/// <param name="baseAddress">The base address of the module in the process's memory space.</param>
		/// <param name="moduleSize">The size of the module in memory.</param>
		public LinuxProcessMemoryStream(Process process, long baseAddress, int moduleSize)
		{
			_process = process;
			_pid = process.Id;
			_processModule = null;
			_moduleBaseAddress = baseAddress;
			_moduleMemorySize = moduleSize;

			Position = _moduleBaseAddress;
		}

		public override unsafe int Read(byte[] buffer, int offset, int count)
		{
			count = Math.Min(count, buffer.Length - offset); // Make sure we don't overflow the buffer

			fixed (byte* pBuffer = buffer)
			{
				var localIov = new Iovec
				{
					iov_base = (IntPtr)(pBuffer + offset),
					iov_len = (IntPtr)count
				};
				var remoteIov = new Iovec
				{
					iov_base = (IntPtr)Position,
					iov_len = (IntPtr)count
				};

				long bytesRead = process_vm_readv(_pid, ref localIov, 1, ref remoteIov, 1, 0);

				if (bytesRead < 0)
				{
					int errno = Marshal.GetLastPInvokeError();
					throw new IOException(
						$"process_vm_readv failed for PID {_pid} at address 0x{Position:X}: errno {errno}");
				}

				Position += bytesRead;
				return (int)bytesRead;
			}
		}

		public override unsafe void Write(byte[] buffer, int offset, int count)
		{
			count = Math.Min(count, buffer.Length - offset); // Make sure we don't read beyond the buffer

			fixed (byte* pBuffer = buffer)
			{
				var localIov = new Iovec
				{
					iov_base = (IntPtr)(pBuffer + offset),
					iov_len = (IntPtr)count
				};
				var remoteIov = new Iovec
				{
					iov_base = (IntPtr)Position,
					iov_len = (IntPtr)count
				};

				long bytesWritten = process_vm_writev(_pid, ref localIov, 1, ref remoteIov, 1, 0);

				if (bytesWritten < 0)
				{
					int errno = Marshal.GetLastPInvokeError();
					throw new IOException(
						$"process_vm_writev failed for PID {_pid} at address 0x{Position:X}: errno {errno}");
				}

				Position += bytesWritten;
			}
		}

		#region Native Functions

		[StructLayout(LayoutKind.Sequential)]
		private struct Iovec
		{
			public IntPtr iov_base;
			public IntPtr iov_len;
		}

		[DllImport("libc", SetLastError = true)]
		private static extern long process_vm_readv(
			int pid,
			ref Iovec local_iov, ulong liovcnt,
			ref Iovec remote_iov, ulong riovcnt,
			ulong flags);

		[DllImport("libc", SetLastError = true)]
		private static extern long process_vm_writev(
			int pid,
			ref Iovec local_iov, ulong liovcnt,
			ref Iovec remote_iov, ulong riovcnt,
			ulong flags);

		#endregion Native Functions
	}
}
