// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace System.Diagnostics.Interop;

public static partial class WindowsNative {
	public static partial class IO {
		private static readonly Process currentProcess = Process.GetCurrentProcess();

		public static DiskIoData GetDiskIo() {
			if (GetProcessIoCounters(currentProcess.Handle, out var counters)) {
				return new() {
					ReadBytes = counters.ReadTransferCount,
					WrittenBytes = counters.WriteTransferCount,
					ReadOps = counters.ReadOperationCount,
					WriteOps = counters.WriteOperationCount
				};
			}

			throw new Win32Exception();
		}

		#region . native .

		// http://msdn.microsoft.com/en-us/library/ms683218%28VS.85%29.aspx
		[StructLayout(LayoutKind.Sequential)]
		private struct IO_COUNTERS {
			public ulong ReadOperationCount;
			public ulong WriteOperationCount;
			public ulong OtherOperationCount;
			public ulong ReadTransferCount;
			public ulong WriteTransferCount;
			public ulong OtherTransferCount;
		}

		[return: MarshalAs(UnmanagedType.Bool)]
		[LibraryImport("kernel32.dll", SetLastError = true)]
		private static partial bool GetProcessIoCounters(IntPtr processHandle, out IO_COUNTERS ioCounters);

		#endregion
	}
}
